namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 微信服务号配置响应（敏感字段脱敏）
/// </summary>
public class WeChatSettingsDto
{
    public string? AppId { get; set; }
    /// <summary>AppSecret 脱敏：仅返回是否已配置</summary>
    public bool HasAppSecret { get; set; }
    /// <summary>Token 脱敏：仅返回是否已配置</summary>
    public bool HasToken { get; set; }
    /// <summary>EncodingAESKey 脱敏：仅返回是否已配置</summary>
    public bool HasEncodingAESKey { get; set; }
    public string EncryptMode { get; set; } = "plain";
    public int? MailboxId { get; set; }
    public bool ContactCollectionEnabled { get; set; }
    public bool AutoReplyEnabled { get; set; }
    public string? AutoReplyMessage { get; set; }
    public bool Enabled { get; set; }
    /// <summary>回调 URL（供微信公众平台后台配置）</summary>
    public string? CallbackUrl { get; set; }
}

/// <summary>
/// 微信消息解析结果（内部使用）
/// </summary>
public class WeChatMessageDto
{
    /// <summary>开发者微信号（服务号原始 ID）</summary>
    public string ToUserName { get; set; } = string.Empty;
    /// <summary>发送者 OpenId（用户唯一标识）</summary>
    public string FromUserName { get; set; } = string.Empty;
    /// <summary>消息创建时间（Unix 时间戳，秒）</summary>
    public long CreateTime { get; set; }
    /// <summary>消息类型：text/image/voice/video/shortvideo/location/link/event</summary>
    public string MsgType { get; set; } = string.Empty;
    /// <summary>消息内容（text 类型）</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>消息 ID（用于去重，64 位整数的字符串形式）</summary>
    public string MsgId { get; set; } = string.Empty;
    /// <summary>图片媒体 ID（image 类型）</summary>
    public string MediaId { get; set; } = string.Empty;
    /// <summary>图片格式（image 类型）</summary>
    public string PicUrl { get; set; } = string.Empty;
    /// <summary>事件类型（event 类型）：subscribe/unsubscribe/CLICK/VIEW 等</summary>
    public string Event { get; set; } = string.Empty;
    /// <summary>事件 KEY 值（菜单点击事件）</summary>
    public string EventKey { get; set; } = string.Empty;
    /// <summary>地理位置纬度（location 类型）</summary>
    public string Latitude { get; set; } = string.Empty;
    /// <summary>地理位置经度（location 类型）</summary>
    public string Longitude { get; set; } = string.Empty;
    /// <summary>地理位置精度（location 类型）</summary>
    public string Precision { get; set; } = string.Empty;
    /// <summary>链接标题（link 类型）</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>链接描述（link 类型）</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>链接 URL（link 类型）</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>消息是否为事件推送</summary>
    public bool IsEvent => string.Equals(MsgType, "event", StringComparison.OrdinalIgnoreCase);
    /// <summary>解析后的消息正文（用于存入 Thread.Body）</summary>
    public string BodyText { get; set; } = string.Empty;
}

/// <summary>
/// 微信消息处理结果
/// </summary>
public class WeChatIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsEvent { get; set; }
    public int? ConversationId { get; set; }

    /// <summary>
    /// 被动回复内容（5 秒内同步返回给微信服务器）。
    /// 用于 subscribe 事件欢迎语等场景；为空表示不被动回复（返回 success）。
    /// </summary>
    public string? PassiveReplyContent { get; set; }
}
