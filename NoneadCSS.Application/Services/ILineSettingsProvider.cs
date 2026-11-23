using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services;

/// <summary>
/// LINE 有效配置提供者：appsettings 基线 + 数据库 SystemOptions 覆盖，敏感字段解密后缓存。
/// </summary>
public interface ILineSettingsProvider
{
    /// <summary>当前有效配置（同步读取，供 HttpClient / 后台任务使用）</summary>
    LineOptions Current { get; }

    /// <summary>异步获取有效配置（首次会加载数据库）</summary>
    Task<LineOptions> GetCurrentAsync();

    /// <summary>强制刷新缓存（保存设置后调用）</summary>
    Task RefreshAsync();
}

/// <summary>LINE 配置项在 SystemOptions 表中的键名（命名遵循 whatsapp.* 先例：小写 + 下划线）</summary>
public static class LineOptionKeys
{
    public const string Category = "line";

    public const string ChannelId = "line.channel_id";
    public const string ChannelSecret = "line.channel_secret";
    public const string ChannelAccessToken = "line.channel_access_token";
    public const string ChannelAccessTokenKeyId = "line.channel_access_token_key_id";
    public const string ApiBaseUrl = "line.api_base_url";
    public const string DataApiBaseUrl = "line.data_api_base_url";
    public const string AttachmentPublicBaseUrl = "line.attachment_public_base_url";
    public const string AttachmentPublicUrlTtlMinutes = "line.attachment_public_url_ttl_minutes";
    public const string WebhookEnabled = "line.webhook_enabled";
    public const string RichMenuId = "line.rich_menu_id";
    public const string MarkAsReadEnabled = "line.mark_as_read_enabled";
    public const string LoadingIndicatorEnabled = "line.loading_indicator_enabled";
    public const string AutoReplyEnabled = "line.auto_reply_enabled";
    public const string AutoReplyMessage = "line.auto_reply_message";
    public const string ProfileEnrichEnabled = "line.profile_enrich_enabled";
    public const string TextChunkChars = "line.text_chunk_chars";
    public const string MaxMessagesPerRequest = "line.max_messages_per_request";
    public const string ReplyTokenTtlSeconds = "line.reply_token_ttl_seconds";
    public const string HumanHandoverMuteMinutes = "line.human_handover_mute_minutes";
    public const string QuotaWarnPercent = "line.quota_warn_percent";
    public const string StatusCheckTimeoutSeconds = "line.status_check_timeout_seconds";
    public const string RetryMaxAttempts = "line.retry_max_attempts";
    public const string MaxInboundMediaBytes = "line.max_inbound_media_bytes";
    public const string MaxOutboundAttachmentBytes = "line.max_outbound_attachment_bytes";
    public const string OutboundImageExtensions = "line.outbound_image_extensions";
    public const string MailboxId = "line.mailbox_id";
    public const string Enabled = "line.enabled";

    // ── 运行期观测状态（不是用户配置，设置页不写这两项）────────────────────────────
    // 审计 #11：状态卡的 LastError 永远是空。LineClient 不持有错误上下文（ILineClient 没有 LastError，
    // 且客户端侧的失败被吞成 null），所以由 LineService 在出站失败点把「HTTP 状态 + 可读原因」落库，
    // 状态页回读 —— 与 Messenger 的 messenger.last_error_* 同口径（MessengerService.GetStatusAsync）。
    /// <summary>最近一次 LINE 出站失败的 HTTP 状态码（字符串落库，读回时 TryParse）</summary>
    public const string LastErrorHttpStatus = "line.last_error_http_status";
    /// <summary>最近一次 LINE 出站失败的可读原因（LINE 返回体里的 message）</summary>
    public const string LastErrorMessage = "line.last_error_message";
}
