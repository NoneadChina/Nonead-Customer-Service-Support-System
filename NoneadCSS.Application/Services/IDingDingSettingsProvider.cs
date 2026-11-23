using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 钉钉有效配置提供器 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// DingDingService 和 DingDingClient 通过此接口获取运行时有效的钉钉配置。
/// </summary>
public interface IDingDingSettingsProvider
{
    /// <summary>
    /// 当前有效的钉钉配置（从内存缓存同步返回，缓存 miss 时返回上次已知值并触发后台刷新）。
    /// </summary>
    DingDingOptions Current { get; }

    /// <summary>异步获取当前配置（缓存 miss 时异步加载，推荐在 async 调用链中使用）</summary>
    Task<DingDingOptions> GetCurrentAsync();

    /// <summary>
    /// 异步加载/刷新有效配置（从 SystemOptions 数据库 + appsettings.json 合并）。
    /// 配置更新后由 DingDingService 调用以使新配置立即生效。
    /// </summary>
    Task RefreshAsync();
}

/// <summary>
/// 钉钉配置在 SystemOptions 表中的键名约定。
/// 定义在 Application 层供 DingDingService 和 Infrastructure 层 DingDingSettingsProvider 共享。
/// </summary>
public static class DingDingOptionKeys
{
    public const string AppKey = "dingding.app_key";
    public const string AppSecret = "dingding.app_secret";
    public const string AgentId = "dingding.agent_id";
    public const string RobotCode = "dingding.robot_code";
    public const string CallbackToken = "dingding.callback_token";
    public const string EncodingAesKey = "dingding.encoding_aes_key";
    public const string ReceiveMode = "dingding.receive_mode";
    public const string MailboxId = "dingding.mailbox_id";
    public const string ContactCollectionEnabled = "dingding.contact_collection_enabled";
    public const string AutoReplyEnabled = "dingding.auto_reply_enabled";
    public const string AutoReplyMessage = "dingding.auto_reply_message";
    public const string Enabled = "dingding.enabled";
    public const string Category = "dingding";
}
