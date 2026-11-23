using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Common;

/// <summary>
/// LINE Webhook 载荷解析与签名校验（纯函数，不做任何 IO）。
/// 规范来源 docs/integration/line.md：
///   X-Line-Signature = Base64(HMAC-SHA256(raw_body_bytes, ChannelSecret))，必须对原始字节校验并常量时间比较；
///   载荷形如 {"destination":"U…","events":[{…}]}。
/// </summary>
public static class LineWebhookParser
{
    /// <summary>LINE 事件签名请求头名</summary>
    public const string SignatureHeader = "X-Line-Signature";

    /// <summary>
    /// 校验 Webhook 签名。对**原始请求体字节**做 HMAC-SHA256 后 Base64，与请求头常量时间比较。
    /// 任何缺失/不匹配都返回 false（调用方应回 401/400，且不进入业务处理）。
    /// </summary>
    public static bool VerifySignature(byte[]? rawBody, string? channelSecret, string? signatureHeader)
    {
        if (rawBody is null || rawBody.Length == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(channelSecret) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        // 头里可能有换行/空白（反向代理拼接），统一去掉后比较
        var provided = signatureHeader.Trim();
        if (provided.Length == 0)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
        var computed = Convert.ToBase64String(hmac.ComputeHash(rawBody));

        // 常量时间比较：Base64 长度可变，FixedTimeEquals 长度不同会立即返回 false（不泄露正文，仅泄露长度）
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(provided));
    }

    /// <summary>计算签名（供设置页自检与单测使用；返回值不应写日志）</summary>
    public static string ComputeSignature(byte[] rawBody, string channelSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
        return Convert.ToBase64String(hmac.ComputeHash(rawBody));
    }

    /// <summary>快速判断是否 LINE Webhook 载荷（含 destination + events 数组）</summary>
    public static bool IsLineWebhook(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("events", out var events)
                && events.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>读取 destination（本 Channel 的 Basic ID / userId），用于与配置核对；解析失败返回 null</summary>
    public static string? ParseDestination(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("destination", out var destination)
                && destination.ValueKind == JsonValueKind.String
                ? destination.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 解析全部事件。非消息事件同样返回（上层按 EventType 过滤），畸形事件被跳过而不影响其余事件。
    /// </summary>
    public static List<LineWebhookEvent> ParseEvents(string? payload)
    {
        var events = new List<LineWebhookEvent>();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return events;
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return events;
        }

        if (!root.TryGetProperty("events", out var eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
        {
            return events;
        }

        foreach (var item in eventsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var parsed = ParseSingleEvent(item);
            if (parsed is not null)
            {
                events.Add(parsed);
            }
        }

        return events;
    }

    /// <summary>仅解析需要落库的客户消息事件</summary>
    public static List<LineWebhookEvent> ParseMessageEvents(string? payload)
        => ParseEvents(payload).Where(e => e.IsCustomerMessage).ToList();

    private static LineWebhookEvent? ParseSingleEvent(JsonElement ev)
    {
        var eventType = ReadString(ev, "type") ?? "unknown";
        var mode = ReadString(ev, "mode") ?? "active";
        var timestamp = ReadInt64(ev, "timestamp");

        var result = new LineWebhookEvent
        {
            EventType = eventType,
            Mode = mode,
            Timestamp = timestamp,
            ReplyToken = ReadString(ev, "replyToken"),
            MarkAsReadToken = ReadString(ev, "markAsReadToken"),
        };

        // source：user / group / room
        if (ev.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
        {
            result.SourceType = ReadString(source, "type") ?? "user";
            result.UserId = ReadString(source, "userId");
            result.ChatId = ReadString(source, "groupId") ?? ReadString(source, "roomId");

            // 群/房间事件里也可能带 userId（发送者）
            if (string.IsNullOrEmpty(result.UserId))
            {
                result.UserId = ReadString(source, "userId");
            }
        }

        switch (eventType)
        {
            case "message":
                if (!ev.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                result.MessageType = ReadString(message, "id") is null ? "unknown" : (ReadString(message, "type") ?? "unknown");
                result.MessageId = ReadString(message, "id");
                result.ContentMessageId = result.MessageId;
                result.QuoteMessageId = ReadString(message, "quoteMessageId");

                switch (result.MessageType)
                {
                    case "text":
                        result.TextBody = ReadString(message, "text");
                        break;
                    case "image":
                    case "video":
                    case "audio":
                        // 内容需从 api-data.line.me 用 message.id 换取，载荷里没有 URL
                        break;
                    case "file":
                        result.FileName = ReadString(message, "fileName");
                        result.FileSize = ReadInt64(message, "fileSize");
                        break;
                    case "location":
                        result.Title = ReadString(message, "title");
                        result.LocationAddress = ReadString(message, "address");
                        result.Latitude = ReadDouble(message, "latitude");
                        result.Longitude = ReadDouble(message, "longitude");
                        break;
                    case "sticker":
                        result.TextBody = $"[sticker {ReadString(message, "packageId")}/{ReadString(message, "stickerId")}]";
                        break;
                }

                // 幂等键优先用 message.id；缺 id 的畸形消息用事件级合成键，避免重复入库
                result.DedupeKey = !string.IsNullOrWhiteSpace(result.MessageId)
                    ? result.MessageId!
                    : BuildFallbackKey(result, "msg");
                return result;

            case "unsend":
                if (ev.TryGetProperty("unsend", out var unsend)
                    && unsend.ValueKind == JsonValueKind.Object
                    && unsend.TryGetProperty("messageIds", out var messageIds)
                    && messageIds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var id in messageIds.EnumerateArray())
                    {
                        if (id.ValueKind == JsonValueKind.String)
                        {
                            var value = id.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                result.UnsentMessageIds.Add(value!);
                            }
                        }
                    }
                }

                result.DedupeKey = result.UnsentMessageIds.Count > 0
                    ? $"unsend_{string.Join(",", result.UnsentMessageIds)}"
                    : BuildFallbackKey(result, "unsend");
                return result;

            case "postback":
                if (ev.TryGetProperty("postback", out var postback) && postback.ValueKind == JsonValueKind.Object)
                {
                    result.PostbackData = ReadString(postback, "data");
                    result.QuoteMessageId = ReadString(postback, "inputMessage");
                }

                result.MessageType = "postback";
                result.TextBody = result.PostbackData;
                result.DedupeKey = BuildFallbackKey(result, "postback");
                return result;

            case "follow":
            case "unfollow":
            case "join":
            case "leave":
            case "memberJoined":
            case "memberLeft":
            case "beacon":
            case "accountLink":
            case "videoPlayComplete":
                result.DedupeKey = BuildFallbackKey(result, eventType);
                return result;

            default:
                // 未知事件类型仍返回，便于日志与未来扩展（上层默认不落库）
                result.DedupeKey = BuildFallbackKey(result, eventType);
                return result;
        }
    }

    /// <summary>
    /// 非消息事件的合成幂等键：type + source + timestamp。
    /// LINE 对同一事件重试时三者完全一致，可据此去重；缺 timestamp 时退化为 type+source。
    /// </summary>
    private static string BuildFallbackKey(LineWebhookEvent ev, string prefix)
    {
        var source = ev.UserId ?? ev.ChatId ?? "unknown";
        return ev.Timestamp > 0
            ? $"{prefix}_{source}_{ev.Timestamp}"
            : $"{prefix}_{source}";
    }

    private static string? ReadString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long ReadInt64(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
            ? result
            : 0L;

    private static long? ReadInt64Nullable(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var result)
            ? result
            : null;

    private static double? ReadDouble(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var result)
            ? result
            : null;
}
