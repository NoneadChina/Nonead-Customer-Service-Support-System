using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 微信有效配置提供器 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// WeChatAccessTokenService 和 WeChatClient 通过此接口获取运行时有效的微信配置，
/// 而非 IOptions&lt;WeChatOptions&gt;（仅反映 appsettings.json 启动时的静态值）。
/// </summary>
public interface IWeChatSettingsProvider
{
    /// <summary>
    /// 当前有效的微信配置（从内存缓存同步返回，缓存 miss 时返回上次已知值并触发后台刷新）。
    /// </summary>
    WeChatOptions Current { get; }

    /// <summary>异步获取当前配置（缓存 miss 时异步加载，推荐在 async 调用链中使用）</summary>
    Task<WeChatOptions> GetCurrentAsync();

    /// <summary>
    /// 异步加载/刷新有效配置（从 SystemOptions 数据库 + appsettings.json 合并）。
    /// 配置更新后由 WeChatService 调用以使新配置立即生效。
    /// </summary>
    Task RefreshAsync();
}

/// <summary>
/// 微信配置在 SystemOptions 表中的键名约定。
/// 定义在 Application 层供 WeChatService 和 Infrastructure 层 WeChatSettingsProvider 共享。
/// </summary>
public static class WeChatOptionKeys
{
    public const string AppId = "wechat.app_id";
    public const string AppSecret = "wechat.app_secret";
    public const string Token = "wechat.token";
    public const string EncodingAESKey = "wechat.encoding_aes_key";
    public const string EncryptMode = "wechat.encrypt_mode";
    public const string MailboxId = "wechat.mailbox_id";
    public const string ContactCollectionEnabled = "wechat.contact_collection_enabled";
    public const string AutoReplyEnabled = "wechat.auto_reply_enabled";
    public const string AutoReplyMessage = "wechat.auto_reply_message";
    public const string Enabled = "wechat.enabled";
    public const string Category = "wechat";
}
