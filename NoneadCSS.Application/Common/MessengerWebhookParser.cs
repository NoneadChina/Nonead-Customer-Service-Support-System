using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Common;

/// <summary>
/// Facebook Messenger Platform Webhook 解析器 - X-Hub-Signature-256 签名验证 + GET 订阅校验 + 载荷解析。
/// 所有方法为静态纯函数，无 IO、无副作用，便于单元测试。
/// 载荷形态：{"object":"page","entry":[{"id":PAGE,"time":ms,"messaging":[{sender,recipient,timestamp,message|postback|optin|read|delivery}]}]}
/// 解析约定（docs/integration/messenger.md）：
///   - message.is_echo 必须跳过（否则坐席回复会被当成客户消息，形成自问自答死循环）；
///   - sender.id == Page id 的事件同样跳过（Page 自身发的消息，等价 echo）；
///   - 幂等键取 message.mid（跨重试与 echo 稳定）；postback / optin 无 mid，用合成键。
/// </summary>
public static class MessengerWebhookParser
{
    /// <summary>GET 订阅校验的 hub.mode 期望值</summary>
    public const string SubscribeMode = "subscribe";

    /// <summary>签名头算法前缀（Graph 头格式：sha256=&lt;hex&gt;）</summary>
    private const string SignaturePrefix = "sha256=";

    /// <summary>
    /// 验证 Webhook 签名 (X-Hub-Signature-256 = HMAC-SHA256(AppSecret, 原始 body 字节))。
    /// 必须在 JSON 绑定之前传入原始字节；恒定时间比较。旧版 X-Hub-Signature(SHA-1) 不支持。
    /// 注意：签名不含时间戳 ⇒ 无防重放能力，去重靠 message.mid。
    /// </summary>
    /// <param name="rawBody">请求原始 body 字节流</param>
    /// <param name="appSecret">App Secret（HMAC 密钥）</param>
    /// <param name="signatureHeader">X-Hub-Signature-256 头值，格式 "sha256={hex_sig}"</param>
    /// <returns>签名是否匹配（AppSecret/头缺失或格式不符一律 false）</returns>
    public static bool VerifySignature(byte[] rawBody, string appSecret, string? signatureHeader)
    {
        if (rawBody == null)
            return false;
        if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        if (!signatureHeader.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var providedHex = signatureHeader[SignaturePrefix.Length..].Trim().ToLowerInvariant();
        if (providedHex.Length == 0)
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computedHex = Convert.ToHexString(hmac.ComputeHash(rawBody)).ToLowerInvariant();

        // 恒定时间比较：先要求长度一致（长度不同不可能相等，且不泄露内容），再逐字节比较
        var providedBytes = Encoding.UTF8.GetBytes(providedHex);
        var computedBytes = Encoding.UTF8.GetBytes(computedHex);
        if (providedBytes.Length != computedBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(computedBytes, providedBytes);
    }

    /// <summary>
    /// 验证 Webhook GET 订阅校验（hub.mode / hub.verify_token / hub.challenge）。
    /// mode==subscribe 且 verify_token 与配置值完全相等才回显 challenge，否则返回 null（控制器回 403）。
    /// </summary>
    /// <returns>验证通过返回 hub.challenge 原文（须以 text/plain 回 200），失败返回 null</returns>
    public static string? VerifyWebhookSubscription(string? hubMode, string? hubVerifyToken, string? hubChallenge, string? configuredVerifyToken)
    {
        if (!string.Equals(hubMode, SubscribeMode, StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(configuredVerifyToken))
            return null;

        if (string.IsNullOrWhiteSpace(hubVerifyToken))
            return null;

        // 配置值与传入值都是不透明短 token，等值比较即可（无需恒定时间：非密钥派生）
        if (!string.Equals(hubVerifyToken, configuredVerifyToken, StringComparison.Ordinal))
            return null;

        return hubChallenge;
    }

    /// <summary>
    /// 判断 payload 是否为 Messenger Page Webhook（object == "page"）。
    /// </summary>
    public static bool IsMessengerWebhook(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("object", out var obj) &&
                   obj.ValueEquals("page");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 从 Webhook payload 解析入站消息事件（逐 entry、逐 messaging）。
    /// 已过滤：is_echo、sender.id == Page id、以及无文本/无附件的空调用（如纯 sender_action echo）。
    /// 解析失败（非法 JSON）返回空列表并由调用方记日志；单条 messaging 结构异常只跳过该条。
    /// </summary>
    public static List<MessengerWebhookMessage> ParseMessages(string? payload)
    {
        var result = new List<MessengerWebhookMessage>();
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        using var doc = JsonDocument.Parse(payload);
        var pageId = ExtractPageId(doc.RootElement);

        foreach (var messaging in EnumerateMessagingElements(doc.RootElement))
        {
            var parsed = ParseSingleMessaging(messaging, pageId);
            if (parsed != null)
                result.Add(parsed);
        }

        return result;
    }

    /// <summary>
    /// 统计 payload 中被忽略的 echo / Page 自发消息数量（用于诊断与测试断言，不产生线程）。
    /// </summary>
    public static int CountIgnoredEchoes(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return 0;

        using var doc = JsonDocument.Parse(payload);
        var pageId = ExtractPageId(doc.RootElement);
        var count = 0;

        foreach (var messaging in EnumerateMessagingElements(doc.RootElement))
        {
            if (IsEchoOrSelfMessage(messaging, pageId))
                count++;
        }

        return count;
    }

    /// <summary>
    /// 从 Webhook payload 解析投递/已读回执。Graph 的 message_deliveries / message_reads 事件在两种位置都会出现：
    ///   - entry[].delivery / entry[].read（订阅 message_deliveries、message_reads 时的官方形态，事件时间取 entry[].time）；
    ///   - messaging[].delivery / messaging[].read（事件时间取 messaging[].timestamp）。
    /// 回执对象本身的形态是 {"mids":["<mid>",…],"watermark":<epochMs>}：mids 是逐条消息的 mid，
    /// watermark 表示「该时刻之前的出站消息全部已送达/已读」。只有 watermark 而没有 mids 时，
    /// 记一条 MessageId 为空的水位标记（调用方按水位处理，不做逐条线程查找）。
    /// 旧版注释声称逐条明细在 ops[].mid —— 与平台实际载荷不符；ops[] 仅作为可选的替代形状继续兼容读取。
    /// </summary>
    public static List<MessengerReceiptUpdate> ParseReceipts(string? payload)
    {
        var result = new List<MessengerReceiptUpdate>();
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var entry in entries.EnumerateArray())
        {
            // entry 级回执（message_deliveries / message_reads 的官方落位）
            var entryTimestampMs = ReadInt64(entry, "time");
            CollectReceipt(entry, "delivery", "delivered", entryTimestampMs, result);
            CollectReceipt(entry, "read", "read", entryTimestampMs, result);

            if (!entry.TryGetProperty("messaging", out var messaging) || messaging.ValueKind != JsonValueKind.Array)
                continue;

            // messaging 级回执（同一事件的另一落位；两种落位不会在同一事件里同时出现）
            foreach (var item in messaging.EnumerateArray())
            {
                var timestampMs = ReadInt64(item, "timestamp");
                CollectReceipt(item, "delivery", "delivered", timestampMs, result);
                CollectReceipt(item, "read", "read", timestampMs, result);
            }
        }

        return result;
    }

    /// <summary>
    /// 提取 Page id：优先 messaging[].recipient.id，其次 entry.id。
    /// 用于 echo 守卫与 CRM 映射键 (channel=messenger, external_id=PSID, page_id)。
    /// </summary>
    public static string? ExtractPageId(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return ExtractPageId(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- 内部实现

    private static IEnumerable<JsonElement> EnumerateMessagingElements(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("messaging", out var messaging) || messaging.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in messaging.EnumerateArray())
                yield return item;
        }
    }

    private static string? ExtractPageId(JsonElement root)
    {
        if (root.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.TryGetProperty("id", out var id))
                {
                    var value = ReadString(id);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
        }

        return null;
    }

    private static bool IsEchoOrSelfMessage(JsonElement messaging, string? pageId)
    {
        // message.is_echo
        if (messaging.TryGetProperty("message", out var message) &&
            message.TryGetProperty("is_echo", out var isEcho) &&
            (isEcho.ValueKind == JsonValueKind.True))
        {
            return true;
        }

        // sender.id == Page id（Page 自身发出的消息，语义等价 echo）
        var senderId = ReadNestedString(messaging, "sender", "id");
        if (!string.IsNullOrWhiteSpace(pageId) && string.Equals(senderId, pageId, StringComparison.Ordinal))
            return true;

        return false;
    }

    private static MessengerWebhookMessage? ParseSingleMessaging(JsonElement messaging, string? pageId)
    {
        if (IsEchoOrSelfMessage(messaging, pageId))
            return null;

        var from = ReadNestedString(messaging, "sender", "id");
        if (string.IsNullOrWhiteSpace(from))
            return null;

        var recipientId = ReadNestedString(messaging, "recipient", "id");
        var timestampMs = ReadInt64(messaging, "timestamp");

        // 回执事件（read / delivery）不产生消息线程
        if (messaging.TryGetProperty("read", out _) || messaging.TryGetProperty("delivery", out _))
            return null;

        var message = new MessengerWebhookMessage
        {
            From = from,
            PageId = !string.IsNullOrWhiteSpace(recipientId) ? recipientId : pageId,
            TimestampMs = timestampMs,
            Timestamp = timestampMs > 0 ? timestampMs / 1000 : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Seq = messaging.TryGetProperty("message", out var m0) ? ReadInt64OrNull(m0, "seq") : null
        };

        if (messaging.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.Object)
        {
            message.IsEcho = messageEl.TryGetProperty("is_echo", out var echoEl) && echoEl.ValueKind == JsonValueKind.True;

            var mid = ReadString2(messageEl, "mid");
            if (!string.IsNullOrWhiteSpace(mid))
                message.MessageId = mid;

            if (messageEl.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            {
                message.TextBody = textEl.GetString();
                message.MessageType = "text";
            }

            // 用户点按 quick reply 按钮时 Graph 同时给出 message.text（按钮标题）与 message.quick_reply.payload，
            // 所以 text 分支不能把归类定死为 text：会话线程应按「按钮点按」归类（与 LineWebhookParser 的 postback、
            // WhatsAppWebhookParser 的 interactive.button_reply 一致），text 只作为展示正文保留。
            if (messageEl.TryGetProperty("quick_reply", out var quickReply) && quickReply.ValueKind == JsonValueKind.Object)
            {
                message.QuickReplyPayload = ReadString2(quickReply, "payload");
                message.MessageType = "quick_reply";
            }

            if (messageEl.TryGetProperty("attachments", out var attachments) &&
                attachments.ValueKind == JsonValueKind.Array &&
                attachments.GetArrayLength() > 0)
            {
                // 一条消息最多一个附件（Graph 行为）；取第一个可处理的附件
                var first = attachments[0];
                ApplyAttachment(message, first);
            }

            if (string.IsNullOrEmpty(message.MessageType))
                message.MessageType = "unknown";

            // 无 mid 时（罕见：异常载荷）合成一个稳定键，避免同一重试重复入库
            if (string.IsNullOrWhiteSpace(message.MessageId))
                message.MessageId = BuildSyntheticId(message.From, message.PageId, timestampMs, "message");

            // 完全空的 message 事件（无文本、无附件，例如仅 sender_action 的 echo 残留）不建线程
            if (message.MessageType is "unknown" && string.IsNullOrEmpty(message.TextBody))
                return null;

            return message;
        }

        if (messaging.TryGetProperty("postback", out var postback))
        {
            message.MessageType = "postback";
            message.PostbackPayload = ReadString2(postback, "payload");
            message.PostbackTitle = ReadString2(postback, "title");
            if (postback.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
                message.QuickReplyPayload = data.GetString();

            if (string.IsNullOrWhiteSpace(message.PostbackPayload) && string.IsNullOrWhiteSpace(message.PostbackTitle))
                return null;

            message.MessageId = BuildSyntheticId(message.From, message.PageId, timestampMs, "postback");
            return message;
        }

        if (messaging.TryGetProperty("optin", out var optin))
        {
            message.MessageType = "optin";
            message.OptinRef = ReadString2(optin, "ref");
            if (string.IsNullOrWhiteSpace(message.OptinRef))
                return null;

            message.MessageId = BuildSyntheticId(message.From, message.PageId, timestampMs, "optin");
            return message;
        }

        // 其余事件（reaction / handshake / unidentified 等）本期不处理
        return null;
    }

    private static void ApplyAttachment(MessengerWebhookMessage message, JsonElement attachment)
    {
        var type = ReadString2(attachment, "type") ?? "file";
        message.AttachmentType = type;

        if (!attachment.TryGetProperty("payload", out var payload))
            return;

        message.MediaUrl = ReadString2(payload, "url");
        message.AttachmentId = ReadString2(payload, "attachment_id");
        message.Title = ReadString2(payload, "title");

        switch (type)
        {
            case "location":
                if (payload.TryGetProperty("coordinates", out var coords))
                {
                    if (coords.TryGetProperty("lat", out var lat) && lat.TryGetDouble(out var latVal))
                        message.Latitude = latVal;
                    if (coords.TryGetProperty("long", out var lng) && lng.TryGetDouble(out var lngVal))
                        message.Longitude = lngVal;
                }

                message.MessageType = "location";
                return;

            case "fallback":
                // 不支持的附件类型：Graph 用 fallback 携带 title/url
                message.MessageType = "fallback";
                if (string.IsNullOrWhiteSpace(message.TextBody))
                    message.TextBody = ReadString2(payload, "url");
                return;

            default:
                // image / audio / video / file
                message.MessageType = type;
                return;
        }
    }

    /// <summary>读取容器（entry 或 messaging）上名为 property 的回执对象（delivery / read）；不存在或非对象则什么都不做。</summary>
    private static void CollectReceipt(JsonElement container, string property, string status, long timestampMs, List<MessengerReceiptUpdate> result)
    {
        if (!container.TryGetProperty(property, out var receipt) || receipt.ValueKind != JsonValueKind.Object)
            return;

        var watermark = ReadInt64OrNull(receipt, "watermark");
        CollectMidUpdates(receipt, status, timestampMs, watermark, result);
    }

    /// <summary>
    /// 从回执对象里取逐条 mid：官方形状是 mids（字符串数组，docs/integration/messenger.md 只把 read/delivery 列为事件字段，
    /// 逐条明细即 mids）；ops[]（对象数组、元素含 mid）作为旧实现兼容的替代形状一并收编。
    /// 一个 mid 都没有时，若带 watermark 则记一条 MessageId 为空的水位标记。
    /// </summary>
    private static void CollectMidUpdates(JsonElement receipt, string status, long timestampMs, long? watermark, List<MessengerReceiptUpdate> result)
    {
        var counted = 0;

        if (receipt.TryGetProperty("mids", out var mids) && mids.ValueKind == JsonValueKind.Array)
        {
            foreach (var midElement in mids.EnumerateArray())
            {
                var mid = ReadString(midElement);
                if (string.IsNullOrWhiteSpace(mid))
                    continue;

                result.Add(new MessengerReceiptUpdate
                {
                    MessageId = mid,
                    Status = status,
                    Timestamp = timestampMs > 0 ? timestampMs / 1000 : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    WatermarkMs = watermark
                });
                counted++;
            }
        }

        if (receipt.TryGetProperty("ops", out var ops) && ops.ValueKind == JsonValueKind.Array)
        {
            foreach (var op in ops.EnumerateArray())
            {
                if (op.ValueKind != JsonValueKind.Object)
                    continue;

                var mid = ReadString2(op, "mid");
                if (string.IsNullOrWhiteSpace(mid))
                    continue;

                result.Add(new MessengerReceiptUpdate
                {
                    MessageId = mid,
                    Status = status,
                    Timestamp = timestampMs > 0 ? timestampMs / 1000 : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    WatermarkMs = watermark
                });
                counted++;
            }
        }

        if (counted > 0 || !watermark.HasValue)
            return;

        // 只有 watermark、没有逐条 mid：仍回报一次（MessageId 留空），调用方按水位处理
        result.Add(new MessengerReceiptUpdate
        {
            MessageId = string.Empty,
            Status = status,
            Timestamp = watermark.Value / 1000,
            WatermarkMs = watermark
        });
    }

    /// <summary>postback / optin 无 mid，用 (PSID, Page, 时间戳, 事件类型) 合成稳定幂等键。</summary>
    private static string BuildSyntheticId(string psid, string? pageId, long timestampMs, string kind)
        => $"msg_{kind}_{pageId ?? "page"}_{psid}_{timestampMs}";

    private static string? ReadString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        _ => null
    };

    private static string? ReadString2(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var el) ? ReadString(el) : null;

    private static string? ReadNestedString(JsonElement parent, string objectProperty, string valueProperty)
        => parent.TryGetProperty(objectProperty, out var obj) && obj.ValueKind == JsonValueKind.Object
            ? ReadString2(obj, valueProperty)
            : null;

    private static long ReadInt64(JsonElement parent, string property)
        => ReadInt64OrNull(parent, property) ?? 0;

    private static long? ReadInt64OrNull(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var el))
            return null;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var num))
            return num;

        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var parsed))
            return parsed;

        return null;
    }
}
