using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// Telegram Bot API 集成配置（appsettings.json 基线；数据库 SystemOptions 可覆盖）。
/// 官方文档：https://core.telegram.org/bots/api
/// 说明：
///   - BotToken 由 @BotFather 签发，位于请求 URL 路径中（/bot&lt;token&gt;/&lt;method&gt;），属敏感字段，必须加密存储且不得回显、不得写日志。
///   - ApiBaseUrl 可指向自建 Bot API 服务器（https://github.com/tdlib/telegram-bot-api），默认使用官方 https://api.telegram.org。
///   - Telegram 没有 24 小时客服窗口限制，但用户可随时拉黑机器人（403），此时会话应标记为不可达而不是无限重试。
/// </summary>
public class TelegramOptions
{
    /// <summary>appsettings 配置节名称</summary>
    public const string SectionName = "Telegram";

    /// <summary>入站模式：Webhook（生产推荐）</summary>
    public const string ModeWebhook = "webhook";

    /// <summary>入站模式：轮询 getUpdates（无公网回调地址时使用）</summary>
    public const string ModePolling = "polling";

    /// <summary>Webhook 密钥请求头名称（Telegram 每次 POST 都会带上）</summary>
    public const string WebhookSecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>是否启用 Telegram 渠道</summary>
    public bool Enabled { get; set; }

    /// <summary>入站模式：webhook | polling</summary>
    [RegularExpression("^(webhook|polling)$")]
    public string Mode { get; set; } = ModeWebhook;

    /// <summary>Bot Token（敏感字段，数据库中加密存储，不通过配置接口回显）</summary>
    [MaxLength(256)]
    public string? BotToken { get; set; }

    /// <summary>Bot API 基础地址。官方：https://api.telegram.org；自建服务器示例：http://127.0.0.1:8081</summary>
    [MaxLength(256)]
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>是否使用自建（Local）Bot API 服务器：getFile 返回本机绝对路径，上传上限 2000MB</summary>
    public bool UseLocalBotApiServer { get; set; }

    /// <summary>Webhook 回调路径（相对本站域名），默认 /api/telegram/webhook</summary>
    [MaxLength(256)]
    public string CallbackPath { get; set; } = "/api/telegram/webhook";

    /// <summary>
    /// 对外回调完整地址（可选）。留空时由 App:BaseUrl + CallbackPath 推导。
    /// Telegram 要求 HTTPS（自建 Bot API 服务器允许 HTTP）。
    /// </summary>
    [MaxLength(512)]
    public string? WebhookUrl { get; set; }

    /// <summary>Webhook 密钥（敏感字段，加密存储）。setWebhook 时登记，入站请求头需常量时间比对</summary>
    [MaxLength(256)]
    public string? WebhookSecretToken { get; set; }

    /// <summary>自定义服务器证书 PEM（可选；仅自签名证书需要，官方 API 侧登记用）</summary>
    [MaxLength(4096)]
    public string? WebhookCertificate { get; set; }

    /// <summary>指定服务器 IP（可选；自建服务器与 IPv4 证书校验场景）</summary>
    [MaxLength(64)]
    public string? WebhookIpAddress { get; set; }

    /// <summary>Webhook 最大并发连接数（官方允许 1-100）</summary>
    [Range(1, 100)]
    public int MaxWebhookConnections { get; set; } = 40;

    /// <summary>允许的 Update 类型（留空 = 官方默认集）。轮询与 setWebhook 均按此过滤</summary>
    public string[] AllowedUpdates { get; set; } = [];

    /// <summary>轮询长连接超时（秒，官方允许 0-50）</summary>
    [Range(0, 50)]
    public int PollingTimeout { get; set; } = 25;

    /// <summary>轮询单次拉取条数（官方允许 1-100）</summary>
    [Range(1, 100)]
    public int PollingLimit { get; set; } = 100;

    /// <summary>切换到 polling 模式时是否自动 deleteWebhook（否则 getUpdates 会返回 409 冲突）</summary>
    public bool DeleteWebhookOnSwitch { get; set; } = true;

    /// <summary>@BotFather 中登记的 bot 用户名（用于生成 t.me 深链、前端展示）</summary>
    [MaxLength(64)]
    public string? BotUsername { get; set; }

    /// <summary>是否接收群聊/频道消息（关闭时仅处理私聊；注意 bot 在群内的隐私模式开关）</summary>
    public bool AcceptGroupMessages { get; set; }

    /// <summary>会话绑定的邮箱 ID（留空回退到第一个活跃邮箱）</summary>
    public int? MailboxId { get; set; }

    /// <summary>新会话自动回复开关</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>新会话自动回复内容（最长 500 字）</summary>
    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    /// <summary>匹配已有会话的时间窗口（小时）。Telegram 无 24h 回复窗口，默认 720 小时（30 天）内复用会话</summary>
    [Range(1, 8760)]
    public int ConversationMatchWindowHours { get; set; } = 720;

    /// <summary>出站单条文本最大字符数（官方硬限制 4096，超长自动分片）</summary>
    [Range(1, 4096)]
    public int MaxMessageChars { get; set; } = 4096;

    /// <summary>全局出站速率限制（条/秒）。官方上限约 30 条/秒（所有 chat 合计）</summary>
    [Range(1, 30)]
    public int RateLimitGlobalPerSeconds { get; set; } = 25;

    /// <summary>单个 chat 出站速率限制（条/秒）。官方约 1 条/秒</summary>
    [Range(1, 10)]
    public int RateLimitPerChatPerSeconds { get; set; } = 1;

    /// <summary>429/409 等限流时的最大本地等待秒数；超过则直接返回让调用方（消息队列）延后重试，避免长时间占用 worker</summary>
    [Range(0, 120)]
    public int MaxLocalBackoffSeconds { get; set; } = 10;

    /// <summary>状态探测（getMe）超时秒数</summary>
    [Range(1, 60)]
    public int StatusCheckTimeoutSeconds { get; set; } = 8;

    /// <summary>账号绑定深链 token 的 HMAC 密钥（敏感字段，加密存储；留空则不启用深链绑定）</summary>
    [MaxLength(256)]
    public string? LinkTokenHmacKey { get; set; }

    /// <summary>账号绑定深链 token 有效期（分钟）</summary>
    [Range(1, 1440)]
    public int LinkTokenTtlMinutes { get; set; } = 30;

    /// <summary>是否已具备最小可用配置（BotToken 必填）</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);

    /// <summary>是否为轮询模式</summary>
    public bool IsPollingMode => string.Equals(Mode, ModePolling, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为 Webhook 模式</summary>
    public bool IsWebhookMode => !IsPollingMode;
}
