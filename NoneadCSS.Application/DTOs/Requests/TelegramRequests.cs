using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新 Telegram 配置请求。敏感字段（BotToken / WebhookSecretToken / LinkTokenHmacKey）为 null 或空白表示不修改；
/// 想撤销已保存的密钥必须显式把对应的 Clear* 开关置 true（审计 #5：只靠"提交空字符串"清不掉旧值，
/// 前端表单留空与"不改这一项"无法区分，泄露的密钥会一直留在库里）。
/// 保存后服务端会自动同步 setWebhook / deleteWebhook。
/// </summary>
public class UpdateTelegramSettingsRequest
{
    public bool? Enabled { get; set; }

    /// <summary>入站模式：webhook | polling</summary>
    [RegularExpression("^(webhook|polling)$", ErrorMessage = "Mode 只能为 webhook 或 polling")]
    public string? Mode { get; set; }

    /// <summary>Bot Token（敏感字段，加密存储，不回显）</summary>
    [MaxLength(256)]
    public string? BotToken { get; set; }

    /// <summary>显式清除库里已保存的 BotToken（true 时忽略 BotToken 字段；清除后 Telegram 视为未配置）</summary>
    public bool ClearBotToken { get; set; }

    [MaxLength(256)]
    public string? ApiBaseUrl { get; set; }

    public bool? UseLocalBotApiServer { get; set; }

    [MaxLength(512)]
    public string? WebhookUrl { get; set; }

    /// <summary>Webhook 密钥（敏感字段，加密存储，不回显）</summary>
    [MaxLength(256)]
    public string? WebhookSecretToken { get; set; }

    /// <summary>
    /// 显式清除库里已保存的 WebhookSecretToken（true 时忽略 WebhookSecretToken 字段）。
    /// 清除后 webhook 入站端点立即拒绝所有请求（TelegramController 在读请求体之前就 Unauthorized），
    /// 若仍处于启用的 webhook 模式，保存流程会由 SetupWebhookAsync 轮换出一个新密钥。
    /// </summary>
    public bool ClearWebhookSecretToken { get; set; }

    [MaxLength(64)]
    public string? WebhookIpAddress { get; set; }

    [Range(1, 100)]
    public int? MaxWebhookConnections { get; set; }

    /// <summary>允许的 Update 类型列表；空数组表示使用官方默认集合</summary>
    public string[]? AllowedUpdates { get; set; }

    [Range(0, 50)]
    public int? PollingTimeout { get; set; }

    [Range(1, 100)]
    public int? PollingLimit { get; set; }

    public bool? DeleteWebhookOnSwitch { get; set; }

    [MaxLength(64)]
    public string? BotUsername { get; set; }

    public bool? AcceptGroupMessages { get; set; }

    public int? MailboxId { get; set; }

    public bool? AutoReplyEnabled { get; set; }

    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    [Range(1, 8760)]
    public int? ConversationMatchWindowHours { get; set; }

    [Range(1, 4096)]
    public int? MaxMessageChars { get; set; }

    [Range(1, 30)]
    public int? RateLimitGlobalPerSeconds { get; set; }

    [Range(1, 10)]
    public int? RateLimitPerChatPerSeconds { get; set; }

    /// <summary>深链绑定令牌 HMAC 密钥（敏感字段，加密存储，不回显）</summary>
    [MaxLength(256)]
    public string? LinkTokenHmacKey { get; set; }

    /// <summary>显式清除库里已保存的 LinkTokenHmacKey（true 时忽略该字段；清除后深链绑定令牌不再可用）</summary>
    public bool ClearLinkTokenHmacKey { get; set; }

    [Range(1, 1440)]
    public int? LinkTokenTtlMinutes { get; set; }
}

/// <summary>Telegram 坐席回复请求</summary>
public class TelegramReplyRequest
{
    [Required]
    public int ConversationId { get; set; }

    /// <summary>回复文本（与 AttachmentIds 至少一个非空；超过 MaxMessageChars 自动分片）</summary>
    public string? Content { get; set; }

    /// <summary>站内已上传附件 ID 列表</summary>
    public List<int>? AttachmentIds { get; set; }

    /// <summary>是否引用回复客户最后一条消息（reply_parameters）</summary>
    public bool QuoteLastCustomerMessage { get; set; }

    /// <summary>
    /// 允许值 none / HTML / Markdown；null、空串或 "none" 表示不发送 parse_mode（纯文本）。
    /// </summary>
    public string? ParseMode { get; set; }
}

/// <summary>Telegram Webhook 登记请求（保存配置后同步 / 手动重试）</summary>
public class TelegramWebhookSetupRequest
{
    /// <summary>覆盖默认回调地址（留空 = App:BaseUrl + CallbackPath）</summary>
    [MaxLength(512)]
    public string? WebhookUrl { get; set; }

    /// <summary>是否丢弃积压的 update（默认 false，避免丢消息）</summary>
    public bool DropPendingUpdates { get; set; }
}

/// <summary>Telegram 标记不可达状态请求（坐席手动清除不可达标记）</summary>
public class TelegramUnreachableStateRequest
{
    [Required]
    public int ConversationId { get; set; }
}
