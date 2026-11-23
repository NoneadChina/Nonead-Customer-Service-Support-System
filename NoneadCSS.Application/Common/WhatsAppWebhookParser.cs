using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Common;

/// <summary>
/// WhatsApp Cloud API Webhook 解析器 - HMAC-SHA256 签名验证 + JSON payload 解析。
/// 所有方法为静态纯函数，无副作用，便于单元测试。
/// </summary>
public static class WhatsAppWebhookParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 验证 Webhook 签名 (X-Hub-Signature-256)。
    /// Meta 使用 App Secret 作 HMAC-SHA256 密钥对 rawBody 计算签名。
    /// </summary>
    /// <param name="rawBody">请求原始 body 字节流</param>
    /// <param name="appSecret">App Secret（密钥）</param>
    /// <param name="signatureHeader">X-Hub-Signature-256 头值，格式 "sha256={hex_sig}"</param>
    /// <returns>签名是否匹配</returns>
    public static bool VerifySignature(byte[] rawBody, string appSecret, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        // 头格式: "sha256={hex_signature}"
        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedHex = signatureHeader[prefix.Length..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computedHash = hmac.ComputeHash(rawBody);
        var computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(expectedHex.ToLowerInvariant()));
    }

    /// <summary>
    /// 验证 Webhook GET 验证请求 (hub.mode / hub.verify_token / hub.challenge)。
    /// </summary>
    /// <param name="hubMode">hub.mode 参数值（应为 "subscribe"）</param>
    /// <param name="hubVerifyToken">hub.verify_token 参数值</param>
    /// <param name="configuredVerifyToken">系统中配置的 Verify Token</param>
    /// <returns>验证通过返回 hub.challenge 值，失败返回 null</returns>
    public static string? VerifyWebhookSubscription(string? hubMode, string? hubVerifyToken, string? hubChallenge, string? configuredVerifyToken)
    {
        if (!string.Equals(hubMode, "subscribe", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(configuredVerifyToken))
            return null;

        if (!string.Equals(hubVerifyToken, configuredVerifyToken, StringComparison.Ordinal))
            return null;

        return hubChallenge;
    }

    /// <summary>
    /// 从 Webhook payload 解析入站消息列表。
    /// 只提取 value.messages[] 中的消息，忽略 statuses/errors。
    /// </summary>
    public static List<WhatsAppWebhookMessage> ParseMessages(string payload)
    {
        var result = new List<WhatsAppWebhookMessage>();
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        using var doc = JsonDocument.Parse(payload);

        if (!doc.RootElement.TryGetProperty("entry", out var entries))
            return result;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes))
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("field", out var field) &&
                    field.ValueEquals("messages") &&
                    change.TryGetProperty("value", out var value))
                {
                    var displayPhoneNumber = value.TryGetProperty("metadata", out var metadata) &&
                                             metadata.TryGetProperty("display_phone_number", out var dpn)
                        ? dpn.GetString()
                        : null;

                    // 提取联系人信息
                    string? profileName = null;
                    if (value.TryGetProperty("contacts", out var contacts) &&
                        contacts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var contact in contacts.EnumerateArray())
                        {
                            if (contact.TryGetProperty("profile", out var profile) &&
                                profile.TryGetProperty("name", out var name))
                            {
                                profileName = name.GetString();
                            }
                        }
                    }

                    // 提取消息
                    if (value.TryGetProperty("messages", out var messages) &&
                        messages.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var msg in messages.EnumerateArray())
                        {
                            var parsed = ParseSingleMessage(msg, profileName, displayPhoneNumber);
                            if (parsed != null)
                                result.Add(parsed);
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 从 Webhook payload 解析状态回执列表。
    /// </summary>
    public static List<WhatsAppStatusUpdate> ParseStatuses(string payload)
    {
        var result = new List<WhatsAppStatusUpdate>();
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        using var doc = JsonDocument.Parse(payload);

        if (!doc.RootElement.TryGetProperty("entry", out var entries))
            return result;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes))
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("field", out var field) &&
                    field.ValueEquals("messages") &&
                    change.TryGetProperty("value", out var value) &&
                    value.TryGetProperty("statuses", out var statuses) &&
                    statuses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var status in statuses.EnumerateArray())
                    {
                        var parsed = ParseSingleStatus(status);
                        if (parsed != null)
                            result.Add(parsed);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 判断 payload 是否为 WhatsApp Webhook（object == "whatsapp_business_account"）。
    /// </summary>
    public static bool IsWhatsAppWebhook(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("object", out var obj) &&
                   obj.ValueEquals("whatsapp_business_account");
        }
        catch
        {
            return false;
        }
    }

    private static WhatsAppWebhookMessage? ParseSingleMessage(JsonElement msg, string? profileName, string? displayPhoneNumber)
    {
        var message = new WhatsAppWebhookMessage
        {
            ProfileName = profileName,
            DisplayPhoneNumber = displayPhoneNumber
        };

        if (msg.TryGetProperty("id", out var id))
            message.MessageId = id.GetString() ?? string.Empty;

        if (msg.TryGetProperty("from", out var from))
            message.From = from.GetString() ?? string.Empty;

        if (msg.TryGetProperty("timestamp", out var ts))
        {
            if (ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var tsLong))
                message.Timestamp = tsLong;
            else if (ts.ValueKind == JsonValueKind.String &&
                     long.TryParse(ts.GetString(), out var tsFromString))
                message.Timestamp = tsFromString;
        }

        if (!msg.TryGetProperty("type", out var type))
        {
            message.MessageType = "unknown";
            return message;
        }

        message.MessageType = type.GetString() ?? "unknown";

        switch (message.MessageType)
        {
            case "text":
                if (msg.TryGetProperty("text", out var text) && text.TryGetProperty("body", out var body))
                    message.TextBody = body.GetString();
                break;

            case "image":
            case "video":
            case "audio":
            case "document":
            case "sticker":
                if (msg.TryGetProperty(message.MessageType, out var media))
                {
                    if (media.TryGetProperty("id", out var mediaId))
                        message.MediaId = mediaId.GetString();
                    if (media.TryGetProperty("mime_type", out var mime))
                        message.MimeType = mime.GetString();
                    if (media.TryGetProperty("caption", out var cap))
                        message.Caption = cap.GetString();
                    if (media.TryGetProperty("filename", out var fn))
                        message.Filename = fn.GetString();
                }
                break;

            case "interactive":
                if (msg.TryGetProperty("interactive", out var interactive))
                {
                    if (interactive.TryGetProperty("type", out var intType))
                    {
                        var intTypeStr = intType.GetString();
                        if (intTypeStr == "button_reply" &&
                            interactive.TryGetProperty("button_reply", out var buttonReply) &&
                            buttonReply.TryGetProperty("title", out var btnTitle))
                        {
                            message.InteractiveTitle = btnTitle.GetString();
                        }
                        else if (intTypeStr == "list_reply" &&
                                 interactive.TryGetProperty("list_reply", out var listReply) &&
                                 listReply.TryGetProperty("title", out var listTitle))
                        {
                            message.InteractiveTitle = listTitle.GetString();
                        }
                    }
                }
                break;

            case "button":
                if (msg.TryGetProperty("button", out var button))
                {
                    if (button.TryGetProperty("text", out var btnText))
                        message.ButtonText = btnText.GetString();
                    if (button.TryGetProperty("payload", out var payloadEl))
                        message.InteractiveTitle = payloadEl.GetString();
                }
                break;

            case "location":
                if (msg.TryGetProperty("location", out var loc))
                {
                    if (loc.TryGetProperty("latitude", out var lat) && lat.TryGetDouble(out var latVal))
                        message.Latitude = latVal;
                    if (loc.TryGetProperty("longitude", out var lng) && lng.TryGetDouble(out var lngVal))
                        message.Longitude = lngVal;
                    if (loc.TryGetProperty("name", out var locName))
                        message.LocationName = locName.GetString();
                    if (loc.TryGetProperty("address", out var locAddr))
                        message.LocationAddress = locAddr.GetString();
                }
                break;

            case "system":
                // 系统消息（客户换号等），仅记日志不创建线程
                break;

            default:
                message.MessageType = "unknown";
                break;
        }

        return message;
    }

    private static WhatsAppStatusUpdate? ParseSingleStatus(JsonElement status)
    {
        var update = new WhatsAppStatusUpdate();

        if (status.TryGetProperty("id", out var id))
            update.MessageId = id.GetString() ?? string.Empty;

        if (status.TryGetProperty("recipient_id", out var recipientId))
            update.RecipientWaId = recipientId.GetString() ?? string.Empty;

        if (status.TryGetProperty("status", out var statusVal))
            update.Status = statusVal.GetString() ?? string.Empty;

        if (status.TryGetProperty("timestamp", out var ts))
        {
            if (ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var tsLong))
                update.Timestamp = tsLong;
            else if (ts.ValueKind == JsonValueKind.String &&
                     long.TryParse(ts.GetString(), out var tsFromString))
                update.Timestamp = tsFromString;
        }

        if (status.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var err in errors.EnumerateArray())
            {
                if (err.TryGetProperty("message", out var errMsg))
                {
                    update.ErrorMessage = errMsg.GetString();
                    break;
                }
            }
        }

        if (status.TryGetProperty("conversation", out var conv) &&
            conv.TryGetProperty("id", out var convId))
        {
            update.ConversationId = convId.GetString();
        }

        return update;
    }
}
