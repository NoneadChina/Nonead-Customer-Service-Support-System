using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Common;

/// <summary>
/// Telegram Update 解析器（纯函数，不做任何 IO，便于单元测试）。
/// 要点（docs/integration/telegram.md）：
///   - Telegram 没有请求体签名，只有 setWebhook 时登记的 secret_token 会回带到 X-Telegram-Bot-Api-Secret-Token 头，需常量时间比对。
///   - Update.update_id 递增但约一周静默后会重置 ⇒ 只能当轮询游标，去重必须用 chat.id + message_id。
///   - Update 至多有一个内容字段（message / edited_message / channel_post / callback_query / my_chat_member …）。
///   - photo 是按尺寸排列的数组 ⇒ 取最大的一档；caption 上限 1024，text 上限 4096。
/// </summary>
public static class TelegramWebhookParser
{
    /// <summary>Webhook 密钥请求头名称（Telegram 官方固定）</summary>
    public const string SecretHeaderName = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>
    /// 校验 Webhook 密钥头（常量时间比较）。未配置密钥时一律拒绝（fail-closed）：
    /// 任何人都可伪造 chat.id/message_id 投递假消息，因此不允许匿名放行。
    /// </summary>
    public static bool VerifySecretToken(string? configuredSecret, string? headerToken)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret) || string.IsNullOrWhiteSpace(headerToken))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(configuredSecret);
        var received = Encoding.UTF8.GetBytes(headerToken);
        if (expected.Length != received.Length)
        {
            // 长度不等时仍做一次等长比较，避免通过响应时间探测密钥长度
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    /// <summary>判断原始请求体是否为 Telegram Update（含 update_id 字段）</summary>
    public static bool IsTelegramUpdate(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("update_id", out var updateId)
                   && (updateId.ValueKind == JsonValueKind.Number || updateId.ValueKind == JsonValueKind.String);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>解析单条 Webhook Update；非法 JSON 抛 JsonException 由调用方捕获</summary>
    public static TelegramWebhookMessage? ParseUpdate(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return ParseUpdateElement(document.RootElement);
    }

    /// <summary>
    /// 解析 Update 集合：支持 Webhook 单对象、Update 数组、以及 getUpdates 的 {"ok":true,"result":[...]} 信封。
    /// </summary>
    public static List<TelegramWebhookMessage> ParseUpdates(string payload)
    {
        var results = new List<TelegramWebhookMessage>();
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                AddIfParsed(results, item);
            }

            return results;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("result", out var resultArray) && resultArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultArray.EnumerateArray())
                {
                    AddIfParsed(results, item);
                }

                return results;
            }

            AddIfParsed(results, root);
        }

        return results;
    }

    /// <summary>读取 update_id（仅用于排序与轮询游标）</summary>
    public static long? PeekUpdateId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? ReadInt64(document.RootElement, "update_id")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>去重键：chat.id + message_id（同一 chat 内 message_id 唯一，且不会像 update_id 那样重置）</summary>
    public static string BuildDedupeKey(long chatId, long messageId) => $"{chatId}:{messageId}";

    /// <summary>
    /// 拆分超长文本（Telegram sendMessage 上限 4096 字符）。
    /// 边界只做「回退」不做「吞并」：分隔符（换行 / 空格）归属前一片，因此拼接所有分片可逐字符还原输入。
    /// 不切开代理对（emoji），找不到边界时在 maxChars 处硬切；与 MessengerService.SplitText / LineClient.SplitText 同一口径。
    /// maxChars &lt; 2 时代理对仍整对成片（该片可比 maxChars 多 1 个 char），以保证既不抛越界也不产出空分片。
    /// </summary>
    public static IReadOnlyList<string> SplitLongMessage(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        if (maxChars <= 0)
        {
            maxChars = 4096;
        }

        if (text.Length <= maxChars)
        {
            return [text];
        }

        var parts = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            if (text.Length - start <= maxChars)
            {
                parts.Add(text[start..]);
                break;
            }

            var end = start + maxChars;
            if (char.IsHighSurrogate(text[end - 1]))
            {
                end--;
            }

            // 审计 #14：maxChars=1 且本片首字符是高位代理项时，上面的回退会让 end == start，
            // 于是 LastIndexOf('\n', end - 1, windowLength) 的 startIndex 变成 -1 ⇒ ArgumentOutOfRangeException，
            // 同时 start 永不前进 ⇒ 死循环 + 空分片。代理对（2 个 char）是最小不可分单元：
            // 宁可这一片比 maxChars 多出 1 个 char，也不拆开代理对、绝不产出空分片。
            if (end <= start)
            {
                end = Math.Min(start + 2, text.Length);
            }

            // 只在末尾 1/4 窗口内回退到换行，其次空格；找不到就硬切，分隔符留在前一片
            // （窗口长度与 startIndex 都取下限，保证 maxChars 极小时也不会越界）
            var windowLength = Math.Min(end - start, Math.Max(1, maxChars / 4));
            var newline = text.LastIndexOf('\n', Math.Max(0, end - 1), Math.Max(1, windowLength));
            if (newline > start)
            {
                end = newline + 1;
            }
            else
            {
                var space = text.LastIndexOf(' ', Math.Max(0, end - 1), Math.Max(1, windowLength));
                if (space > start)
                {
                    end = space + 1;
                }
            }

            // 兜底：任何回退路径都不允许 end 退到 start 之前，否则空分片会让循环无法收敛
            if (end <= start)
            {
                end = start + 1;
            }

            parts.Add(text[start..end]);
            start = end;
        }

        return parts;
    }
    /// <summary>解析 /start 深链参数（https://t.me/&lt;bot&gt;?start=PARAM ⇒ 首条消息为 "/start PARAM"）</summary>
    public static bool TryParseStartParameter(string? text, out string? parameter)
    {
        parameter = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = trimmed.Length > 6 ? trimmed[6..].TrimStart() : string.Empty;
        if (rest.Length == 0)
        {
            return true;
        }

        // /start 参数最长 64 个字符（官方限制），带 startgroup 前缀时形如 "startgroup=PARAM"
        if (rest.StartsWith("startgroup=", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[11..];
        }

        parameter = rest.Length > 64 ? rest[..64] : rest;
        return true;
    }

    /// <summary>
    /// 解析 Bot API 响应体：{"ok":true,"result":{...}} 或 {"ok":false,"error_code":..,"description":"..","parameters":{..}}。
    /// 分类规则（docs/integration/telegram.md 第 7 节）：
    ///   - 429/420 ⇒ 限流，读 parameters.retry_after；
    ///   - 403 ⇒ 用户拉黑 / bot 被移出群 ⇒ Blocked（终态，不重试）；
    ///   - 400 chat not found / bot can't initiate conversation ⇒ Unreachable（不重试）；
    ///   - 401 ⇒ InvalidToken；409 ⇒ Conflict（webhook 与轮询冲突）；
    ///   - parameters.migrate_to_chat_id ⇒ 需要改写 chat_id。
    /// </summary>
    public static TelegramSendResult ParseSendResponse(string? responseBody, string? fallbackMessageIdField = "message_id")
    {
        var result = new TelegramSendResult();
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            result.ErrorMessage = "响应为空";
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result.ErrorMessage = "响应格式异常";
                return result;
            }

            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            if (ok)
            {
                result.Success = true;
                if (root.TryGetProperty("result", out var payload) && payload.ValueKind == JsonValueKind.Object
                    && !string.IsNullOrEmpty(fallbackMessageIdField)
                    && payload.TryGetProperty(fallbackMessageIdField, out var messageIdElement))
                {
                    result.MessageId = ReadRawString(messageIdElement);
                }

                return result;
            }

            result.ErrorCode = (int)(ReadInt64(root, "error_code") ?? 0);
            result.Description = ReadString(root, "description");
            if (root.TryGetProperty("parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Object)
            {
                result.RetryAfterSeconds = (int?)ReadInt64(parameters, "retry_after");
                result.MigrateToChatId = ReadInt64(parameters, "migrate_to_chat_id");
            }

            var description = result.Description ?? string.Empty;
            switch (result.ErrorCode)
            {
                case 429:
                case 420:
                    // 限流：保留 retry_after，由调用方（消息队列）延后重试
                    break;
                case 403:
                    result.Blocked = true;
                    break;
                case 401:
                    result.InvalidToken = true;
                    break;
                case 409:
                    result.Conflict = true;
                    break;
                case 400:
                    result.Unreachable = description.Contains("chat not found", StringComparison.OrdinalIgnoreCase)
                                       || description.Contains("can't initiate conversation", StringComparison.OrdinalIgnoreCase)
                                       || description.Contains("bot was kicked", StringComparison.OrdinalIgnoreCase);
                    break;
            }

            result.ErrorMessage = string.IsNullOrEmpty(description) ? $"Telegram 返回 {result.ErrorCode}" : description;
            return result;
        }
        catch (JsonException)
        {
            result.ErrorMessage = "响应不是合法 JSON";
            return result;
        }
    }

    private static void AddIfParsed(List<TelegramWebhookMessage> results, JsonElement update)
    {
        var message = ParseUpdateElement(update);
        if (message != null)
        {
            results.Add(message);
        }
    }

    private static TelegramWebhookMessage? ParseUpdateElement(JsonElement update)
    {
        if (update.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var model = new TelegramWebhookMessage
        {
            UpdateId = ReadInt64(update, "update_id") ?? 0
        };

        // callback_query：需要 answerCallbackQuery（id 仅 10 秒有效），不产生会话线程
        if (update.TryGetProperty("callback_query", out var callbackQuery) && callbackQuery.ValueKind == JsonValueKind.Object)
        {
            model.UpdateKind = "callback_query";
            model.CallbackQueryId = ReadString(callbackQuery, "id");
            model.CallbackData = ReadString(callbackQuery, "data");
            if (callbackQuery.TryGetProperty("message", out var originMessage) && originMessage.ValueKind == JsonValueKind.Object)
            {
                FillChat(model, originMessage);
                model.MessageId = ReadInt64(originMessage, "message_id") ?? 0;
            }
            else if (callbackQuery.TryGetProperty("from", out var callbackFrom) && callbackFrom.ValueKind == JsonValueKind.Object)
            {
                FillFrom(model, callbackFrom);
            }

            if (model.ChatId != 0 && model.MessageId != 0)
            {
                model.DedupeKey = BuildDedupeKey(model.ChatId, model.MessageId);
            }

            return model;
        }

        string? kind = null;
        JsonElement message = default;
        foreach (var candidate in new[] { "message", "edited_message", "channel_post", "edited_channel_post", "business_message" })
        {
            if (update.TryGetProperty(candidate, out var element) && element.ValueKind == JsonValueKind.Object)
            {
                kind = candidate;
                message = element;
                break;
            }
        }

        if (kind == null)
        {
            // my_chat_member / chat_member / inline_query / poll_answer 等：识别但不入库
            foreach (var property in update.EnumerateObject())
            {
                if (property.NameEquals("update_id"))
                {
                    continue;
                }

                model.UpdateKind = property.Name;
                break;
            }

            if (string.IsNullOrEmpty(model.UpdateKind))
            {
                return null;
            }

            return model;
        }

        model.UpdateKind = kind;
        model.MessageId = ReadInt64(message, "message_id") ?? 0;
        FillChat(model, message);
        model.EditDate = ReadInt64(message, "edit_date");
        model.Timestamp = ReadInt64(message, "date") ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        model.MessageThreadId = (int?)ReadInt64(message, "message_thread_id");
        model.ReplyToMessageId = message.TryGetProperty("reply_to_message", out var replyTo) && replyTo.ValueKind == JsonValueKind.Object
            ? ReadInt64(replyTo, "message_id")
            : null;

        if (message.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object)
        {
            FillFrom(model, from);
        }
        else if (model.ChatId != 0)
        {
            // 频道 posts 无 from：发送者即频道本身
            model.FromId = model.ChatId;
            model.DisplayName = model.ChatTitle;
        }

        // 私聊：chat.id == from.id；群/频道：chat.id 为负 ⇒ 会话键取 chat.id
        model.IsGroupMessage = model.ChatId < 0;
        if (model.FromId == 0)
        {
            model.FromId = model.ChatId;
        }

        FillContent(model, message);

        TryParseStartParameter(model.TextBody, out var startParameter);
        if (!string.IsNullOrEmpty(startParameter))
        {
            model.StartParameter = startParameter;
        }

        if (model.ChatId != 0 && model.MessageId != 0)
        {
            model.DedupeKey = BuildDedupeKey(model.ChatId, model.MessageId);
        }

        return model;
    }

    private static void FillChat(TelegramWebhookMessage model, JsonElement message)
    {
        if (!message.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        model.ChatId = ReadInt64(chat, "id") ?? 0;
        model.ChatType = ReadString(chat, "type") ?? string.Empty;
        model.ChatTitle = ReadString(chat, "title");
    }

    private static void FillFrom(TelegramWebhookMessage model, JsonElement from)
    {
        model.FromId = ReadInt64(from, "id") ?? 0;
        model.IsBotSender = from.TryGetProperty("is_bot", out var isBot) && isBot.ValueKind == JsonValueKind.True;
        model.FromUsername = ReadString(from, "username");
        model.LanguageCode = ReadString(from, "language_code");

        var first = ReadString(from, "first_name");
        var last = ReadString(from, "last_name");
        var fullName = string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        model.DisplayName = fullName.Length > 0 ? fullName : model.FromUsername;
    }

    private static void FillContent(TelegramWebhookMessage model, JsonElement message)
    {
        var text = ReadString(message, "text");
        if (!string.IsNullOrEmpty(text))
        {
            model.MessageType = "text";
            model.TextBody = text;
        }

        if (message.TryGetProperty("caption", out var captionElement))
        {
            model.Caption = ReadRawString(captionElement);
        }

        // 媒体类：photo 为尺寸阶梯数组，取最大一档；其余为单对象
        if (message.TryGetProperty("photo", out var photo) && photo.ValueKind == JsonValueKind.Array && photo.GetArrayLength() > 0)
        {
            JsonElement largest = default;
            long largestSize = -1;
            foreach (var item in photo.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var size = ReadInt64(item, "file_size") ?? 0;
                if (size >= largestSize)
                {
                    largestSize = size;
                    largest = item;
                }
            }

            if (largest.ValueKind == JsonValueKind.Object)
            {
                model.MessageType = "photo";
                model.FileId = ReadString(largest, "file_id");
                model.FileUniqueId = ReadString(largest, "file_unique_id");
                model.FileSize = largestSize > 0 ? largestSize : null;
                model.MimeType = "image/jpeg";
                model.Filename = "photo.jpg";
                model.Caption ??= ReadString(largest, "caption");
            }
        }

        foreach (var kind in new[] { "document", "audio", "voice", "video", "video_note", "animation", "sticker" })
        {
            if (!message.TryGetProperty(kind, out var media) || media.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            model.MessageType = kind;
            model.FileId = ReadString(media, "file_id");
            model.FileUniqueId = ReadString(media, "file_unique_id");
            model.FileSize = ReadInt64(media, "file_size");
            model.MimeType = ReadString(media, "mime_type");
            model.Filename = ReadString(media, "file_name");
            model.Caption ??= ReadString(media, "caption");

            if (string.IsNullOrEmpty(model.Filename))
            {
                (model.Filename, model.MimeType) = InferMediaIdentity(kind, model.MimeType);
            }

            break;
        }

        if (message.TryGetProperty("contact", out var contact) && contact.ValueKind == JsonValueKind.Object)
        {
            model.MessageType = "contact";
            model.TextBody = string.Join(" / ", new[] { ReadString(contact, "phone_number"), ReadString(contact, "first_name") }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        if (message.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.Object)
        {
            model.MessageType = "location";
            model.Latitude = ReadDouble(location, "latitude");
            model.Longitude = ReadDouble(location, "longitude");
            model.TextBody = ReadString(location, "address")
                             ?? (model.Latitude.HasValue && model.Longitude.HasValue
                                 ? $"{model.Latitude.Value:0.000000}, {model.Longitude.Value:0.000000}"
                                 : null);
        }

        if (message.TryGetProperty("poll", out var poll) && poll.ValueKind == JsonValueKind.Object)
        {
            model.MessageType = "poll";
            model.TextBody = ReadString(poll, "question");
        }

        if (message.TryGetProperty("migrate_to_chat_id", out var migrate) && migrate.ValueKind == JsonValueKind.Number)
        {
            model.MessageType = "migrate_chat";
            model.MigrateToChatId = ReadInt64(message, "migrate_to_chat_id");
        }
        else if (message.TryGetProperty("left_chat_member", out _)
                 || message.TryGetProperty("new_chat_members", out _)
                 || message.TryGetProperty("new_chat_title", out _)
                 || message.TryGetProperty("new_chat_photo", out _))
        {
            // 群成员/群资料变更等服务消息：不建线程，只保留类型
            model.MessageType = "service";
        }
    }

    /// <summary>媒体类型缺 mime_type/file_name 时的兜底推断</summary>
    private static (string? fileName, string? mimeType) InferMediaIdentity(string kind, string? mimeType)
    {
        return kind switch
        {
            "voice" => ("voice.ogg", mimeType ?? "audio/ogg"),
            "audio" => ("audio.mp3", mimeType ?? "audio/mpeg"),
            "video" => ("video.mp4", mimeType ?? "video/mp4"),
            "video_note" => ("video_note.mp4", mimeType ?? "video/mp4"),
            "animation" => ("animation.mp4", mimeType ?? "video/mp4"),
            "sticker" => ("sticker.webp", mimeType ?? "image/webp"),
            "document" => ("document.bin", mimeType ?? "application/octet-stream"),
            _ => (null, mimeType)
        };
    }

    private static long? ReadInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? ReadDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value))
        {
            return value;
        }

        if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ReadString(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var element) ? ReadRawString(element) : null;
    }

    private static string? ReadRawString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
