using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoneadCSS.Application.Common;

/// <summary>
/// Apple Messages for Business 的 JWT 签发与校验（HS256，不是 ES256）。
/// 规格见 docs/integration/imessage.md §1/§3：
///   header {"alg":"HS256","typ":"JWT"}；claims 只有 iss / aud（= MSP ID 或 business/CSP ID）与 iat；
///   没有 kid、没有 teamId、没有 exp；iat 超过 1 小时 Apple 直接拒 ⇒ 至少每小时重签，且绝不跨小时缓存。
/// 不存在「Apple 下发的 EC 密钥对」——只有 MSP/business 与 Apple 之间的 HS256 共享密钥。
/// </summary>
public static class IMessageJwtSigner
{
    /// <summary>Apple 接受的最长 iat 龄期（超过 1 小时直接拒）。</summary>
    public static readonly TimeSpan MaxAcceptedAge = TimeSpan.FromHours(1);

    public const int MaxTtlSeconds = 3600;

    /// <summary>
    /// 签发 AMB 访问令牌。iss 与 aud 同值（Apple 口径：均为 MSP ID 或 business/CSP ID）。
    /// </summary>
    /// <param name="issuerAndAudience">MSP ID 或 business/CSP ID</param>
    /// <param name="sharedSecret">HS256 共享密钥（不得写日志）</param>
    /// <param name="ttlSeconds">保留参数：Apple 不使用 exp，此值仅用于调用方的重签节奏；硬上限 3600</param>
    /// <param name="issuedAtUtc">签发时刻（测试可注入）</param>
    public static string MintToken(
        string issuerAndAudience,
        string sharedSecret,
        int ttlSeconds = MaxTtlSeconds,
        DateTimeOffset? issuedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(issuerAndAudience))
            throw new ArgumentException("iss/aud 不能为空", nameof(issuerAndAudience));
        if (string.IsNullOrWhiteSpace(sharedSecret))
            throw new ArgumentException("HS256 共享密钥不能为空", nameof(sharedSecret));
        if (ttlSeconds <= 0 || ttlSeconds > MaxTtlSeconds)
            ttlSeconds = MaxTtlSeconds;

        var issued = issuedAtUtc ?? DateTimeOffset.UtcNow;
        var iat = issued.ToUnixTimeSeconds();

        // claims 严格按 Apple 口径：只有 iss / aud / iat，无 exp / kid / teamId
        var claims = new Dictionary<string, object>(3)
        {
            ["iss"] = issuerAndAudience,
            ["aud"] = issuerAndAudience,
            ["iat"] = iat,
        };

        var header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var payload = JsonSerializer.Serialize(claims);

        var signingInput = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(header))}.{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}";
        var signature = Sign(signingInput, sharedSecret);
        return $"{signingInput}.{signature}";
    }

    /// <summary>
    /// 校验入站请求的 Authorization 头（Apple/MSP 用同一 HS256 密钥回签）。
    /// 失败时给出可记日志的原因（不含密钥、不含令牌内容）。
    /// </summary>
    public static bool TryValidate(
        string? authorizationHeader,
        string sharedSecret,
        string expectedIssuerOrAudience,
        out string? error,
        DateTimeOffset? nowUtc = null,
        TimeSpan? clockSkew = null)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            error = "未配置 AMB 共享密钥，无法校验入站令牌";
            return false;
        }

        var token = authorizationHeader;
        if (!string.IsNullOrWhiteSpace(token) && token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token["Bearer ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            error = "缺少 Authorization 头（Apple 对入站缺此头会返回 401）";
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            error = "JWT 结构非法";
            return false;
        }

        var expectedSig = Sign($"{parts[0]}.{parts[1]}", sharedSecret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSig),
                Encoding.UTF8.GetBytes(parts[2])))
        {
            error = "JWT 签名不匹配（可能密钥轮换或未核准的调用方）";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            error = "JWT 载荷解析失败";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("iat", out var iatEl) || iatEl.ValueKind != JsonValueKind.Number)
            {
                error = "JWT 缺少 iat";
                return false;
            }

            var iat = iatEl.GetInt64();
            var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            var skew = (clockSkew ?? TimeSpan.FromMinutes(2)).TotalSeconds;
            if (iat - now > skew || now - iat > MaxAcceptedAge.TotalSeconds + skew)
            {
                error = "JWT 已过期（iat 超过 1 小时会被 Apple 拒收，须按小时重签）";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedIssuerOrAudience))
            {
                var iss = root.TryGetProperty("iss", out var issEl) && issEl.ValueKind == JsonValueKind.String
                    ? issEl.GetString() : null;
                var aud = root.TryGetProperty("aud", out var audEl) && audEl.ValueKind == JsonValueKind.String
                    ? audEl.GetString() : null;
                if (!string.Equals(iss, expectedIssuerOrAudience, StringComparison.Ordinal)
                    && !string.Equals(aud, expectedIssuerOrAudience, StringComparison.Ordinal))
                {
                    error = "JWT iss/aud 与本租户不匹配";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>解出 JWT 的 iat（秒）；无 iat 返回 null。</summary>
    public static long? ReadIssuedAt(string token)
    {
        var parts = (token ?? string.Empty).Split('.');
        if (parts.Length != 3) return null;
        try
        {
            using var doc = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            return doc.RootElement.TryGetProperty("iat", out var iat) && iat.ValueKind == JsonValueKind.Number
                ? iat.GetInt64()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Sign(string signingInput, string sharedSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
    }

    /// <summary>base64url（无填充），符合 JWT 规范。</summary>
    public static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }
}
