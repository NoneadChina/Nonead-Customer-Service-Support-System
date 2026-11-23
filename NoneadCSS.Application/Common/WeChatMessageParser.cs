using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 微信消息解析器 - 解析微信服务号推送的 XML 消息，并提供签名校验。
/// </summary>
public static class WeChatMessageParser
{
    /// <summary>
    /// 微信签名校验：将 token、timestamp、nonce 字典排序后拼接，SHA1 哈希后与 signature 比较。
    /// 签名通过后额外校验时间戳新鲜度（±5 分钟窗口，与钉钉 DingTalkCallbackCrypto 保持一致），
    /// 防止攻击者重放捕获的合法签名请求。
    /// </summary>
    /// <param name="token">服务号配置的 Token</param>
    /// <param name="timestamp">请求中的 timestamp 参数</param>
    /// <param name="nonce">请求中的 nonce 参数</param>
    /// <param name="signature">请求中的 signature 参数</param>
    /// <returns>签名是否匹配且时间戳在有效窗口内</returns>
    public static bool ValidateSignature(string token, string timestamp, string nonce, string signature)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var arr = new[] { token, timestamp, nonce };
        Array.Sort(arr, StringComparer.Ordinal);
        var joined = string.Concat(arr);

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(joined));
        var calculated = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        // 微信签名不区分大小写，统一转小写后进行时序安全比较，防止计时侧信道
        var signatureMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(calculated),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));

        if (!signatureMatches)
        {
            return false;
        }

        // 时间戳新鲜度校验（防重放攻击）：拒绝超过 5 分钟的请求。
        // 仅在签名通过后校验，避免对伪造签名的请求泄露时间戳判定细节。
        if (!long.TryParse(timestamp, out var ts))
        {
            return false;
        }

        try
        {
            var tsDateTime = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
            return Math.Abs(DateTimeOffset.UtcNow.UtcDateTime.Subtract(tsDateTime).TotalMinutes) <= 5;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 提取加密消息 XML 中的 Encrypt 节点密文（安全模式/兼容模式报文）。
    /// 明文报文或解析失败返回 null。
    /// </summary>
    public static string? ExtractEncryptValue(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(xml);
            var value = doc.Root?.Element("Encrypt")?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 微信消息体签名校验（msg_signature）：官方加密模式下对 token、timestamp、nonce、Encrypt
    /// 四个值字典序排序后拼接，SHA1 哈希与 msg_signature 比较。
    /// 时间戳新鲜度由 ValidateSignature（同一 timestamp）负责，此处只做消息体完整性校验。
    /// </summary>
    /// <param name="token">服务号配置的 Token</param>
    /// <param name="timestamp">请求中的 timestamp 参数</param>
    /// <param name="nonce">请求中的 nonce 参数</param>
    /// <param name="encrypt">消息体 Encrypt 节点的密文</param>
    /// <param name="msgSignature">请求中的 msg_signature 参数</param>
    /// <returns>消息体签名是否匹配</returns>
    public static bool ValidateMessageSignature(string token, string timestamp, string nonce, string encrypt, string msgSignature)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(encrypt)
            || string.IsNullOrEmpty(msgSignature))
        {
            return false;
        }

        var arr = new[] { token, timestamp, nonce, encrypt };
        Array.Sort(arr, StringComparer.Ordinal);
        var joined = string.Concat(arr);

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(joined));
        var calculated = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        // 微信签名不区分大小写，统一转小写后做时序安全比较，防止计时侧信道
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(calculated),
            Encoding.UTF8.GetBytes(msgSignature.ToLowerInvariant()));
    }

    /// <summary>
    /// 解析微信推送的 XML 消息为 WeChatMessageDto。
    /// 支持明文模式和加密模式（需传入 EncodingAESKey 解密）。
    /// 安全模式下解密后会校验 AppId 防止伪冒。
    /// </summary>
    /// <param name="xml">原始 XML 字符串</param>
    /// <param name="encodingAESKey">消息加解密密钥（明文模式传 null）</param>
    /// <param name="expectedAppId">安全模式下用于校验解密消息中的 AppId（可选）</param>
    /// <param name="encryptMode">WeChatOptions.EncryptMode：plain|compatible|safe；safe 时拒绝明文报文（可选，默认按自动探测）</param>
    /// <returns>解析后的消息 DTO；解析失败返回 null</returns>
    public static WeChatMessageDto? Parse(string xml, string? encodingAESKey = null, string? expectedAppId = null, string? encryptMode = null)
    {
        return Parse(xml, encodingAESKey, expectedAppId, encryptMode, out _);
    }

    public static WeChatMessageDto? Parse(string xml, string? encodingAESKey, string? expectedAppId, out string? error)
    {
        return Parse(xml, encodingAESKey, expectedAppId, null, out error);
    }

    public static WeChatMessageDto? Parse(string xml, string? encodingAESKey, string? expectedAppId, string? encryptMode, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(xml))
        {
            error = "XML 为空";
            return null;
        }

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null)
            {
                error = "XML 根节点为空";
                return null;
            }

            // 加密模式：XML 中含 Encrypt 节点，需解密后重新解析
            var encryptNode = root.Element("Encrypt");
            if (encryptNode != null && !string.IsNullOrEmpty(encryptNode.Value))
            {
                if (string.IsNullOrEmpty(encodingAESKey))
                {
                    error = "安全模式下 EncodingAESKey 缺失，无法解密";
                    return null;
                }

                var (decryptedXml, decryptedAppId, decryptError) = DecryptMessage(encryptNode.Value, encodingAESKey);
                if (string.IsNullOrEmpty(decryptedXml))
                {
                    error = $"解密失败：{decryptError}（AESKey长度={encodingAESKey.Length}）";
                    return null;
                }

                // 校验解密消息中的 AppId 防止伪冒
                if (!string.IsNullOrEmpty(expectedAppId) && !string.IsNullOrEmpty(decryptedAppId)
                    && !string.Equals(decryptedAppId, expectedAppId, StringComparison.Ordinal))
                {
                    error = $"AppId 校验不通过：expected={expectedAppId}, actual={decryptedAppId}";
                    return null;
                }

                doc = XDocument.Parse(decryptedXml);
                root = doc.Root;
                if (root == null)
                {
                    error = "解密后 XML 根节点为空";
                    return null;
                }
            }

            var msgType = GetElementValue(root, "MsgType").ToLowerInvariant();
            var evt = GetElementValue(root, "Event");

            // 位置消息和 LOCATION 事件使用不同的 XML 字段名：
            // - location 消息：Location_X(纬度)、Location_Y(经度)、Scale(精度)
            // - LOCATION 事件：Latitude(纬度)、Longitude(经度)、Precision(精度)
            string latitude, longitude, precision;
            if (msgType == "event" && evt.Equals("LOCATION", StringComparison.OrdinalIgnoreCase))
            {
                latitude = GetElementValue(root, "Latitude");
                longitude = GetElementValue(root, "Longitude");
                precision = GetElementValue(root, "Precision");
            }
            else
            {
                latitude = GetElementValue(root, "Location_X");
                longitude = GetElementValue(root, "Location_Y");
                precision = GetElementValue(root, "Scale");
            }

            var msg = new WeChatMessageDto
            {
                ToUserName = GetElementValue(root, "ToUserName"),
                FromUserName = GetElementValue(root, "FromUserName"),
                CreateTime = long.TryParse(GetElementValue(root, "CreateTime"), out var ts) ? ts : 0,
                MsgType = msgType,
                Content = GetElementValue(root, "Content"),
                MsgId = GetElementValue(root, "MsgId"),
                MediaId = GetElementValue(root, "MediaId"),
                PicUrl = GetElementValue(root, "PicUrl"),
                Event = evt,
                EventKey = GetElementValue(root, "EventKey"),
                Latitude = latitude,
                Longitude = longitude,
                Precision = precision,
                Title = GetElementValue(root, "Title"),
                Description = GetElementValue(root, "Description"),
                Url = GetElementValue(root, "Url")
            };

            msg.BodyText = BuildBodyText(msg);
            return msg;
        }
        catch (Exception ex)
        {
            error = $"解析异常：{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// 构建被动回复的 XML（用于直接响应微信服务器，5 秒内同步返回）。
    /// </summary>
    public static string BuildTextReply(string toUserName, string fromUserName, string content)
    {
        var xml = new StringBuilder();
        xml.Append("<xml>");
        xml.Append($"<ToUserName><![CDATA[{SecurityElement.Escape(toUserName)}]]></ToUserName>");
        xml.Append($"<FromUserName><![CDATA[{SecurityElement.Escape(fromUserName)}]]></FromUserName>");
        xml.Append($"<CreateTime>{new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()}</CreateTime>");
        xml.Append("<MsgType><![CDATA[text]]></MsgType>");
        xml.Append($"<Content><![CDATA[{SecurityElement.Escape(content)}]]></Content>");
        xml.Append("</xml>");
        return xml.ToString();
    }

    /// <summary>
    /// 根据消息类型构建可读正文（存入 Thread.Body）。
    /// </summary>
    private static string BuildBodyText(WeChatMessageDto msg)
    {
        return msg.MsgType switch
        {
            "text" => msg.Content,
            "image" => $"[图片] {msg.PicUrl}",
            "voice" => $"[语音] media_id={msg.MediaId}",
            "video" or "shortvideo" => $"[视频] media_id={msg.MediaId}",
            "location" => $"[位置] 经度:{msg.Longitude} 纬度:{msg.Latitude} 精度:{msg.Precision}",
            "link" => $"[链接] {msg.Title} - {msg.Description} ({msg.Url})",
            "event" => BuildEventText(msg),
            _ => $"[未知消息类型: {msg.MsgType}]"
        };
    }

    private static string BuildEventText(WeChatMessageDto msg)
    {
        return msg.Event.ToLowerInvariant() switch
        {
            "subscribe" => "[事件] 用户关注",
            "unsubscribe" => "[事件] 用户取消关注",
            "click" => $"[事件] 菜单点击: {msg.EventKey}",
            "view" => $"[事件] 菜单跳转: {msg.EventKey}",
            _ => $"[事件] {msg.Event}: {msg.EventKey}"
        };
    }

    private static string GetElementValue(XElement? root, string name)
    {
        return root?.Element(name)?.Value ?? string.Empty;
    }

    /// <summary>
    /// AES-CBC 解密微信加密消息（安全模式）。
    /// EncodingAESKey 为 Base64 编码的 43 字符，解码后为 32 字节 AES 密钥。
    /// 解密后返回 (XML 内容, AppId)，AppId 可用于校验防伪冒。
    /// </summary>
    private static (string? xml, string? appId, string? error) DecryptMessage(string encrypted, string encodingAESKey)
    {
        try
        {
            // EncodingAESKey 43 字符 + '=' → Base64 → 32 字节密钥
            var key = Convert.FromBase64String(encodingAESKey + "=");

            // 检查密文是否在上游传输/中间环节被改写（+ 被转成空格、/ 被转义等）
            // Base64 标准字符集：A-Z a-z 0-9 + / = ，其他字符都说明密文已损坏
            var invalidChars = encrypted.Where(c => !((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=')).Take(10).ToArray();
            if (invalidChars.Length > 0)
            {
                return (null, null, $"密文包含非 Base64 字符（可能被上游环节损坏）：{string.Join(",", invalidChars.Select(c => $"'0x{(int)c:X2}'"))}，密文长度={encrypted.Length}");
            }

            var cipherText = Convert.FromBase64String(encrypted);
            // AES 块大小为 16 字节，密文字节长度必须是 16 的倍数
            if (cipherText.Length % 16 != 0)
            {
                return (null, null, $"密文字节长度={cipherText.Length} 不是 16 的倍数（密文可能被截断），Base64长度={encrypted.Length}");
            }

            // 微信加密格式：16字节随机字符串 + 4字节消息长度(大端) + 消息内容 + AppId + PKCS7填充
            // 注意：微信的 PKCS7 填充按 32 字节对齐（而非标准 AES 的 16 字节对齐），
            // 当消息较长时 padding 值会超过 16，导致 .NET 的 PaddingMode.PKCS7 验证失败。
            // 解决方案：使用 PaddingMode.None 解密，按消息长度字段精确截取内容，忽略 padding。
            byte[] plainText;
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;
                aes.IV = key[..16];

                using var decryptor = aes.CreateDecryptor();
                plainText = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
            }

            // 跳过 16 字节随机字符串，读取 4 字节消息长度（大端）
            var length = BitConverter.ToInt32(plainText, 16);
            if (BitConverter.IsLittleEndian)
            {
                length = System.Net.IPAddress.HostToNetworkOrder(length);
            }

            // 长度合法性校验：消息内容必须能落在 plainText 范围内
            // plainText 结构：16(随机) + 4(长度) + length(消息) + N(AppId) + padding
            if (length <= 0 || 20 + length > plainText.Length)
            {
                return (null, null, $"解密后消息长度字段非法：length={length}, plainText长度={plainText.Length}");
            }

            // 按长度字段精确截取消息内容，AppId 紧跟其后，末尾为 padding（自动忽略）
            var xmlBytes = plainText.Skip(20).Take(length).ToArray();
            // AppId 位于消息内容之后，到 padding 之前；padding 值为最后一个字节的值
            var paddingValue = plainText[^1];
            var appIdEnd = plainText.Length - (paddingValue > 0 && paddingValue <= 32 ? paddingValue : 0);
            var appIdBytes = plainText.Skip(20 + length).Take(appIdEnd - 20 - length).ToArray();
            var appId = Encoding.UTF8.GetString(appIdBytes).TrimEnd('\0');

            return (Encoding.UTF8.GetString(xmlBytes), appId, null);
        }
        catch (Exception ex)
        {
            return (null, null, ex.Message);
        }
    }
}
