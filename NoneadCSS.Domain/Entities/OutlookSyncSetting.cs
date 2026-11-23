namespace NoneadCSS.Domain.Entities;

/// <summary>
/// Outlook 日历同步设置 - 存储用户的 OAuth 令牌和同步配置
/// </summary>
public class OutlookSyncSetting : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>
    /// 关联用户 ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Azure AD 用户邮箱（从 Graph API /me 获取）
    /// </summary>
    public string? OutlookEmail { get; set; }

    /// <summary>
    /// Azure AD 用户 OID（用于唯一标识）
    /// </summary>
    public string? OutlookUserId { get; set; }

    // OAuth 令牌
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// 是否启用双向同步
    /// </summary>
    public bool SyncEnabled { get; set; } = true;

    /// <summary>
    /// 同步方向：both / to_outlook / from_outlook
    /// </summary>
    public string SyncDirection { get; set; } = "both";

    /// <summary>
    /// 上次同步时间
    /// </summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>
    /// 上次同步状态：success / failed / partial
    /// </summary>
    public string? LastSyncStatus { get; set; }

    /// <summary>
    /// 上次同步错误信息
    /// </summary>
    public string? LastSyncError { get; set; }

    /// <summary>
    /// Graph API delta token，用于增量同步
    /// </summary>
    public string? DeltaToken { get; set; }

    /// <summary>
    /// T000099: 用户自配置的 Azure AD 应用程序(客户端) ID。
    /// 为空时回退到 appsettings 基线 / 全局 SystemOptions 配置。
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// T000099: 用户自配置的 Azure AD 客户端密码（密文存储，EF Core HasConversion 自动加密/解密）。
    /// 为空时回退到 appsettings 基线 / 全局 SystemOptions 配置。
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// T000099: 用户自配置的 Azure AD 目录(租户) ID（common 表示多租户）。
    /// 为空时回退到 appsettings 基线。
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// T000099: 用户自配置的 OAuth 回调地址（需与 Azure 应用注册一致）。
    /// 为空时回退到 appsettings 基线。
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// T000099: 用户自配置的 OAuth 权限范围（空格分隔）。
    /// 为空时回退到 appsettings 基线。
    /// </summary>
    public string? Scopes { get; set; }

    /// <summary>
    /// 连续失败次数（成功时归零）
    /// </summary>
    public int ConsecutiveFailures { get; set; } = 0;

    /// <summary>
    /// 下次重试时间（连续失败后按指数退避计算）
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>
    /// 最近一次全量同步时间（delta token 从 null 首次变为非 null 时记录）
    /// </summary>
    public DateTimeOffset? LastFullSyncAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public byte[] RowVersion { get; set; } = null!;

    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 判断令牌是否已过期
    /// </summary>
    public bool IsTokenExpired => !TokenExpiresAt.HasValue || TokenExpiresAt.Value <= DateTime.UtcNow.AddMinutes(5);

    /// <summary>
    /// 判断是否已连接
    /// </summary>
    public bool IsConnected => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken);
}
