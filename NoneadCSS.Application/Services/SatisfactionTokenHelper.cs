using System.Security.Cryptography;
using System.Text;

namespace NoneadCSS.Application.Services;

public static class SatisfactionTokenHelper
{
    private const int TokenValidityDays = 3;

    /// <summary>
    /// 生成满意度评价令牌（以客户邮箱作为身份标识）。
    /// 保留原签名以兼容邮件渠道现有调用。
    /// </summary>
    public static string GenerateToken(int conversationId, string customerEmail, string secretKey)
    {
        return GenerateTokenWithIdentifier(conversationId, customerEmail, secretKey);
    }

    /// <summary>
    /// 生成满意度评价令牌（以客户渠道 ID 作为身份标识）。
    /// 用于微信/飞书/WhatsApp/钉钉等聊天渠道客户无邮箱时的满意度调查。
    /// </summary>
    /// <param name="channelId">客户在渠道中的唯一标识（openId / waId / userId 等）</param>
    public static string GenerateTokenByChannelId(int conversationId, string channelId, string secretKey)
    {
        // 使用 "channel:" 前缀与邮箱标识区分，避免不同标识类型之间的碰撞
        return GenerateTokenWithIdentifier(conversationId, $"channel:{channelId}", secretKey);
    }

    /// <summary>
    /// 内部统一生成逻辑：使用任意 identifier 拼接签名原文。
    /// </summary>
    private static string GenerateTokenWithIdentifier(int conversationId, string identifier, string secretKey)
    {
        var raw = $"{conversationId}|{identifier}|{DateTime.UtcNow:yyyyMMdd}|{secretKey}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        var token = Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-").TrimEnd('=');
        return $"{conversationId}-{token}";
    }

    /// <summary>
    /// 验证满意度评价令牌（以客户邮箱作为身份标识）。
    /// 保留原签名以兼容邮件渠道现有调用。
    /// </summary>
    public static bool ValidateToken(string token, int conversationId, string customerEmail, string secretKey)
    {
        return ValidateTokenWithIdentifier(token, conversationId, customerEmail, secretKey);
    }

    /// <summary>
    /// 验证满意度评价令牌（以客户渠道 ID 作为身份标识）。
    /// </summary>
    public static bool ValidateTokenByChannelId(string token, int conversationId, string channelId, string secretKey)
    {
        return ValidateTokenWithIdentifier(token, conversationId, $"channel:{channelId}", secretKey);
    }

    /// <summary>
    /// 内部统一验证逻辑：使用任意 identifier 进行哈希比对。
    /// </summary>
    private static bool ValidateTokenWithIdentifier(string token, int conversationId, string identifier, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('-', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        var tokenHash = parts[1].Replace("_", "/").Replace("-", "+");
        var padding = 4 - tokenHash.Length % 4;
        if (padding < 4)
        {
            tokenHash += new string('=', padding);
        }

        byte[] providedHash;
        try
        {
            providedHash = Convert.FromBase64String(tokenHash);
        }
        catch
        {
            return false;
        }

        var today = DateTime.UtcNow;
        for (int i = 0; i <= TokenValidityDays; i++)
        {
            var date = today.AddDays(-i).ToString("yyyyMMdd");
            var raw = $"{conversationId}|{identifier}|{date}|{secretKey}";
            var bytes = Encoding.UTF8.GetBytes(raw);
            var expectedHash = SHA256.HashData(bytes);
            if (CryptographicOperations.FixedTimeEquals(providedHash, expectedHash))
            {
                return true;
            }
        }

        return false;
    }
}