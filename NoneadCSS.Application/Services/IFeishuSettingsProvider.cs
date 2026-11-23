using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 飞书配置提供者接口 - 合并 appsettings.json 基线和 SystemOptions 数据库配置，
/// 提供当前生效的飞书配置（内存缓存）。
/// </summary>
public interface IFeishuSettingsProvider
{
    /// <summary>当前生效的飞书配置（从内存缓存读取，缓存 miss 时返回上次已知值并触发后台刷新）</summary>
    FeishuOptions Current { get; }

    /// <summary>异步获取当前配置（缓存 miss 时异步加载，推荐在 async 调用链中使用）</summary>
    Task<FeishuOptions> GetCurrentAsync();

    /// <summary>从数据库重新加载配置并刷新内存缓存</summary>
    Task RefreshAsync();
}

/// <summary>飞书 SystemOptions 键名常量</summary>
public static class FeishuOptionKeys
{
    public const string Category = "feishu";
    public const string AppId = "feishu.app_id";
    public const string AppSecret = "feishu.app_secret";
    public const string VerificationToken = "feishu.verification_token";
    public const string EncryptKey = "feishu.encrypt_key";
    public const string EncryptEnabled = "feishu.encrypt_enabled";
    public const string MailboxId = "feishu.mailbox_id";
    public const string ContactCollectionEnabled = "feishu.contact_collection_enabled";
    public const string AutoReplyEnabled = "feishu.auto_reply_enabled";
    public const string AutoReplyMessage = "feishu.auto_reply_message";
    public const string Enabled = "feishu.enabled";
    public const string ReceiveMode = "feishu.receive_mode";
}
