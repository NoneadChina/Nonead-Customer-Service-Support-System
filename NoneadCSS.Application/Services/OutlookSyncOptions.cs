namespace NoneadCSS.Application.Services;

/// <summary>
/// Azure AD / Microsoft Graph 配置选项
/// </summary>
public class OutlookSyncOptions
{
    public const string SectionName = "AzureAd";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// 租户 ID。common 表示多租户；specific tenant GUID 表示单租户
    /// </summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// OAuth 回调地址（必须与 Azure 应用注册一致）
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// 请求的权限范围（空格分隔）
    /// </summary>
    public string Scopes { get; set; } = "Calendars.ReadWrite offline_access User.Read";

    /// <summary>
    /// 授权端点 URL 模板（{0} = TenantId）
    /// </summary>
    public string AuthorizeUrl { get; set; } = "https://login.microsoftonline.com/{0}/oauth2/v2.0/authorize";

    /// <summary>
    /// 令牌端点 URL 模板（{0} = TenantId）
    /// </summary>
    public string TokenUrl { get; set; } = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";

    /// <summary>
    /// Microsoft Graph API 基地址
    /// </summary>
    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// 判断是否已配置必要参数
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri);

    public List<int> BackoffMinutes { get; set; } = new() { 1, 5, 15, 60, 240 };
    public int AutoDisableThreshold { get; set; } = 10;
    public int LogFlushBatchSize { get; set; } = 50;
    public int MappingBatchSize { get; set; } = 500;
    public int DeltaPageSize { get; set; } = 200;
    public int MaxHttpConcurrency { get; set; } = 5;
    public int MaxDeltaRestarts { get; set; } = 2;

    /// <summary>
    /// P4: Outlook 同步冲突解决策略。
    /// 可选值：
    /// - <c>last_write_wins</c>（默认）：按时间戳比较，较新的一方胜出。
    /// - <c>local_wins</c>：本地始终胜出，Outlook 更新被丢弃。
    /// - <c>outlook_wins</c>：Outlook 始终胜出，本地被覆盖。
    /// 配置路径：Calendar:OutlookSync:ConflictStrategy 或 Calendar:OutlookSyncConflictStrategy（向后兼容）
    /// </summary>
    public string ConflictStrategy { get; set; } = "last_write_wins";

    /// <summary>
    /// 校验冲突策略值是否合法。不合法时返回 false，调用方应回退到默认值并记录警告。
    /// </summary>
    public bool IsValidConflictStrategy() =>
        ConflictStrategy is "last_write_wins" or "local_wins" or "outlook_wins";
}
