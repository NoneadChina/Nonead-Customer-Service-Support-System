namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>Messenger 配置响应（脱敏，密钥只回报是否已配置）</summary>
public class MessengerSettingsDto
{
    public string? AppId { get; set; }
    public string? PageId { get; set; }
    public string? PageUsername { get; set; }
    public string? ApiVersion { get; set; }
    public int? MailboxId { get; set; }
    public int TextChunkChars { get; set; }
    public int WindowHours { get; set; }
    public string MessageTagPolicy { get; set; } = "auto";
    public string MessageTag { get; set; } = "HUMAN_AGENT";
    public bool TypingIndicator { get; set; }
    public bool MarkSeen { get; set; }
    public bool ContactCollectionEnabled { get; set; }
    public bool AutoReplyEnabled { get; set; }
    public string? AutoReplyMessage { get; set; }
    public bool Enabled { get; set; }
    public string? SubscribedFields { get; set; }
    public string? CallbackUrl { get; set; }

    /// <summary>是否已配置 PageAccessToken（不回显原值）</summary>
    public bool HasPageAccessToken { get; set; }

    /// <summary>是否已配置 AppSecret（不回显原值）</summary>
    public bool HasAppSecret { get; set; }

    /// <summary>是否已配置 VerifyToken（不回显原值）</summary>
    public bool HasVerifyToken { get; set; }
}

/// <summary>Messenger 连接状态 DTO</summary>
public class MessengerStatusDto
{
    public bool Configured { get; set; }
    public bool Enabled { get; set; }
    public bool ApiReachable { get; set; }
    public string? CallbackUrl { get; set; }
    public string? PageId { get; set; }
    public string? ApiVersion { get; set; }

    /// <summary>窗口状态：ok(剩余>阈值) / warning(剩余≤阈值) / expired(已过期) / unknown(无会话)</summary>
    public string WindowStatus { get; set; } = "unknown";

    /// <summary>窗口剩余小时数（expired 时为 0）</summary>
    public int WindowRemainingHours { get; set; }

    /// <summary>最近一条客户消息时间</summary>
    public DateTime? LastCustomerMessageAt { get; set; }

    /// <summary>Page Access Token 最近一次确认失效时间（Graph code 190），null 表示未观察到失效</summary>
    public DateTime? TokenInvalidAt { get; set; }

    /// <summary>最近一次 Graph 错误（便于坐席/运维直接在状态页看到 code 与 fbtrace_id）</summary>
    public MessengerApiError? LastError { get; set; }
}

/// <summary>Graph API 错误（错误恒为 HTTP 400 + {"error":{...}}；fbtrace_id 需留存便于报障）</summary>
public class MessengerApiError
{
    /// <summary>Graph 错误码：551 不可投递 / 613 限流 / 190 令牌失效 / 10 权限或参数 / 2635 PSID 与 Page 不匹配</summary>
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>Graph 错误子码（可为 0）</summary>
    public int Subcode { get; set; }

    /// <summary>fbtrace_id，报障用</summary>
    public string? FbtraceId { get; set; }

    /// <summary>HTTP 状态码</summary>
    public int HttpStatus { get; set; }

    /// <summary>该错误是否应当停止自动重试（551 收件人不可达、190 令牌失效、2635 不匹配）</summary>
    public bool ShouldStopRetrying => Code is 551 or 190 or 2635;

    /// <summary>是否为限流错误（613，需退避）</summary>
    public bool IsRateLimited => Code == 613;
}

/// <summary>Messenger Webhook 入站事件解析结果</summary>
public class MessengerWebhookMessage
{
    /// <summary>幂等键（message.mid，跨重试与 echo 稳定）；postback 无 mid 时用合成键</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>发送者 PSID（sender.id，Page 作用域，非真实 FB UID）</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Page ID（entry.id / recipient.id）</summary>
    public string? PageId { get; set; }

    /// <summary>事件类型：text / attachment / postback / quick_reply / location / fallback / optin</summary>
    public string MessageType { get; set; } = "unknown";

    /// <summary>文本内容</summary>
    public string? TextBody { get; set; }

    /// <summary>附件类型：image / audio / video / file / location / fallback</summary>
    public string? AttachmentType { get; set; }

    /// <summary>附件签名 CDN URL（短 TTL，收到即下载转存）</summary>
    public string? MediaUrl { get; set; }

    /// <summary>可复用附件 ID（订阅 message_echoes 后可用于按 id 复用）</summary>
    public string? AttachmentId { get; set; }

    /// <summary>附件标题 / 文件名</summary>
    public string? Title { get; set; }

    /// <summary>位置 - 纬度</summary>
    public double? Latitude { get; set; }

    /// <summary>位置 - 经度</summary>
    public double? Longitude { get; set; }

    /// <summary>postback 载荷（postback.payload）</summary>
    public string? PostbackPayload { get; set; }

    /// <summary>postback / fallback 标题</summary>
    public string? PostbackTitle { get; set; }

    /// <summary>quick_reply 载荷</summary>
    public string? QuickReplyPayload { get; set; }

    /// <summary>optin.ref（插件/推荐授权）</summary>
    public string? OptinRef { get; set; }

    /// <summary>事件时间戳（Unix 毫秒，已归一化为 Unix 秒供落库）</summary>
    public long Timestamp { get; set; }

    /// <summary>Unix 毫秒原始时间戳（Graph messaging[].timestamp）</summary>
    public long TimestampMs { get; set; }

    /// <summary>message.is_echo（解析器已过滤，保留字段供测试与诊断）</summary>
    public bool IsEcho { get; set; }

    /// <summary>message.seq（同一会话内递增，用于乱序判定）</summary>
    public long? Seq { get; set; }
}

/// <summary>Messenger 投递/已读回执（delivery / read 事件）</summary>
public class MessengerReceiptUpdate
{
    /// <summary>ops[].mid：出站或入站消息 mid</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>回执类型：delivered / read / seen</summary>
    public string Status { get; set; } = string.Empty;

    public long Timestamp { get; set; }

    /// <summary>watermark（Unix 毫秒）：该时刻之前的消息均已送达/已读</summary>
    public long? WatermarkMs { get; set; }
}

/// <summary>Messenger 出站附件描述</summary>
public class MessengerAttachmentSpec
{
    /// <summary>url = 外链直发（payload.is_public=true）；id = attachment_id（Attachment Upload API）</summary>
    public string Kind { get; set; } = "url";

    /// <summary>image / audio / video / file</summary>
    public string Type { get; set; } = "file";

    /// <summary>Kind=url 时为可公开访问的 URL；Kind=id 时为 attachment_id</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>是否可复用（Kind=url 时映射到 payload.is_public）</summary>
    public bool IsPublic { get; set; } = true;
}

/// <summary>PSID 客户资料（GET /{ver}/me/{PSID}）</summary>
public class MessengerCustomerProfile
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePic { get; set; }
    public string? Locale { get; set; }
    public string? Gender { get; set; }
    /// <summary>时区偏移（小时），Graph 返回 number</summary>
    public double? TimezoneOffset { get; set; }
}

/// <summary>Messenger 接入处理结果</summary>
public class MessengerIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public int IgnoredEchoCount { get; set; }
    public int ReceiptCount { get; set; }
    public bool IsDuplicate { get; set; }
    public int? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>自动回复已发送的内容（新会话且启用自动回复时非空）</summary>
    public string? AutoReplyContent { get; set; }
}
