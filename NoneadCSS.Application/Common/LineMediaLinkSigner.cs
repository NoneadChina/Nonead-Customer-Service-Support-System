using System.Security.Cryptography;
using System.Text;

namespace NoneadCSS.Application.Common;

/// <summary>
/// LINE 出站附件直链的签发与校验。
/// LINE 没有聊天媒体上传接口：image/video/audio/file 消息必须给本部署的公网 HTTPS 直链，
/// 而附件表本身没有公网匿名入口（AttachmentsController 的下载接口都要鉴权）。
/// 因此这里用 ChannelSecret 作为 HMAC 密钥，对 "attachmentId|expiresUnixSeconds" 签名，
/// 由 /api/line/media/{id} 匿名端点校验后回源 AttachmentService 取文件字节。
/// 该链接不含任何可推导的凭据，且带过期时间。
/// </summary>
public static class LineMediaLinkSigner
{
    /// <summary>域分隔前缀，避免 ChannelSecret 的 HMAC 被跨用途复用</summary>
    private const string DomainPrefix = "line-media-v1:";

    /// <summary>签发签名查询串（不含 base url），形如 ?exp=…&amp;sig=…</summary>
    public static string BuildQuery(int attachmentId, DateTimeOffset expiresAt, string secret)
    {
        var exp = expiresAt.ToUnixTimeSeconds();
        var sig = Sign(attachmentId, exp, secret);
        return $"?exp={exp}&sig={Uri.EscapeDataString(sig)}";
    }

    /// <summary>拼出完整公网直链（base 末尾斜杠会被去掉）</summary>
    public static string BuildUrl(string publicBaseUrl, int attachmentId, DateTimeOffset expiresAt, string secret, string? pathSuffix = null)
    {
        var baseUri = publicBaseUrl.TrimEnd('/');
        return $"{baseUri}/api/line/media/{attachmentId}{pathSuffix}{BuildQuery(attachmentId, expiresAt, secret)}";
    }

    /// <summary>校验直链签名与过期时间；任何异常输入都返回 false</summary>
    public static bool Validate(int attachmentId, long expiresUnixSeconds, string? signature, string secret, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(signature) || expiresUnixSeconds <= 0)
        {
            return false;
        }

        // 已过期（允许 60s 时钟偏差）
        if (expiresUnixSeconds < nowUtc.ToUnixTimeSeconds() - 60)
        {
            return false;
        }

        var expected = Sign(attachmentId, expiresUnixSeconds, secret);
        // 十六进制摘要按大小写不敏感比较：直链常被 CDN / 代理 / 人工复制改写大小写，
        // 定长常量时间比较的语义不受影响（仍是 HMAC-SHA256 全 256 位）。
        // 入站 X-Line-Signature 是 Base64（大小写敏感），由 LineWebhookParser.VerifySignature 单独校验，不在此处放宽。
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
    }

    private static string Sign(int attachmentId, long expiresUnixSeconds, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var payload = $"{DomainPrefix}{attachmentId}|{expiresUnixSeconds}";
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
