using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 飞书事件消息解析器 - 解析飞书事件订阅推送的 JSON 消息，提供签名校验与 AES 解密。
/// 飞书事件回调格式参考：https://open.feishu.cn/document/server-docs/event-dispatcher/event/event-dispatching
/// </summary>
public static class FeishuMessageParser
{
    /// <summary>
    /// 飞书事件签名校验：将 timestamp + nonce + encryptKey + body 拼接后 SHA256 哈希，
    /// 与请求头 X-Lark-Signature 比较。
    /// </summary>
    /// <param name="timestamp">请求头 X-Lark-Request-Timestamp</param>
    /// <param name="nonce">请求头 X-Lark-Request-Nonce</param>
    /// <param name="encryptKey">飞书应用配置的 Encrypt Key</param>
    /// <param name="body">原始请求 body 字符串</param>
    /// <param name="signature">请求头 X-Lark-Signature</param>
    /// <returns>签名是否匹配</returns>
    public static bool ValidateSignature(string timestamp, string nonce, string encryptKey, string body, string signature)
    {
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce)
            || string.IsNullOrEmpty(encryptKey) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var joined = timestamp + nonce + encryptKey + body;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        var calculated = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        // 时序安全比较，防止计时侧信道
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(calculated),
            Encoding.UTF8.GetBytes(signature));
    }

    /// <summary>
    /// AES-256-CBC 解密飞书加密事件载荷。
    /// 密钥 = SHA256(EncryptKey)（32 字节），IV = 密文前 16 字节，PKCS7 填充。
    /// </summary>
    /// <param name="encryptKey">飞书应用配置的 Encrypt Key</param>
    /// <param name="encryptedData">Base64 编码的加密数据</param>
    /// <returns>解密后的 JSON 明文；失败返回 null</returns>
    public static string? DecryptPayload(string encryptKey, string encryptedData)
    {
        if (string.IsNullOrEmpty(encryptKey) || string.IsNullOrEmpty(encryptedData))
        {
            return null;
        }

        try
        {
            var key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptKey));
            var cipherText = Convert.FromBase64String(encryptedData);

            if (cipherText.Length < 16)
            {
                return null;
            }

            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = cipherText[..16];

            using var decryptor = aes.CreateDecryptor();
            var plainText = decryptor.TransformFinalBlock(cipherText, 16, cipherText.Length - 16);

            return Encoding.UTF8.GetString(plainText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"飞书事件解密失败：{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 解析飞书事件回调 JSON 为 FeishuEventDto。
    /// 支持两种格式：
    /// 1. URL 验证挑战：{ "challenge": "xxx", "token": "xxx", "type": "url_verification" }
    /// 2. 事件 v2.0：{ "schema": "2.0", "header": {...}, "event": {...} }
    /// </summary>
    /// <param name="rawJson">原始 JSON 字符串</param>
    /// <returns>解析后的事件 DTO；解析失败返回 null</returns>
    public static FeishuEventDto? ParseEvent(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // 挑战验证请求
            if (root.TryGetProperty("type", out var typeProp)
                && typeProp.ValueKind == JsonValueKind.String
                && typeProp.GetString() == "url_verification")
            {
                var dto = new FeishuEventDto
                {
                    IsChallenge = true,
                    Challenge = root.TryGetProperty("challenge", out var ch) && ch.ValueKind == JsonValueKind.String
                        ? ch.GetString() : null,
                    Token = root.TryGetProperty("token", out var tk) && tk.ValueKind == JsonValueKind.String
                        ? tk.GetString() : null
                };
                return dto;
            }

            // 事件 v2.0 格式
            var result = new FeishuEventDto();

            if (root.TryGetProperty("header", out var header) && header.ValueKind == JsonValueKind.Object)
            {
                result.EventId = GetString(header, "event_id");
                result.EventType = GetString(header, "event_type");
                result.CreateTime = GetString(header, "create_time");
                result.Token = GetString(header, "token");
                result.AppId = GetString(header, "app_id");
                result.TenantKey = GetString(header, "tenant_key");
            }

            if (root.TryGetProperty("event", out var evt) && evt.ValueKind == JsonValueKind.Object)
            {
                // 发送者信息
                if (evt.TryGetProperty("sender", out var sender) && sender.ValueKind == JsonValueKind.Object)
                {
                    result.SenderType = GetString(sender, "sender_type");
                    if (sender.TryGetProperty("sender_id", out var senderId) && senderId.ValueKind == JsonValueKind.Object)
                    {
                        result.SenderOpenId = GetString(senderId, "open_id");
                        result.SenderUserId = GetString(senderId, "user_id");
                        result.SenderUnionId = GetString(senderId, "union_id");
                    }
                }

                // 消息信息
                if (evt.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                {
                    result.MessageId = GetString(message, "message_id");
                    result.ChatType = GetString(message, "chat_type");
                    result.MessageType = GetString(message, "message_type");
                    result.MessageCreateTime = GetString(message, "create_time");

                    // 飞书消息 content 字段为 JSON 字符串（二次序列化），保留原始字符串
                    if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        result.Content = content.GetString();
                    }
                    else if (message.TryGetProperty("content", out var contentObj) && contentObj.ValueKind == JsonValueKind.Object)
                    {
                        result.Content = contentObj.GetRawText();
                    }
                }
            }

            result.BodyText = BuildBodyText(result);
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"飞书事件解析失败：{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 根据消息类型构建可读正文（存入 Thread.Body 与预览）。
    /// 飞书消息 content 为 JSON 字符串，按消息类型解析后提取文本。
    /// </summary>
    public static string BuildBodyText(FeishuEventDto dto)
    {
        if (string.IsNullOrEmpty(dto.MessageType))
        {
            return string.Empty;
        }

        var content = dto.Content ?? string.Empty;

        return dto.MessageType.ToLowerInvariant() switch
        {
            "text" => ExtractText(content),
            "image" => "[图片]",
            "file" => ExtractFileName(content),
            "audio" => "[语音]",
            "media" => "[视频]",
            "post" => ExtractPostText(content),
            _ => "[未知消息类型]"
        };
    }

    /// <summary>
    /// 从文本消息 content JSON 中提取 text 字段。
    /// 格式：{ "text": "xxx" }
    /// </summary>
    private static string ExtractText(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // 解析失败降级返回原始内容
            return content;
        }

        return content;
    }

    /// <summary>
    /// 从文件消息 content JSON 中提取 file_name 字段。
    /// 格式：{ "file_key": "xxx", "file_name": "xxx.pdf" }
    /// </summary>
    private static string ExtractFileName(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return "[文件]";
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("file_name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                var fileName = name.GetString();
                return string.IsNullOrEmpty(fileName) ? "[文件]" : $"[文件] {fileName}";
            }
        }
        catch
        {
            // 忽略解析失败
        }

        return "[文件]";
    }

    /// <summary>
    /// 从富文本(post)消息 content JSON 中提取所有文本内容。
    /// 飞书 post 格式：
    /// { "zh_cn": { "title": "xxx", "content": [ [ {"tag":"text","text":"xxx"}, {"tag":"a","text":"xxx","href":"..."} ] ] } }
    /// 支持 zh_cn / en_us / ja_jp 等多语言节点，取首个存在的语言。
    /// </summary>
    private static string ExtractPostText(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return "[富文本]";
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // 遍历语言节点，取首个存在的
            foreach (var langProp in root.EnumerateObject())
            {
                if (langProp.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var sb = new StringBuilder();
                var langObj = langProp.Value;

                if (langObj.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                {
                    var titleStr = title.GetString();
                    if (!string.IsNullOrEmpty(titleStr))
                    {
                        sb.Append(titleStr).Append('\n');
                    }
                }

                if (langObj.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var line in contentArr.EnumerateArray())
                    {
                        if (line.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var node in line.EnumerateArray())
                        {
                            if (node.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            // text / a / mention 等标签均可能含 text 字段
                            if (node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                            {
                                sb.Append(text.GetString());
                            }
                        }

                        sb.Append('\n');
                    }
                }

                return sb.ToString().Trim();
            }
        }
        catch
        {
            // 忽略解析失败
        }

        return "[富文本]";
    }

    private static string? GetString(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
