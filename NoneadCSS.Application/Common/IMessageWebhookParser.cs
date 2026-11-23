using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Common;

/// <summary>
/// iMessage 入站报文纯解析（无 IO，便于单测）。
/// 覆盖两种传输：
///   bridge — BlueBubbles webhook（事件 new-message / updated-message / message-error /
///            message-send-error / typing-indicator / read-receipt / chat-read-status /
///            group-name-change / participant-* / server-update / url-change / Hello World 测试）
///   amb    — Apple Messages for Business 的 POST /message（body 为 gzip 解压后的 JSON，头带 Source-Id/Destination-Id/Id）
/// 约定：入站按消息 guid 幂等去重；isFromMe=true 一律忽略；chat 身份按 §5 归一化（私聊收敛裸 handle，群聊保留 GUID）。
/// </summary>
public static class IMessageWebhookParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>判断报文是否像 BlueBubbles webhook（顶层有 type + data，且不是 WhatsApp/Messenger 形态）。</summary>
    public static bool IsBlueBubblesWebhook(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (doc.RootElement.TryGetProperty("object", out var obj)
                && obj.ValueKind == JsonValueKind.String) return false; // WhatsApp/Messenger 形态
            return doc.RootElement.TryGetProperty("type", out var type)
                   && type.ValueKind == JsonValueKind.String
                   && doc.RootElement.TryGetProperty("data", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>解析单事件报文（BlueBubbles 每个 POST 一个事件）。无法识别时 Kind=Unknown。</summary>
    public static IMessageParsedEvent ParseEvent(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new IMessageParsedEvent { Notice = "空报文" };

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return new IMessageParsedEvent { Notice = $"JSON 解析失败：{ex.Message}" };
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new IMessageParsedEvent { Notice = "顶层不是对象" };

            var kind = ReadString(root, "type") ?? IMessageEventKinds.Unknown;
            var data = root.TryGetProperty("data", out var d) ? d : default;

            // BlueBubbles 的 Hello World 测试事件：data 是字符串或带 text 的对象
            if (data.ValueKind == JsonValueKind.String)
            {
                return new IMessageParsedEvent
                {
                    Kind = kind,
                    Update = new IMessageEventUpdate { Kind = kind, DisplayName = data.GetString() },
                };
            }

            if (data.ValueKind != JsonValueKind.Object)
                return new IMessageParsedEvent { Kind = kind, Notice = "data 不是对象" };

            switch (kind)
            {
                case IMessageEventKinds.NewMessage:
                    {
                        var message = ParseNewMessageData(data);
                        return message == null
                            ? new IMessageParsedEvent { Kind = kind, Notice = "new-message 缺少 guid" }
                            : new IMessageParsedEvent { Kind = kind, Message = message };
                    }

                case IMessageEventKinds.UpdatedMessage:
                case IMessageEventKinds.MessageError:
                case IMessageEventKinds.MessageSendError:
                case IMessageEventKinds.TypingIndicator:
                case IMessageEventKinds.ReadReceipt:
                case IMessageEventKinds.ChatReadStatus:
                case IMessageEventKinds.GroupNameChange:
                case IMessageEventKinds.ParticipantAdded:
                case IMessageEventKinds.ParticipantRemoved:
                case IMessageEventKinds.ParticipantLeft:
                    return new IMessageParsedEvent { Kind = kind, Update = ParseUpdate(kind, data) };

                case IMessageEventKinds.ServerUpdate:
                case IMessageEventKinds.UrlChange:
                    return new IMessageParsedEvent { Kind = kind, Notice = "服务器级事件，不落会话" };

                default:
                    return new IMessageParsedEvent { Kind = IMessageEventKinds.Unknown, Notice = $"未知事件类型：{kind}" };
            }
        }
    }

    /// <summary>只取客户消息（new-message），并过滤自己的出站回声（isFromMe=true）。</summary>
    public static List<IMessageWebhookMessage> ParseNewMessages(string? payload)
    {
        var result = new List<IMessageWebhookMessage>();
        var evt = ParseEvent(payload);
        if (evt.Message is { IsFromMe: false } message) result.Add(message);
        return result;
    }

    /// <summary>按 guid 去重（保留首次出现）。持久化去重在 IngestionService 里做（分布式锁 + 线程 MessageId 校验）。</summary>
    public static List<IMessageWebhookMessage> DedupeByGuid(IEnumerable<IMessageWebhookMessage> messages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IMessageWebhookMessage>();
        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.MessageGuid)) continue;
            if (seen.Add(message.MessageGuid)) result.Add(message);
        }

        return result;
    }

    // ===== AMB（Apple Messages for Business）=====

    /// <summary>
    /// 解析 AMB 入站报文（已解压）。Apple 语义：400 = 头缺失/JSON 非法/destination-id 与 body 不一致；
    /// 因此这里同时做头与 body 的一致性校验，错误消息可直接用于回 400。
    /// </summary>
    public static IMessageParsedEvent ParseAmbInbound(
        string? payload,
        string? sourceIdHeader,
        string? destinationIdHeader,
        string? idHeader,
        out string? clientError)
    {
        clientError = null;

        if (string.IsNullOrWhiteSpace(sourceIdHeader)
            || string.IsNullOrWhiteSpace(destinationIdHeader)
            || string.IsNullOrWhiteSpace(idHeader))
        {
            clientError = "缺少 Source-Id / Destination-Id / Id 头";
            return new IMessageParsedEvent { Notice = clientError };
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload ?? string.Empty);
        }
        catch (JsonException)
        {
            clientError = "请求体不是合法 JSON";
            return new IMessageParsedEvent { Notice = clientError };
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                clientError = "请求体顶层不是对象";
                return new IMessageParsedEvent { Notice = clientError };
            }

            var bodySourceId = ReadString(root, "sourceId");
            var bodyDestinationId = ReadString(root, "destinationId");

            if (!string.IsNullOrWhiteSpace(bodySourceId)
                && !string.Equals(bodySourceId, sourceIdHeader, StringComparison.Ordinal))
            {
                clientError = "sourceId 与 Source-Id 头不一致";
                return new IMessageParsedEvent { Notice = clientError };
            }

            if (!string.IsNullOrWhiteSpace(bodyDestinationId)
                && !string.Equals(bodyDestinationId, destinationIdHeader, StringComparison.Ordinal))
            {
                clientError = "destinationId 与 Destination-Id 头不一致";
                return new IMessageParsedEvent { Notice = clientError };
            }

            var type = ReadString(root, "type") ?? "message";
            var guid = ReadString(root, "id") ?? idHeader!;

            // close：用户侧滑删除并确认「停止接收」⇒ 此后必须屏蔽坐席发送（后续发送会 410）
            if (type.Equals("close", StringComparison.OrdinalIgnoreCase))
            {
                return new IMessageParsedEvent
                {
                    Kind = "amb-close",
                    Update = new IMessageEventUpdate
                    {
                        Kind = "amb-close",
                        MessageGuid = guid,
                        ChatGuid = null,
                        // close 的 sourceId 有两种形态：活跃会话是不透明 id，仅收到邀请未接受则是 tel:+… 号码
                        DisplayName = bodySourceId ?? sourceIdHeader,
                    },
                };
            }

            var capabilityList = ReadString(root, "capabilityList") ?? ReadString(root, "capability-list");
            var message = new IMessageWebhookMessage
            {
                MessageGuid = guid,
                HandleAddress = bodySourceId ?? sourceIdHeader,
                ExternalId = NormalizeAmbSourceId(bodySourceId ?? sourceIdHeader!),
                Service = "iMessage",
                IsGroup = root.TryGetProperty("group", out var group) && group.ValueKind == JsonValueKind.True,
                TextBody = ReadString(root, "body"),
                MessageType = type.Equals("reaction", StringComparison.OrdinalIgnoreCase) ? "reaction" : "text",
                DateCreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                DisplayName = capabilityList,
            };

            if (root.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in attachments.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    message.Attachments.Add(new IMessageAttachmentRef
                    {
                        Guid = ReadString(item, "mmcs-url") ?? ReadString(item, "url") ?? ReadString(item, "id") ?? Guid.NewGuid().ToString("N"),
                        Filename = ReadString(item, "file-name") ?? ReadString(item, "fileName"),
                        MimeType = ReadString(item, "mime-type") ?? ReadString(item, "mimeType"),
                        TotalBytes = ReadLong(item, "file-size") ?? ReadLong(item, "fileSize") ?? 0,
                        DownloadUrl = ReadString(item, "mmcs-url") ?? ReadString(item, "url"),
                        Signature = ReadString(item, "signature-base64") ?? ReadString(item, "signature"),
                        Owner = ReadString(item, "mmcs-owner") ?? ReadString(item, "owner"),
                    });
                }

                if (message.Attachments.Count > 0) message.MessageType = "attachment";
            }

            return new IMessageParsedEvent { Kind = "amb-" + type, Message = message };
        }
    }

    /// <summary>
    /// AMB 客户 id 归一化：不透明 id 原样保留；`tel:+1…` 形态（只收到过邀请未接受）按电话归一。
    /// </summary>
    public static string NormalizeAmbSourceId(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return string.Empty;
        var value = sourceId.Trim();
        if (value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            return IMessageIdentity.NormalizeHandle(value[4..]);
        if (value.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
            return value; // 不透明 id / urn:mbid:… 不做任何变形
        return IMessageIdentity.NormalizeHandle(value);
    }

    /// <summary>
    /// 校验 MSP 回调的 HMAC（各家 MSP 口径不一：允许裸 hex 或 `sha256=` 前缀）。恒定时间比较。
    /// 必须先拿原始字节再比对，绝不能反序列化后重序列化。
    /// </summary>
    public static bool VerifyMspSignature(byte[] rawBody, string webhookSecret, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret) || string.IsNullOrWhiteSpace(signatureHeader)) return false;

        var provided = signatureHeader.Trim();
        if (provided.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            provided = provided["sha256=".Length..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var expected = Convert.ToHexString(hmac.ComputeHash(rawBody)).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided.ToLowerInvariant()));
    }

    // ===== 私有解析块 =====

    private static IMessageWebhookMessage? ParseNewMessageData(JsonElement data)
    {
        var guid = ReadString(data, "guid");
        if (string.IsNullOrWhiteSpace(guid)) return null;

        var message = new IMessageWebhookMessage
        {
            MessageGuid = guid,
            ChatGuid = ReadString(data, "chatGuid"),
            HandleAddress = ReadHandleAddress(data),
            IsFromMe = ReadBool(data, "isFromMe") ?? false,
            TextBody = ReadString(data, "text"),
            DateCreatedUnixMs = NormalizeUnixMsOrNull(ReadLong(data, "dateCreated") ?? ReadLong(data, "DateCreated"))
                ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        // chats[] 里取 chatGuid / chatIdentifier / 群标记；顶层 chats 缺失时退回 chatGuid
        string? chatIdentifier = null;
        bool? isGroup = null;
        string? displayName = null;

        if (data.TryGetProperty("chats", out var chats) && chats.ValueKind == JsonValueKind.Array)
        {
            foreach (var chat in chats.EnumerateArray())
            {
                if (chat.ValueKind != JsonValueKind.Object) continue;
                message.ChatGuid ??= ReadString(chat, "guid");
                chatIdentifier ??= ReadString(chat, "chatIdentifier");
                displayName ??= ReadString(chat, "name");
                var style = ReadInt(chat, "style");
                var groupFlag = ReadBool(chat, "isGroupChat");
                if (groupFlag == true || style == 2) isGroup = true;
            }
        }

        message.ChatIdentifier = chatIdentifier;
        message.DisplayName = displayName;
        message.Service = IMessageIdentity.GetServiceFromGuid(message.ChatGuid);

        var (externalId, group) = IMessageIdentity.ResolveExternalId(message.ChatGuid, chatIdentifier, message.HandleAddress, isGroup);
        message.IsGroup = group;
        message.ExternalId = externalId;

        if (data.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in attachments.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                message.Attachments.Add(new IMessageAttachmentRef
                {
                    Guid = ReadString(item, "guid") ?? string.Empty,
                    Filename = ReadString(item, "filename") ?? ReadString(item, "transferName"),
                    MimeType = ReadString(item, "mime_type") ?? ReadString(item, "mimeType"),
                    TotalBytes = ReadLong(item, "total_bytes") ?? ReadLong(item, "totalBytes") ?? 0,
                });
            }
        }

        message.MessageType = message.Attachments.Count > 0
            ? "attachment"
            : LooksLikeTapback(message.TextBody) ? "tapback" : "text";

        return message;
    }

    private static IMessageEventUpdate ParseUpdate(string kind, JsonElement data)
    {
        var update = new IMessageEventUpdate
        {
            Kind = kind,
            MessageGuid = ReadString(data, "guid") ?? ReadString(data, "rowid")?.ToString(),
            ChatGuid = ReadString(data, "chatGuid") ?? ReadString(data, "chat_GUID") ?? ReadString(data, "chat_guid"),
            DateReadUnixMs = NormalizeUnixMsOrNull(ReadLong(data, "dateRead")),
            IsFromMe = ReadBool(data, "isFromMe") ?? false,
            IsTyping = ReadBool(data, "isTyping"),
            DisplayName = ReadString(data, "displayname") ?? ReadString(data, "displayName"),
        };

        if (data.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in messages.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) update.MessageGuids.Add(item.GetString()!);
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var g = ReadString(item, "guid");
                    if (!string.IsNullOrWhiteSpace(g)) update.MessageGuids.Add(g!);
                }
            }
        }

        if (kind is IMessageEventKinds.MessageError or IMessageEventKinds.MessageSendError)
        {
            update.ErrorMessage = ExtractErrorMessage(data);
        }

        return update;
    }

    private static string? ExtractErrorMessage(JsonElement data)
    {
        if (data.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (error.ValueKind == JsonValueKind.Object)
                return ReadString(error, "error") ?? ReadString(error, "type") ?? error.GetRawText();
        }

        return ReadString(data, "reason");
    }

    private static string? ReadHandleAddress(JsonElement data)
    {
        if (!data.TryGetProperty("handle", out var handle)) return null;
        if (handle.ValueKind == JsonValueKind.Object) return ReadString(handle, "address");
        if (handle.ValueKind == JsonValueKind.String) return handle.GetString();
        return null;
    }

    /// <summary>tapback 在 AMB 以纯文本送达（如 Liked "Thank you."）⇒ 原样渲染，不合成对象。</summary>
    private static bool LooksLikeTapback(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.StartsWith("Liked ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Loved ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Disliked ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Laughed ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Emphasized ", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("Questioned ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>BlueBubbles 时间戳通常为毫秒；小于 1e11 视为秒并换算。</summary>
    private static long NormalizeUnixMs(long value) => value <= 0 ? 0 : (value < 100_000_000_000L ? value * 1000 : value);

    private static long? NormalizeUnixMsOrNull(long? value) => value.HasValue && value.Value > 0 ? NormalizeUnixMs(value.Value) : null;

    private static string? ReadString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? ReadBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static long? ReadLong(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var p)) return p;
        return null;
    }

    private static int? ReadInt(JsonElement obj, string name)
    {
        var v = ReadLong(obj, name);
        return v.HasValue ? (int)v.Value : null;
    }
}
