namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 密码重置令牌
/// </summary>
public class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
    public bool IsUsed => UsedAt.HasValue;
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    public bool IsValid => !IsUsed && !IsExpired;

    public User? User { get; set; }

    /// <summary>
    /// 标记令牌为已使用
    /// </summary>
    public void MarkAsUsed()
    {
        UsedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 创建密码重置令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="token">令牌值</param>
    /// <param name="expiresAt">过期时间</param>
    /// <returns>新的密码重置令牌实例</returns>
    public static PasswordResetToken Create(int userId, string token, DateTime expiresAt)
    {
        return new PasswordResetToken
        {
            UserId = userId,
            Token = token,
            ExpiresAt = expiresAt
        };
    }
}
