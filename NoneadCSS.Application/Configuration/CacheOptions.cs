namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 缓存策略配置选项（统一管理各服务/Provider 的缓存过期时间）
/// </summary>
public class CacheOptions
{
    /// <summary>通用缓存过期时间（分钟），适用于 Module/Role/SystemOption/Localization 等</summary>
    public int DefaultCacheMinutes { get; set; } = 10;

    /// <summary>邮箱缓存过期时间（分钟）</summary>
    public int MailboxCacheMinutes { get; set; } = 5;

    /// <summary>密码策略缓存过期时间（分钟）</summary>
    public int PasswordPolicyCacheMinutes { get; set; } = 5;

    /// <summary>本地化资源刷新间隔（分钟），略短于 DefaultCacheMinutes 以实现平滑刷新</summary>
    public int LocalizationRefreshIntervalMinutes { get; set; } = 9;

    /// <summary>第三方渠道 SettingsProvider 缓存过期时间（分钟）</summary>
    public int ChannelSettingsCacheMinutes { get; set; } = 10;
}
