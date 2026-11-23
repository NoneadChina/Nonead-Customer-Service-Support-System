namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// iMessage 配置回显 DTO — 密钥字段一律脱敏，只回报是否已配置（Has* 布尔）。
/// </summary>
public class IMessageSettingsDto
{
    /// <summary>bridge / amb_msp / amb_apple_direct</summary>
    public string Mode { get; set; } = "bridge";

    /// <summary>当前模式是否可用（amb_apple_direct 需显式实验开关）</summary>
    public bool ModeActive { get; set; }

    /// <summary>amb_apple_direct 的实验开关状态</summary>
    public bool ExperimentalAppleDirect { get; set; }

    public bool Enabled { get; set; }
    public bool Configured { get; set; }

    // bridge
    public string? BaseUrl { get; set; }
    public string AuthStyle { get; set; } = "bearer";
    public bool HasPassword { get; set; }
    /// <summary>bridge webhook 是否已配置共享密钥（为空时入站是放行状态，仅打一次性告警）。</summary>
    public bool HasBridgeWebhookSecret { get; set; }
    public string WebhookPath { get; set; } = "/api/imessage/bluebubbles/webhook";
    public string? WebhookBaseUrl { get; set; }
    public string SendMethod { get; set; } = "apple-script";
    public bool AllowSmsFallback { get; set; }
    public string? AllowList { get; set; }
    public int RateLimitPerMinute { get; set; }
    public bool VerifyTls { get; set; } = true;
    public string HealthEndpoint { get; set; } = "/api/v1/ping";
    public string MinServerVersion { get; set; } = "1.0.0";
    public bool ReadHistoryOnStart { get; set; }

    // 会话/邮箱/自动回复
    public int MatchWindowHours { get; set; }
    public int MailboxId { get; set; }
    public bool AutoReplyEnabled { get; set; }
    public string? AutoReplyMessage { get; set; }

    /// <summary>本系统对外 webhook 完整地址（App:BaseUrl + WebhookPath）</summary>
    public string? CallbackUrl { get; set; }

    // amb_msp
    public string? MspConnector { get; set; }
    public string? ApiBaseUrl { get; set; }
    public bool HasApiKey { get; set; }
    public bool HasWebhookSecret { get; set; }
    public string? BusinessIdentifier { get; set; }
    public string FallbackChannel { get; set; } = "none";

    // amb_apple_direct
    public string? MspId { get; set; }
    public bool HasMspSharedSecret { get; set; }
    public int TokenTtlSeconds { get; set; } = 3600;
    public string GatewayBaseUrl { get; set; } = "https://mspgw.push.apple.com";
    public string? BusinessId { get; set; }
    public string InboundPath { get; set; } = "/message";
    public int AttachmentKeySize { get; set; } = 256;
    public int MaxBodyBytes { get; set; } = 2 * 1024 * 1024;
}

/// <summary>入站消息（BlueBubbles new-message 或 AMB message 归一化后的结果）。</summary>
public class IMessageWebhookMessage
{
    /// <summary>平台消息 id：bridge=data.guid（幂等键）；amb=Id 头 / body.id</summary>
    public string MessageGuid { get; set; } = string.Empty;

    /// <summary>bridge：chatGuid（保留服务前缀，回复时使用）；amb：null</summary>
    public string? ChatGuid { get; set; }

    /// <summary>chatIdentifier（1:1 为 handle，群为 chat… 标识）</summary>
    public string? ChatIdentifier { get; set; }

    /// <summary>handle.address — 视为不可信展示数据，仅用于兜底</summary>
    public string? HandleAddress { get; set; }

    /// <summary>iMessage / SMS / RCS / any / unknown（由 chatGuid 前缀得出）</summary>
    public string Service { get; set; } = "unknown";

    /// <summary>是否群聊</summary>
    public bool IsGroup { get; set; }

    /// <summary>归一化后的客户外部 id（私聊=裸 handle，群聊=GUID）</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>文本正文（AMB 的 tapback 以纯文本送达，如 Liked "Thank you." ⇒ 原样渲染）</summary>
    public string? TextBody { get; set; }

    /// <summary>text / attachment / reaction / tapback / unknown</summary>
    public string MessageType { get; set; } = "text";

    /// <summary>附件引用列表</summary>
    public List<IMessageAttachmentRef> Attachments { get; set; } = [];

    /// <summary>消息创建时间（Unix 毫秒；解析失败为 0）</summary>
    public long DateCreatedUnixMs { get; set; }

    /// <summary>isFromMe — true 时上游应忽略（自己的回声）</summary>
    public bool IsFromMe { get; set; }

    /// <summary>显示名（群名 / 联系人显示名，可选）</summary>
    public string? DisplayName { get; set; }
}

/// <summary>入站附件引用（bridge：附件 guid；amb：mmcs-url + 签名参数）。</summary>
public class IMessageAttachmentRef
{
    public string Guid { get; set; } = string.Empty;
    public string? Filename { get; set; }
    public string? MimeType { get; set; }
    public long TotalBytes { get; set; }

    /// <summary>AMB 下载所需参数（url / signature / owner），bridge 模式为 null</summary>
    public string? DownloadUrl { get; set; }

    public string? Signature { get; set; }

    public string? Owner { get; set; }
}

/// <summary>非消息类事件归一化结果（送达/已读/输入指示/错误）。</summary>
public class IMessageEventUpdate
{
    /// <summary>事件类型（见 IMessageEventKinds）</summary>
    public string Kind { get; set; } = IMessageEventKinds.Unknown;

    public string? MessageGuid { get; set; }
    public string? ChatGuid { get; set; }
    public long? DateReadUnixMs { get; set; }
    public bool IsFromMe { get; set; }
    public bool? IsTyping { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>read-receipt 携带的一批消息 guid</summary>
    public List<string> MessageGuids { get; set; } = [];
}

/// <summary>单条 webhook 事件的解析结果。</summary>
public class IMessageParsedEvent
{
    public string Kind { get; set; } = IMessageEventKinds.Unknown;
    public IMessageWebhookMessage? Message { get; set; }
    public IMessageEventUpdate? Update { get; set; }

    /// <summary>解析期告警（可记日志，不含密钥）</summary>
    public string? Notice { get; set; }
}

/// <summary>iMessage 入站处理结果。</summary>
public class IMessageIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDuplicate { get; set; }

    /// <summary>本条报文只含状态/事件类更新（非客户消息）</summary>
    public bool IsStatus { get; set; }

    public int? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AutoReplyContent { get; set; }

    /// <summary>被主动忽略的条数（isFromMe=true、不在白名单、未知事件）</summary>
    public int IgnoredCount { get; set; }
}

/// <summary>事件类型常量（BlueBubbles webhook 事件名）。</summary>
public static class IMessageEventKinds
{
    public const string NewMessage = "new-message";
    public const string UpdatedMessage = "updated-message";
    public const string MessageError = "message-error";
    public const string MessageSendError = "message-send-error";
    public const string TypingIndicator = "typing-indicator";
    public const string ReadReceipt = "read-receipt";
    public const string ChatReadStatus = "chat-read-status";
    public const string GroupNameChange = "group-name-change";
    public const string ParticipantAdded = "participant-added";
    public const string ParticipantRemoved = "participant-removed";
    public const string ParticipantLeft = "participant-left";
    public const string ServerUpdate = "server-update";
    public const string UrlChange = "url-change";

    /// <summary>BlueBubbles 注册 webhook 时发送的 Hello World 测试事件</summary>
    public const string Test = "test";

    public const string Unknown = "unknown";
}
