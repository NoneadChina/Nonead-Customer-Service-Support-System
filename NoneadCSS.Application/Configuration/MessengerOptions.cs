using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// Facebook Messenger Platform (Graph API) 接入配置选项。
/// 对应 appsettings.json 的 Messenger 节，运行时由 SystemOptions 数据库覆盖
/// （见 MessengerSettingsProvider，密钥类字段以 ENC: 前缀加密存储）。
/// </summary>
public class MessengerOptions
{
    public const string SectionName = "Messenger";

    /// <summary>Meta 应用 App ID（公开标识符，非密钥，用于排查与 Page Inbox 核对）</summary>
    [MaxLength(64)]
    public string? AppId { get; set; }

    /// <summary>App Secret（X-Hub-Signature-256 的 HMAC 密钥，敏感字段，加密存储）</summary>
    [MaxLength(128)]
    public string? AppSecret { get; set; }

    /// <summary>公共主页 Page ID（Webhook entry.id / recipient.id，也是 PSID 的作用域）</summary>
    [MaxLength(64)]
    public string? PageId { get; set; }

    /// <summary>Page Access Token（Send API 的 Bearer 令牌，敏感字段，加密存储；无刷新端点，到期需人工换发）</summary>
    [MaxLength(512)]
    public string? PageAccessToken { get; set; }

    /// <summary>Webhook 验证 Token（Meta 配置 Webhook 时填写的 Verify Token）</summary>
    [MaxLength(128)]
    public string? VerifyToken { get; set; }

    /// <summary>公共主页句柄 / @username（仅用于坐席界面展示，可空）</summary>
    [MaxLength(64)]
    public string? PageUsername { get; set; }

    /// <summary>
    /// Graph API 版本。必须是配置项，不要写死：Meta 无 v2x 别名，版本号随时间滚动
    /// （docs/integration/messenger.md：当前 v25.0，2026-02-18 发布）。
    /// </summary>
    [RegularExpression("^v[0-9]+\\.[0-9]+$", ErrorMessage = "ApiVersion 形如 v25.0")]
    public string ApiVersion { get; set; } = "v25.0";

    /// <summary>关联的邮箱 ID（Messenger 会话归属此邮箱；未配置时回退到第一个活跃邮箱）</summary>
    public int? MailboxId { get; set; }

    /// <summary>回调路径（默认 /api/messenger，与前端路由/导航一致）</summary>
    public string CallbackPath { get; set; } = "/api/messenger";

    /// <summary>
    /// 出站文本分片长度（字符）。Graph API 文本上限多方镜像均为 2000；
    /// 5000/10000 的说法无可信来源，不依赖。
    /// 上界必须钳在 2000：配成 3000/5000 时分片仍超 Graph 上限，长回复会整体发送失败。
    /// </summary>
    [Range(200, 2000)]
    public int TextChunkChars { get; set; } = 2000;

    /// <summary>客服窗口小时数（Messenger 为 24 小时；窗口外主动回复需 message_tag）</summary>
    [Range(1, 168)]
    public int WindowHours { get; set; } = 24;

    /// <summary>窗口剩余小于该小时数时状态报 warning</summary>
    [Range(0, 24)]
    public int WarningThresholdHours { get; set; } = 4;

    /// <summary>
    /// 窗口外主动回复策略：auto(带 message_tag，默认) / none(拒绝发送，提示坐席) / always(窗口内也带 tag)。
    /// 客服场景使用的 tag 由 MessageTag 指定（HUMAN_AGENT）。
    /// </summary>
    [RegularExpression("^(auto|none|always)$", ErrorMessage = "MessageTagPolicy 取值 auto/none/always")]
    public string MessageTagPolicy { get; set; } = "auto";

    /// <summary>窗口外主动回复使用的 message_tag 值（客服场景固定 HUMAN_AGENT）</summary>
    [MaxLength(32)]
    public string MessageTag { get; set; } = "HUMAN_AGENT";

    /// <summary>坐席回复前是否发送 sender_action=typing_on</summary>
    public bool TypingIndicator { get; set; } = true;

    /// <summary>入站消息处理后是否发送 sender_action=mark_seen</summary>
    public bool MarkSeen { get; set; } = true;

    /// <summary>是否启用客户信息收集（从对话文本提取姓名/手机/邮箱；PSID 不是手机号，故不写手机号）</summary>
    public bool ContactCollectionEnabled { get; set; } = true;

    /// <summary>是否启用自动回复（新会话欢迎语）</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>自动回复内容</summary>
    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    /// <summary>是否启用 Messenger 接入</summary>
    public bool Enabled { get; set; }

    /// <summary>订阅字段清单（仅用于状态页展示与「订阅字段」动作，不自动订阅）</summary>
    [MaxLength(512)]
    public string SubscribedFields { get; set; } =
        "messages,messaging_postbacks,messaging_optins,messaging_handovers,message_deliveries,message_reads";

    /// <summary>入站媒体下载/转存大小上限（MB），超限拒绝落盘</summary>
    [Range(1, 100)]
    public int MediaDownloadMaxMB { get; set; } = 25;

    /// <summary>API 可达性检测超时（秒），避免网络波动误判、阻塞页面加载</summary>
    public int StatusCheckTimeoutSeconds { get; set; } = 8;

    /// <summary>Graph 错误 613（限流）时的重试次数上限，每次间隔递增（按 fbtrace 便于报障）</summary>
    [Range(0, 5)]
    public int RateLimitBackoffTries { get; set; } = 2;

    /// <summary>判断是否已配置核心凭证</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PageId)
        && !string.IsNullOrWhiteSpace(PageAccessToken)
        && !string.IsNullOrWhiteSpace(AppSecret)
        && !string.IsNullOrWhiteSpace(VerifyToken);
}
