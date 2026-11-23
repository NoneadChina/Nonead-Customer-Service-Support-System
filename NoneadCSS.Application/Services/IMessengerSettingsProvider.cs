using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Messenger 有效配置提供器 - 合并 appsettings.json 基线配置与 SystemOptions 数据库覆盖配置。
/// MessengerService / MessengerIngestionService / MessengerClient 通过此接口获取运行时有效配置。
/// </summary>
public interface IMessengerSettingsProvider
{
    /// <summary>
    /// 当前有效的 Messenger 配置（从内存缓存同步返回，miss 时返回上次已知值并触发后台刷新）。
    /// </summary>
    MessengerOptions Current { get; }

    /// <summary>异步获取当前配置（优先读缓存，miss 时异步加载）。</summary>
    Task<MessengerOptions> GetCurrentAsync();

    /// <summary>
    /// 异步加载/刷新有效配置（SystemOptions 数据库 + appsettings.json 合并）。
    /// 配置更新后由 MessengerService 调用以使新配置立即生效。
    /// </summary>
    Task RefreshAsync();
}

/// <summary>
/// Messenger 配置在 SystemOptions 表中的键名约定（Category="messenger"，键名小写下划线）。
/// 定义在 Application 层供 MessengerService 与 Infrastructure 层 MessengerSettingsProvider 共享。
/// 敏感键（page_access_token / app_secret）值以 "ENC:" 前缀加密存储，只写不回显。
/// </summary>
public static class MessengerOptionKeys
{
    public const string AppId = "messenger.app_id";
    public const string AppSecret = "messenger.app_secret";
    public const string PageId = "messenger.page_id";
    public const string PageAccessToken = "messenger.page_access_token";
    public const string VerifyToken = "messenger.verify_token";
    public const string PageUsername = "messenger.page_username";
    public const string ApiVersion = "messenger.api_version";
    public const string MailboxId = "messenger.mailbox_id";
    public const string TextChunkChars = "messenger.text_chunk_chars";
    public const string WindowHours = "messenger.window_hours";
    public const string WarningThresholdHours = "messenger.warning_threshold_hours";
    public const string MessageTagPolicy = "messenger.message_tag_policy";
    public const string MessageTag = "messenger.message_tag";
    public const string TypingIndicator = "messenger.typing_indicator";
    public const string MarkSeen = "messenger.mark_seen";
    public const string ContactCollectionEnabled = "messenger.contact_collection_enabled";
    public const string AutoReplyEnabled = "messenger.auto_reply_enabled";
    public const string AutoReplyMessage = "messenger.auto_reply_message";
    public const string Enabled = "messenger.enabled";
    public const string SubscribedFields = "messenger.subscribed_fields";
    public const string MediaDownloadMaxMB = "messenger.media_download_max_mb";
    public const string RateLimitBackoffTries = "messenger.rate_limit_backoff_tries";
    public const string Category = "messenger";

    /// <summary>运维诊断键（非用户配置）：最近一次 Graph 190 令牌失效时间、最近一次 Graph 错误。</summary>
    public const string LastTokenInvalidAt = "messenger.last_token_invalid_at";
    public const string LastErrorCode = "messenger.last_error_code";
    public const string LastErrorFbtraceId = "messenger.last_error_fbtrace_id";
}
