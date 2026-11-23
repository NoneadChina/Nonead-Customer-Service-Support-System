namespace NoneadCSS.Application.Services;

/// <summary>
/// Outlook 同步有效配置提供器 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// OutlookOAuthService、OutlookSyncService 与 OutlookSyncController 通过此接口获取运行时有效的 Azure AD 凭据。
/// 模式与 <see cref="IDingDingSettingsProvider"/> 一致：Singleton + IMemoryCache + IServiceScopeFactory + IEncryptionService。
/// T000099: 新增 GetForUserAsync(userId)，支持每个用户自配置 Azure AD 凭据（用户级 > 全局基线）。
/// </summary>
public interface IOutlookSettingsProvider
{
    /// <summary>
    /// 当前有效的全局 Outlook 同步配置（从内存缓存同步返回，缓存 miss 时返回上次已知值并触发后台刷新）。
    /// 用于同步上下文中（如 <see cref="IOutlookOAuthService.GetAuthorizeUrl"/>），避免 sync-over-async 阻塞线程池。
    /// 注意：仅返回全局基线 + SystemOptions 覆盖，不包含用户级凭据。用户级凭据请使用 <see cref="GetForUserAsync"/>。
    /// </summary>
    OutlookSyncOptions Current { get; }

    /// <summary>异步获取当前全局配置（缓存 miss 时异步加载，推荐在 async 调用链中使用）</summary>
    Task<OutlookSyncOptions> GetCurrentAsync();

    /// <summary>
    /// T000099: 异步获取指定用户的有效配置（用户级凭据 > 全局基线）。
    /// 用户级凭据存储在 OutlookSyncSetting 表的 ClientId/ClientSecret/TenantId/RedirectUri/Scopes 字段，
    /// 为空时回退到全局基线（appsettings + SystemOptions）。
    /// 推荐在 OAuth 授权/换码/刷新令牌等需要用户级凭据的 async 调用链中使用。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <returns>合并后的有效配置</returns>
    Task<OutlookSyncOptions> GetForUserAsync(int userId);

    /// <summary>
    /// 异步加载/刷新有效配置（从 SystemOptions 数据库 + appsettings.json 合并）。
    /// 配置更新后由 OutlookSyncController.UpdateAdminSettings 调用以使新配置立即生效。
    /// </summary>
    Task RefreshAsync();
}

/// <summary>
/// Outlook 同步配置在 SystemOptions 表中的键名约定。
/// 定义在 Application 层供 OutlookSyncController 与 Infrastructure 层 OutlookSettingsProvider 共享。
/// </summary>
public static class OutlookOptionKeys
{
    public const string ClientId = "outlook.client_id";
    public const string ClientSecret = "outlook.client_secret";
    public const string TenantId = "outlook.tenant_id";
    public const string RedirectUri = "outlook.redirect_uri";
    public const string Scopes = "outlook.scopes";
    public const string Category = "outlook";
}
