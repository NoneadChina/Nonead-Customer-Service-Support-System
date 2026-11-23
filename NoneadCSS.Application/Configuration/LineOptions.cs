using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// LINE Messaging API 接入配置（appsettings 基线；数据库 SystemOptions 覆盖）。
/// 敏感字段（ChannelSecret / ChannelAccessToken）在数据库中加密存储，读取时由 LineSettingsProvider 解密。
/// 参考 docs/integration/line.md。
/// </summary>
public class LineOptions
{
    /// <summary>appsettings 中的配置节名</summary>
    public const string SectionName = "Line";

    /// <summary>LINE Channel ID（仅展示与核对用，不参与 API 调用；LINE 用 Bearer 令牌鉴权）</summary>
    [MaxLength(64)]
    public string? ChannelId { get; set; }

    /// <summary>Channel Secret：用于校验 Webhook 的 X-Line-Signature（HMAC-SHA256）</summary>
    [MaxLength(256)]
    public string? ChannelSecret { get; set; }

    /// <summary>Channel Access Token：所有 API 调用的 Bearer 令牌（长期令牌直接粘贴）</summary>
    [MaxLength(4096)]
    public string? ChannelAccessToken { get; set; }

    /// <summary>令牌 key_id（由 v2.1 签发接口返回，仅展示与轮换核对用）</summary>
    [MaxLength(64)]
    public string? ChannelAccessTokenKeyId { get; set; }

    /// <summary>主 API 基址（默认官方地址，私有化代理可改）</summary>
    [Url]
    [MaxLength(256)]
    public string ApiBaseUrl { get; set; } = "https://api.line.me";

    /// <summary>媒体 API 基址（入站消息内容下载）</summary>
    [Url]
    [MaxLength(256)]
    public string DataApiBaseUrl { get; set; } = "https://api-data.line.me";

    /// <summary>
    /// 出站附件公网基址。LINE 没有聊天媒体上传接口，图片/音视频/文件消息必须由本部署提供
    /// 可公网访问的 HTTPS 直链（不得重定向、不得要求鉴权），因此该基址必须是公网可达的 https 域名。
    /// 留空时由 App:BaseUrl 推导。
    /// </summary>
    [MaxLength(256)]
    public string? AttachmentPublicBaseUrl { get; set; }

    /// <summary>出站附件直链有效期（分钟）。LINE 在客户点开消息时才取文件，故默认给较长窗口。</summary>
    public int AttachmentPublicUrlTtlMinutes { get; set; } = 10080;

    /// <summary>Webhook 总开关：false 时 POST /api/line/webhook 直接 200 丢弃（不入库、不回复）</summary>
    public bool WebhookEnabled { get; set; } = true;

    /// <summary>已绑定的 Rich Menu ID（仅展示，绑定操作在 LINE 控制台完成）</summary>
    [MaxLength(64)]
    public string? RichMenuId { get; set; }

    /// <summary>是否调用 /v2/bot/chat/markAsRead（聊天启用功能，需官方审批开通；未开通时失败静默）</summary>
    public bool MarkAsReadEnabled { get; set; } = true;

    /// <summary>客服输入时是否调用 /v2/bot/chat/loading/start 显示"正在输入"</summary>
    public bool LoadingIndicatorEnabled { get; set; } = true;

    /// <summary>新会话自动回复（mode=bot 或人工接管期间不触发）</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>自动回复文案</summary>
    [MaxLength(1000)]
    public string? AutoReplyMessage { get; set; }

    /// <summary>是否尝试从 LINE 个人资料（displayName/pictureUrl）补全客户姓名与头像</summary>
    public bool ProfileEnrichEnabled { get; set; } = true;

    /// <summary>文本消息单条字符上限（LINE 上限约 5000，超出需分片发送）</summary>
    public int TextChunkChars { get; set; } = 5000;

    /// <summary>单次 messages API 请求的消息对象数量上限（LINE 上限约 5）</summary>
    public int MaxMessagesPerRequest { get; set; } = 5;

    /// <summary>replyToken 视为可用的存活秒数。官方口径 replyToken 一次性且存活极短（≤30s），保守取 25s。</summary>
    public int ReplyTokenTtlSeconds { get; set; } = 25;

    /// <summary>人工接管时长（分钟）：客服回复后该时间内机器人自动回复静默</summary>
    public int HumanHandoverMuteMinutes { get; set; } = 60;

    /// <summary>月度配额使用率达到该百分比时在状态页告警</summary>
    public int QuotaWarnPercent { get; set; } = 80;

    /// <summary>状态页 API 可达性检测超时（秒）</summary>
    public int StatusCheckTimeoutSeconds { get; set; } = 8;

    /// <summary>可重试错误（500/503/429）的额外重试次数（0 表示不重试）</summary>
    public int RetryMaxAttempts { get; set; } = 1;

    /// <summary>入站媒体转存体积上限（字节），超出仅登记占位说明，避免无限制占用存储</summary>
    public long MaxInboundMediaBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>出站附件直链允许的最大文件体积（字节），超出则跳过该附件并回报错误</summary>
    public long MaxOutboundAttachmentBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>出站图片要求 JPEG/PNG，直链文件扩展名白名单（小写、不含点）</summary>
    [MaxLength(256)]
    public string OutboundImageExtensions { get; set; } = "jpg,jpeg,png";

    /// <summary>关联邮箱（MailboxId）解析失败时的兜底，语义与其它 IM 渠道一致</summary>
    public int? MailboxId { get; set; }

    /// <summary>渠道总开关</summary>
    public bool Enabled { get; set; }

    /// <summary>Webhook 回调路径（用于状态页展示回调地址）</summary>
    public string WebhookPath { get; set; } = "/api/line/webhook";

    /// <summary>必填项是否齐备：ChannelSecret 用于验签，ChannelAccessToken 用于调用</summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(ChannelSecret)
        && !string.IsNullOrWhiteSpace(ChannelAccessToken);
}
