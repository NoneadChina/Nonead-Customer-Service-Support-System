using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新 LINE 配置。null 表示"不修改"；敏感字段传空字符串以外的值才覆盖。
/// </summary>
public class UpdateLineSettingsRequest
{
    [MaxLength(64)]
    public string? ChannelId { get; set; }

    /// <summary>Channel Secret（敏感）</summary>
    [MaxLength(256)]
    public string? ChannelSecret { get; set; }

    /// <summary>Channel Access Token（敏感）</summary>
    [MaxLength(4096)]
    public string? ChannelAccessToken { get; set; }
    [MaxLength(256)]
    public string? ApiBaseUrl { get; set; }
    [MaxLength(256)]
    public string? DataApiBaseUrl { get; set; }

    /// <summary>出站附件公网直链基址（必须是公网可达 https）</summary>
    [MaxLength(256)]
    public string? AttachmentPublicBaseUrl { get; set; }

    [Range(5, 100800)]
    public int? AttachmentPublicUrlTtlMinutes { get; set; }

    [MaxLength(64)]
    public string? RichMenuId { get; set; }

    public bool? WebhookEnabled { get; set; }
    public bool? MarkAsReadEnabled { get; set; }
    public bool? LoadingIndicatorEnabled { get; set; }
    public bool? ProfileEnrichEnabled { get; set; }
    public bool? AutoReplyEnabled { get; set; }

    [MaxLength(1000)]
    public string? AutoReplyMessage { get; set; }

    [Range(500, 5000)]
    public int? TextChunkChars { get; set; }

    [Range(1, 5)]
    public int? MaxMessagesPerRequest { get; set; }

    [Range(1, 60)]
    public int? ReplyTokenTtlSeconds { get; set; }

    [Range(0, 1440)]
    public int? HumanHandoverMuteMinutes { get; set; }

    [Range(1, 100)]
    public int? QuotaWarnPercent { get; set; }

    /// <summary>状态检测超时（秒）：配额/令牌/回调端点三个探活调用的共同上限（审计 #12）</summary>
    [Range(1, 60)]
    public int? StatusCheckTimeoutSeconds { get; set; }

    /// <summary>429/5xx 的额外重试次数，0 = 关闭重试（与 LineClient.PostSendAsync 的 RetryMaxAttempts 同口径）</summary>
    [Range(0, 5)]
    public int? RetryMaxAttempts { get; set; }

    /// <summary>入站附件下载上限（字节）；超过则只留文本并记 warn。上限 200 MB = LINE 视频消息官方上限</summary>
    [Range(1, 209_715_200)]
    public long? MaxInboundMediaBytes { get; set; }

    /// <summary>出站附件直链上限（字节）；超过则拒绝为该附件签发公网直链</summary>
    [Range(1, 209_715_200)]
    public long? MaxOutboundAttachmentBytes { get; set; }

    /// <summary>按 image 消息发送的扩展名白名单（逗号分隔；保存时规整为小写、去点、去空项）</summary>
    [MaxLength(256)]
    public string? OutboundImageExtensions { get; set; }

    /// <summary>
    /// Channel Access Token 的 key_id（LINE 控制台签发令牌时给出的标识）。
    /// 审计 #12 要求暴露到界面：它不是密钥（不参与签名、不入库到 ENC: 通道），只用于展示与轮换核对，
    /// 因此按普通字符串读写；传空串表示清除。
    /// </summary>
    [MaxLength(64)]
    public string? ChannelAccessTokenKeyId { get; set; }

    public int? MailboxId { get; set; }

    public bool? Enabled { get; set; }
}

/// <summary>客服在会话页回复 LINE 客户</summary>
public class LineReplyRequest
{
    [Required]
    public int ConversationId { get; set; }

    /// <summary>文本内容（与 AttachmentIds 至少一个）</summary>
    [MaxLength(20000)]
    public string? Content { get; set; }

    /// <summary>已上传附件 id 列表；将签发本部署公网直链后以 image/file 消息发出</summary>
    public List<int>? AttachmentIds { get; set; }

    /// <summary>是否优先尝试 replyToken（默认 true；过期自动回落 push）</summary>
    public bool PreferReplyToken { get; set; } = true;
}

/// <summary>人工接管：机器人自动回复静默 N 分钟（LINE 无公开的会话级接管 API）</summary>
public class LineTakeoverRequest
{
    [Required]
    public int ConversationId { get; set; }

    /// <summary>接管时长（分钟），null 用配置默认值 HumanHandoverMuteMinutes</summary>
    [Range(1, 10080)]
    public int? Minutes { get; set; }
}

/// <summary>设置页主动推送测试消息（走 push，消耗配额）</summary>
public class LinePushTestRequest
{
    /// <summary>目标 LINE userId；留空则推给"最近互动的客户"（便于自检）</summary>
    [MaxLength(64)]
    public string? UserId { get; set; }

    /// <summary>会话 id；提供时从会话解析 userId，优先于 UserId</summary>
    public int? ConversationId { get; set; }

    [Required(AllowEmptyStrings = false)]
    [MaxLength(2000)]
    public string Text { get; set; } = "OpenCSS LINE 连通性测试";
}

/// <summary>Webhook 连通性自检请求（对应 LINE /v2/bot/channel/webhook/test）</summary>
public class LineWebhookCheckRequest
{
    /// <summary>要自检的回调地址；留空使用系统按 App:BaseUrl 生成的地址</summary>
    [MaxLength(256)]
    public string? WebhookUrl { get; set; }
}
