using System.Security.Cryptography;
using System.Text;

namespace NoneadCSS.Application.Security;

/// <summary>
/// 敏感令牌（API Key / Refresh Token / 密码重置令牌）的 HMAC-SHA256 哈希工具。
/// 使用服务器密钥进行 HMAC-SHA256 哈希，防止彩虹表攻击。
/// 数据库仅存储哈希值，明文仅在生成时返回给客户端一次。
/// </summary>
public static class SecretHasher
{
    private static byte[]? _key;

    /// <summary>
    /// 使用服务器密钥初始化 SecretHasher。必须在应用启动时调用。
    /// </summary>
    /// <param name="secret">服务器密钥（至少 32 字节）</param>
    public static void Initialize(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("SecretHasher 服务器密钥不能为空", nameof(secret));

        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new ArgumentException("SecretHasher 服务器密钥长度必须至少 32 字节", nameof(secret));

        _key = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    /// 使用 HMAC-SHA256 哈希输入字符串，返回小写十六进制字符串。
    /// </summary>
    public static string Hash(string input)
    {
        if (_key == null)
            throw new InvalidOperationException("SecretHasher 未初始化，请在应用启动时调用 Initialize()");

        using var hmac = new HMACSHA256(_key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
