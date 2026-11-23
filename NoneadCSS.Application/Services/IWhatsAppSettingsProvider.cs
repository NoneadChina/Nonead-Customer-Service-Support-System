using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services;

/// <summary>
/// WhatsApp 有效配置提供器 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// WhatsAppService 和 WhatsAppClient 通过此接口获取运行时有效的 WhatsApp 配置。
/// </summary>
public interface IWhatsAppSettingsProvider
{
    /// <summary>
    /// 当前有效的 WhatsApp 配置（从内存缓存同步返回，首次访问时异步加载）。
    /// 若缓存未初始化，返回 appsettings.json 的基线值。
    /// </summary>
    WhatsAppOptions Current { get; }

    /// <summary>
    /// 异步获取当前配置（优先读缓存，miss 时异步加载）。
    /// </summary>
    Task<WhatsAppOptions> GetCurrentAsync();

    /// <summary>
    /// 异步加载/刷新有效配置（从 SystemOptions 数据库 + appsettings.json 合并）。
    /// 配置更新后由 WhatsAppService 调用以使新配置立即生效。
    /// </summary>
    Task RefreshAsync();
}

/// <summary>
/// WhatsApp 配置在 SystemOptions 表中的键名约定。
/// 定义在 Application 层供 WhatsAppService 和 Infrastructure 层 WhatsAppSettingsProvider 共享。
/// </summary>
public static class WhatsAppOptionKeys
{
    public const string PhoneNumberId = "whatsapp.phone_number_id";
    public const string AccessToken = "whatsapp.access_token";
    public const string AppSecret = "whatsapp.app_secret";
    public const string VerifyToken = "whatsapp.verify_token";
    public const string BusinessPhoneNumber = "whatsapp.business_phone_number";
    public const string ApiVersion = "whatsapp.api_version";
    public const string MailboxId = "whatsapp.mailbox_id";
    public const string ContactCollectionEnabled = "whatsapp.contact_collection_enabled";
    public const string AutoReplyEnabled = "whatsapp.auto_reply_enabled";
    public const string AutoReplyMessage = "whatsapp.auto_reply_message";
    public const string Enabled = "whatsapp.enabled";
    public const string Category = "whatsapp";
}
