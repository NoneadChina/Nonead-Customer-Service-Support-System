using System.Security.Cryptography;
using System.Text;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 钉钉事件订阅 HTTP 回调加解密工具。
/// 协议参考：https://github.com/open-dingtalk/DingTalk-Callback-Crypto
/// 签名算法：SHA1(sort([token, timestamp, nonce, encrypt]).join(""))
/// 加密算法：AES-CBC-256，PKCS7 填充（32 字节块），IV = Key[0..15]
/// 密文结构：16字节随机串 + 4字节消息长度(大端) + 消息明文 + ownerKey(AppKey) + PKCS7填充
/// </summary>
public static class DingTalkCallbackCrypto
{
    private const int AesKeyLength = 43;
    private const int BlockSize = 32; // 钉钉 PKCS7 填充按 32 字节对齐（非标准 AES 的 16）

    /// <summary>
    /// 校验签名并解密钉钉推送的加密消息。
    /// </summary>
    /// <param name="token">钉钉开放平台配置的签名 Token</param>
    /// <param name="encodingAesKey">43 位 AES 密钥（Base64 编码去掉末尾 '='）</param>
    /// <param name="ownerKey">企业自建应用传 AppKey（即 ClientId）</param>
    /// <param name="msgSignature">请求 query 中的 msg_signature（或 signature）</param>
    /// <param name="timestamp">请求 query 中的 timestamp（或 timeStamp）</param>
    /// <param name="nonce">请求 query 中的 nonce</param>
    /// <param name="encrypt">请求 body 中的 encrypt 字段值</param>
    /// <returns>(成功?, 明文消息, 错误信息)</returns>
    public static (bool success, string? plaintext, string? error) Decrypt(
        string token, string encodingAesKey, string ownerKey,
        string msgSignature, string timestamp, string nonce, string encrypt)
    {
        if (string.IsNullOrEmpty(encodingAesKey) || encodingAesKey.Length != AesKeyLength)
            return (false, null, $"EncodingAesKey 长度必须为 {AesKeyLength}，实际={encodingAesKey?.Length ?? 0}");

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(ownerKey))
            return (false, null, "Token 和 OwnerKey(AppKey) 不能为空");

        if (string.IsNullOrEmpty(encrypt))
            return (false, null, "encrypt 字段为空");

        // 1. 校验签名：SHA1(sort([token, timestamp, nonce, encrypt]))
        var computedSig = ComputeSignature(token, timestamp, nonce, encrypt);
        // M-1 修复：钉钉签名均为 ASCII 字符（hex 串），但使用 UTF8 编码做字节比较，
        // 与 WeChatMessageParser 保持一致，避免编码不一致带来的隐患。
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSig),
                Encoding.UTF8.GetBytes(msgSignature ?? string.Empty)))
        {
            return (false, null, $"签名校验失败：expected={computedSig}, actual={msgSignature}");
        }

        // 1.1 时间戳新鲜度校验（防重放攻击）：拒绝超过 5 分钟的请求
        // 参考 WeChatMessageParser.ValidateSignature 的 5 分钟窗口实现。
        // 签名通过后再校验时间戳，避免对伪造签名的请求泄露时间戳判定细节。
        if (!long.TryParse(timestamp, out var ts))
        {
            return (false, null, "时间戳格式非法，无法解析为 Unix 时间戳");
        }
        try
        {
            var tsDateTime = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
            if (Math.Abs(DateTimeOffset.UtcNow.UtcDateTime.Subtract(tsDateTime).TotalMinutes) > 5)
            {
                return (false, null, "时间戳超出有效窗口（5分钟），疑似重放攻击");
            }
        }
        catch (Exception)
        {
            return (false, null, "时间戳解析失败，疑似重放攻击");
        }

        // 2. AES-CBC 解密
        byte[] plainBytes;
        try
        {
            var key = Convert.FromBase64String(encodingAesKey + "=");
            var cipherBytes = Convert.FromBase64String(encrypt);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None; // 手动处理 PKCS7（32 字节块），不用框架默认的 16 字节块
            aes.IV = key[..16];

            using var decryptor = aes.CreateDecryptor();
            plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        }
        catch (Exception ex)
        {
            return (false, null, $"AES 解密异常：{ex.Message}");
        }

        // 3. 移除 PKCS7 填充（32 字节块对齐）
        var pad = plainBytes[^1];
        if (pad < 1 || pad > BlockSize) pad = 0;
        var unpadded = new byte[plainBytes.Length - pad];
        Array.Copy(plainBytes, unpadded, unpadded.Length);

        // 4. 解析：16随机 + 4长度(大端) + 消息明文 + ownerKey
        if (unpadded.Length < 20)
            return (false, null, $"解密后数据过短：{unpadded.Length} 字节");

        var msgLength = BitConverter.ToInt32(unpadded, 16);
        if (BitConverter.IsLittleEndian)
            msgLength = System.Net.IPAddress.HostToNetworkOrder(msgLength);

        if (msgLength <= 0 || 20 + msgLength > unpadded.Length)
            return (false, null, $"消息长度字段非法：length={msgLength}, total={unpadded.Length}");

        var msgBytes = new byte[msgLength];
        Array.Copy(unpadded, 20, msgBytes, 0, msgLength);
        var plaintext = Encoding.UTF8.GetString(msgBytes);

        // 5. 校验 ownerKey（AppKey）
        var ownerKeyBytes = new byte[unpadded.Length - 20 - msgLength];
        Array.Copy(unpadded, 20 + msgLength, ownerKeyBytes, 0, ownerKeyBytes.Length);
        var fromOwnerKey = Encoding.UTF8.GetString(ownerKeyBytes).TrimEnd('\0');

        if (!string.Equals(fromOwnerKey, ownerKey, StringComparison.Ordinal))
            return (false, null, $"OwnerKey 校验不通过：expected={ownerKey}, actual={fromOwnerKey}");

        return (true, plaintext, null);
    }

    /// <summary>
    /// 加密明文并返回钉钉要求的加密 Map（用于响应 check_url 等验证请求）。
    /// </summary>
    /// <returns>包含 msg_signature、encrypt、timeStamp、nonce 的字典</returns>
    public static Dictionary<string, string> Encrypt(
        string token, string encodingAesKey, string ownerKey, string plaintext)
    {
        if (string.IsNullOrEmpty(encodingAesKey) || encodingAesKey.Length != AesKeyLength)
            throw new ArgumentException($"EncodingAesKey 长度必须为 {AesKeyLength}");

        var key = Convert.FromBase64String(encodingAesKey + "=");
        var randomBytes = RandomNumberGenerator.GetBytes(16);
        var msgBytes = Encoding.UTF8.GetBytes(plaintext);
        var ownerKeyBytes = Encoding.UTF8.GetBytes(ownerKey);
        var lengthBytes = BitConverter.GetBytes(
            BitConverter.IsLittleEndian
                ? System.Net.IPAddress.HostToNetworkOrder(msgBytes.Length)
                : msgBytes.Length);

        // 拼接：random(16) + length(4) + msg + ownerKey + PKCS7 padding
        var beforePad = new byte[16 + 4 + msgBytes.Length + ownerKeyBytes.Length];
        Array.Copy(randomBytes, 0, beforePad, 0, 16);
        Array.Copy(lengthBytes, 0, beforePad, 16, 4);
        Array.Copy(msgBytes, 0, beforePad, 20, msgBytes.Length);
        Array.Copy(ownerKeyBytes, 0, beforePad, 20 + msgBytes.Length, ownerKeyBytes.Length);

        var padBytes = GetPkcs7Padding(beforePad.Length);
        var unencrypted = new byte[beforePad.Length + padBytes.Length];
        Array.Copy(beforePad, unencrypted, beforePad.Length);
        Array.Copy(padBytes, 0, unencrypted, beforePad.Length, padBytes.Length);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.IV = key[..16];

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(unencrypted, 0, unencrypted.Length);
        var encryptBase64 = Convert.ToBase64String(encrypted);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N")[..16];
        var signature = ComputeSignature(token, timestamp, nonce, encryptBase64);

        return new Dictionary<string, string>
        {
            ["msg_signature"] = signature,
            ["encrypt"] = encryptBase64,
            ["timeStamp"] = timestamp,
            ["nonce"] = nonce
        };
    }

    /// <summary>
    /// 计算签名：SHA1(sort([token, timestamp, nonce, encrypt]).join(""))
    /// </summary>
    private static string ComputeSignature(string token, string timestamp, string nonce, string encrypt)
    {
        var arr = new[] { token, timestamp, nonce, encrypt };
        Array.Sort(arr, StringComparer.Ordinal);
        var joined = string.Concat(arr);
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(joined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 钉钉 PKCS7 填充（32 字节块对齐）。
    /// </summary>
    private static byte[] GetPkcs7Padding(int dataLength)
    {
        var amountToPad = BlockSize - (dataLength % BlockSize);
        if (amountToPad == 0) amountToPad = BlockSize;
        var padding = new byte[amountToPad];
        Array.Fill(padding, (byte)amountToPad);
        return padding;
    }
}
