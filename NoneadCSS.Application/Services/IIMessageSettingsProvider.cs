using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services;

/// <summary>
/// iMessage 有效配置提供器：合并 appsettings 基线与 SystemOptions 数据库覆盖（密钥以 ENC: 前缀加密存储）。
/// 与 WhatsAppSettingsProvider 同一模式（Singleton + IMemoryCache + 后台刷新）。
/// </summary>
public interface IIMessageSettingsProvider
{
    /// <summary>当前有效配置（非阻塞：缓存未命中时触发后台刷新并回落上次已知值/基线）。</summary>
    IMessageOptions Current { get; }

    /// <summary>异步获取有效配置（缓存未命中时同步刷新一次）。</summary>
    Task<IMessageOptions> GetCurrentAsync();

    /// <summary>强制刷新缓存。</summary>
    Task RefreshAsync();
}

/// <summary>
/// iMessage 在 SystemOptions 表中的键名（Category = "imessage"，键名小写下划线）。
/// </summary>
public static class IMessageOptionKeys
{
    public const string Category = "imessage";

    // 共享
    public const string Mode = "imessage.mode";
    public const string Enabled = "imessage.enabled";

    // bridge（BlueBubbles）
    public const string BaseUrl = "imessage.base_url";
    public const string AuthStyle = "imessage.auth_style";
    public const string Password = "imessage.password";
    /// <summary>bridge webhook 共享密钥（X-Webhook-Secret / ?secret=）；与 AMB 的 webhook_secret 是两回事。</summary>
    public const string BridgeWebhookSecret = "imessage.bridge_webhook_secret";
    public const string WebhookPath = "imessage.webhook_path";
    public const string WebhookBaseUrl = "imessage.webhook_base_url";
    public const string SendMethod = "imessage.send_method";
    public const string AllowSmsFallback = "imessage.allow_sms_fallback";
    public const string AllowList = "imessage.allow_list";
    public const string RateLimitPerMinute = "imessage.rate_limit_per_minute";
    public const string VerifyTls = "imessage.verify_tls";
    public const string HealthEndpoint = "imessage.health_endpoint";
    public const string MinServerVersion = "imessage.min_server_version";
    public const string ReadHistoryOnStart = "imessage.read_history_on_start";

    // 会话匹配 / 邮箱 / 自动回复
    public const string MatchWindowHours = "imessage.match_window_hours";
    public const string MailboxId = "imessage.mailbox_id";
    public const string AutoReplyEnabled = "imessage.auto_reply_enabled";
    public const string AutoReplyMessage = "imessage.auto_reply_message";
    public const string StatusCheckTimeoutSeconds = "imessage.status_check_timeout_seconds";

    // amb_msp
    public const string MspConnector = "imessage.msp_connector";
    public const string ApiBaseUrl = "imessage.api_base_url";
    public const string ApiKey = "imessage.api_key";
    public const string WebhookSecret = "imessage.webhook_secret";
    public const string BusinessIdentifier = "imessage.business_identifier";
    public const string FallbackChannel = "imessage.fallback_channel";

    // amb_apple_direct（实验性）
    public const string MspId = "imessage.msp_id";
    public const string MspSharedSecret = "imessage.msp_shared_secret";
    public const string TokenTtlSeconds = "imessage.token_ttl_seconds";
    public const string GatewayBaseUrl = "imessage.gateway_base_url";
    public const string BusinessId = "imessage.business_id";
    public const string InboundPath = "imessage.inbound_path";
    public const string AttachmentKeySize = "imessage.attachment_key_size";
    public const string MaxBodyBytes = "imessage.max_body_bytes";
    public const string ExperimentalAppleDirect = "imessage.experimental_apple_direct";
}
