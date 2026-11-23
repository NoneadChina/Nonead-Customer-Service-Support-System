using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Telegram 有效配置提供器 - 合并 appsettings.json 基线与 SystemOptions 数据库覆盖（敏感字段解密后返回）。
/// 与 WhatsAppSettingsProvider 同构：注册为 Singleton，读取一律通过 <see cref="Current"/>。
/// </summary>
public interface ITelegramSettingsProvider
{
    /// <summary>当前有效的 Telegram 配置（含解密后的敏感字段）</summary>
    TelegramOptions Current { get; }

    /// <summary>异步获取当前有效配置（缓存 miss 时同步加载）</summary>
    Task<TelegramOptions> GetCurrentAsync();

    /// <summary>强制刷新缓存（保存配置后调用）</summary>
    Task RefreshAsync();
}

/// <summary>
/// Telegram 配置在 SystemOptions 表中的键名（Category = "telegram"，键名小写下划线）。
/// BotToken / WebhookSecretToken / LinkTokenHmacKey 为敏感项，值以 "ENC:" 前缀加密存储。
/// </summary>
public static class TelegramOptionKeys
{
    public const string Category = "telegram";

    public const string Enabled = "telegram.enabled";
    public const string Mode = "telegram.mode";
    public const string BotToken = "telegram.bot_token";
    public const string ApiBaseUrl = "telegram.api_base_url";
    public const string UseLocalBotApiServer = "telegram.use_local_bot_api_server";
    public const string CallbackPath = "telegram.callback_path";
    public const string WebhookUrl = "telegram.webhook_url";
    public const string WebhookSecretToken = "telegram.webhook_secret_token";
    public const string WebhookIpAddress = "telegram.webhook_ip_address";
    public const string MaxWebhookConnections = "telegram.max_webhook_connections";
    public const string AllowedUpdates = "telegram.allowed_updates";
    public const string PollingTimeout = "telegram.polling_timeout";
    public const string PollingLimit = "telegram.polling_limit";
    public const string DeleteWebhookOnSwitch = "telegram.delete_webhook_on_switch";
    public const string BotUsername = "telegram.bot_username";
    public const string AcceptGroupMessages = "telegram.accept_group_messages";
    public const string MailboxId = "telegram.mailbox_id";
    public const string AutoReplyEnabled = "telegram.auto_reply_enabled";
    public const string AutoReplyMessage = "telegram.auto_reply_message";
    public const string ConversationMatchWindowHours = "telegram.conversation_match_window_hours";
    public const string MaxMessageChars = "telegram.max_message_chars";
    public const string RateLimitGlobalPerSeconds = "telegram.rate_limit_global_per_seconds";
    public const string RateLimitPerChatPerSeconds = "telegram.rate_limit_per_chat_per_seconds";
    public const string MaxLocalBackoffSeconds = "telegram.max_local_backoff_seconds";
    public const string StatusCheckTimeoutSeconds = "telegram.status_check_timeout_seconds";
    public const string LinkTokenHmacKey = "telegram.link_token_hmac_key";
    public const string LinkTokenTtlMinutes = "telegram.link_token_ttl_minutes";

    /// <summary>轮询游标（last update_id + 1）持久化键，非配置项但复用 SystemOptions 存储，避免新增表</summary>
    public const string PollingOffsetCursor = "telegram.polling_offset_cursor";
}
