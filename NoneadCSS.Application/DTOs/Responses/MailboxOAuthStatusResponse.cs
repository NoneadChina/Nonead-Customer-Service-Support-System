namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 邮箱OAuth授权状态响应
/// </summary>
public class MailboxOAuthStatusResponse
{
    public int MailboxId { get; set; }
    public bool Authorized { get; set; }
    /// <summary>是否已存储 AccessToken（不返回明文，仅返回布尔值用于前端判断状态）</summary>
    public bool HasAccessToken { get; set; }
    /// <summary>是否已存储 RefreshToken（不返回明文，仅返回布尔值用于前端判断状态）</summary>
    public bool HasRefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Email { get; set; }
    public string? Error { get; set; }
}
