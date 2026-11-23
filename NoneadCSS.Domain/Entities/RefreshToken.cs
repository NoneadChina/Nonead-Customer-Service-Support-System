namespace NoneadCSS.Domain.Entities;

/// <summary>
/// JWT Refresh Token，用于在 Access Token 过期后获取新的访问令牌。
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>
    /// Refresh Token 字符串（随机生成的 opaque token）。
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 令牌过期时间。
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 令牌创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 创建时使用的 IP 地址。
    /// </summary>
    public string CreatedByIp { get; set; } = string.Empty;

    /// <summary>
    /// 撤销时间（轮换或主动登出时设置）。
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// 若因轮换被撤销，记录替换它的新令牌。
    /// </summary>
    public string? ReplacedByToken { get; set; }

    /// <summary>
    /// 撤销原因（可选）。
    /// </summary>
    public string? ReasonRevoked { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive => !IsExpired && !IsRevoked;

    /// <summary>
    /// 撤销令牌
    /// </summary>
    /// <param name="reason">撤销原因</param>
    /// <param name="replacedByToken">替换令牌（轮换时使用）</param>
    public void Revoke(string? reason = null, string? replacedByToken = null)
    {
        RevokedAt = DateTime.UtcNow;
        ReasonRevoked = reason;
        ReplacedByToken = replacedByToken;
    }

    /// <summary>
    /// 创建新的 Refresh Token
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="token">令牌值</param>
    /// <param name="expiresAt">过期时间</param>
    /// <param name="createdByIp">创建IP</param>
    /// <returns>新的 Refresh Token 实例</returns>
    public static RefreshToken Create(int userId, string token, DateTime expiresAt, string createdByIp)
    {
        return new RefreshToken
        {
            UserId = userId,
            Token = token,
            ExpiresAt = expiresAt,
            CreatedByIp = createdByIp
        };
    }
}
