namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>LINE 设置回显（敏感字段只回 Has* 布尔，不回明文）</summary>
public class LineSettingsDto
{
    public string? ChannelId { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? DataApiBaseUrl { get; set; }
    public string? AttachmentPublicBaseUrl { get; set; }
    public int AttachmentPublicUrlTtlMinutes { get; set; }
    public string? RichMenuId { get; set; }
    public bool WebhookEnabled { get; set; }
    public bool MarkAsReadEnabled { get; set; }
    public bool LoadingIndicatorEnabled { get; set; }
    public bool AutoReplyEnabled { get; set; }
    public string? AutoReplyMessage { get; set; }
    public bool ProfileEnrichEnabled { get; set; }
    public int TextChunkChars { get; set; }
    public int MaxMessagesPerRequest { get; set; }
    public int ReplyTokenTtlSeconds { get; set; }
    public int HumanHandoverMuteMinutes { get; set; }
    public int QuotaWarnPercent { get; set; }

    // 审计 #12：以下四项此前只存在于 LineOptions，DTO 没暴露 ⇒ 界面读不到也改不了。
    // 类型与 LineOptions 保持一致（字节数用 long，可能超过 int 范围）。
    /// <summary>状态检测超时（秒）</summary>
    public int StatusCheckTimeoutSeconds { get; set; }

    /// <summary>429/5xx 的额外重试次数（0 = 关闭重试）</summary>
    public int RetryMaxAttempts { get; set; }

    /// <summary>入站附件下载上限（字节）</summary>
    public long MaxInboundMediaBytes { get; set; }

    /// <summary>出站附件直链上限（字节）</summary>
    public long MaxOutboundAttachmentBytes { get; set; }

    /// <summary>按 image 消息发送的扩展名白名单（逗号分隔、小写）</summary>
    public string? OutboundImageExtensions { get; set; }

    public int? MailboxId { get; set; }
    public bool Enabled { get; set; }

    /// <summary>Webhook 回调地址（供 LINE 控制台填写）</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>附件公网直链基址（未显式配置时回落到 App:BaseUrl）</summary>
    public bool HasChannelAccessToken { get; set; }
    public bool HasChannelSecret { get; set; }

    /// <summary>已解析出的访问令牌 key_id（仅展示）</summary>
    public string? ChannelAccessTokenKeyId { get; set; }
}

/// <summary>LINE 运行状态</summary>
public class LineStatusDto
{
    public bool Configured { get; set; }
    public bool Enabled { get; set; }
    public bool WebhookEnabled { get; set; }

    /// <summary>Message API 是否可达（调用 quota 接口作为轻量探活）</summary>
    public bool ApiReachable { get; set; }

    /// <summary>访问令牌是否有效（/v2/oauth/verify）</summary>
    public bool TokenValid { get; set; }

    /// <summary>令牌 scope（verify 返回，例如 "profile message"）</summary>
    public string? TokenScope { get; set; }

    /// <summary>令牌到期时间（长期令牌 verify 不返回过期 ⇒ null）</summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>令牌已确认失效的时间（401），null 表示未观察到失效</summary>
    public DateTime? TokenInvalidAt { get; set; }

    public string? WebhookUrl { get; set; }

    /// <summary>LINE 控制台当前登记的回调地址（读取 Webhook 端点接口）</summary>
    public string? WebhookEndpointOnLine { get; set; }

    /// <summary>LINE 侧 Webhook 是否已启用</summary>
    public bool? WebhookDeliveryOnLine { get; set; }

    /// <summary>月度推送配额（-1 表示未知；unlimited 计划返回 int.MaxValue）</summary>
    public int QuotaTotal { get; set; } = -1;

    /// <summary>本月已消耗配额（reply 不计入消耗）</summary>
    public int QuotaConsumed { get; set; } = -1;

    /// <summary>配额类型：unknown / none / limited / unlimited</summary>
    public string QuotaType { get; set; } = "unknown";

    /// <summary>配额使用率百分比（无配额信息时为 0）</summary>
    public int QuotaUsedPercent { get; set; }

    /// <summary>配额是否已进入告警阈值</summary>
    public bool QuotaWarning { get; set; }

    /// <summary>最近一次 LINE API 错误</summary>
    /// <remarks>
    /// 审计 #11：由 LineService 在出站失败点落 SystemOptions（line.last_error_http_status /
    /// line.last_error_message），GetStatusAsync 回读。语义是"最近一次真实失败"（与 Messenger 同口径）：
    /// 之后的成功不清除它，所以它是历史失败记录，不是"当前仍在失败"的断言 —— 是否已恢复看 TokenValid / 配额卡。
    /// </remarks>
    public LineApiError? LastError { get; set; }

    /// <summary>
    /// 当前会话可用的回复方式：reply / push / unknown（审计 #11）。
    /// 这里是机器可读代码，不是给用户看的文案 —— 服务端不产文案，否则中英双语只能二选一
    /// （与 QuotaType 归一化同一口径）。前端按代码取 locale key（channels.line.status.replyMode*）。
    /// </summary>
    public string ReplyModeHint { get; set; } = "unknown";
}

/// <summary>LINE API 错误（错误体恒为 {"message":"...","details":[{"message":"...","property":"..."}]}）</summary>
public class LineApiError
{
    public int HttpStatus { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Details { get; set; } = [];

    /// <summary>401：令牌失效，应报警并停止重试</summary>
    public bool IsTokenInvalid => HttpStatus == 401;

    /// <summary>404：目标不是机器人好友（或 userId 无效）⇒ 会话不可达</summary>
    public bool IsNotFriended => HttpStatus == 404;

    /// <summary>403：配额耗尽或权限不足 ⇒ 会话不可达 / 需升级套餐</summary>
    public bool IsQuotaOrPermission => HttpStatus == 403;

    /// <summary>409：重复请求（同一 replyToken 已被使用）</summary>
    public bool IsDuplicate => HttpStatus == 409;

    /// <summary>429：限流，应退避重试</summary>
    public bool IsRateLimited => HttpStatus == 429;

    /// <summary>500/503：服务端临时故障，可重试</summary>
    public bool IsRetryable => HttpStatus is 429 or 500 or 503;
}

/// <summary>LINE 消息对象（text / image / video / audio / file / location）</summary>
public class LineMessageObject
{
    /// <summary>type：text / image / video / audio / file / location</summary>
    public string Type { get; set; } = "text";

    public string? Text { get; set; }

    /// <summary>被引用消息 id（客户引用回复时回传，触发 LINE 的引用展示）</summary>
    public string? QuoteMessageId { get; set; }

    /// <summary>image/video/audio/file：本部署签发的公网 HTTPS 直链</summary>
    public string? OriginalContentUrl { get; set; }

    /// <summary>image/video：缩略图直链（LINE 要求 image 必填；无缩略图时回落到原图）</summary>
    public string? PreviewImageUrl { get; set; }

    /// <summary>file：文件名</summary>
    public string? FileName { get; set; }

    /// <summary>file：文件字节数（LINE 必填）</summary>
    public long? FileSize { get; set; }

    /// <summary>video/audio：时长（毫秒）</summary>
    public int? DurationMs { get; set; }

    /// <summary>location：标题</summary>
    public string? Title { get; set; }

    public string? Address { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
}

/// <summary>发送结果（LINE 的 reply/push 成功响应体为空对象，无外部消息 id）</summary>
public class LineSendResult
{
    public bool Success { get; set; }

    /// <summary>实际使用的发送方式：reply / push（replyToken 缺失或过期时回落 push）</summary>
    public string Via { get; set; } = "push";

    /// <summary>LINE 不返回消息 id，保留字段便于与后续 API 变更对齐</summary>
    public string? ExternalMessageId { get; set; }

    public int ChunkCount { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>目标不可达（404 非好友 / 403 屏蔽或配额）⇒ 上层应把会话标记为不可达</summary>
    public bool Unreachable { get; set; }

    /// <summary>失败时的结构化错误（含 HTTP 状态分类），成功为 null</summary>
    public LineApiError? Error { get; set; }

    /// <summary>
    /// 实际发起的 HTTP 尝试次数（含首次）。用于区分「首次就 409」与「429/5xx 重试后才 409」：
    /// 后者说明第一次请求可能已被 LINE 接受（只是响应丢了），此时该 replyToken 的投递状态是
    /// 不确定的，上层不能再回落 push——否则客户会收到两条同样的消息。
    /// </summary>
    public int Attempts { get; set; } = 1;

    /// <summary>
    /// 投递状态不确定：reply 经过 429/5xx 重试后才被判为令牌问题，第一次请求可能已被 LINE 接受。
    /// 置位时上层（<c>LineService</c> 与出站队列）都不得再回落 push，否则客户会收到两条同样的消息。
    /// </summary>
    public bool DuplicateRisk { get; set; }
}

/// <summary>LINE 个人资料</summary>
public class LineProfile
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PictureUrl { get; set; }
    public string? StatusMessage { get; set; }
    public string? Language { get; set; }
}

/// <summary>入站事件解析结果（消息与非消息事件统一结构）</summary>
public class LineWebhookEvent
{
    /// <summary>事件类型：message / follow / unfollow / join / leave / memberJoined / memberLeft / postback / beacon / unsend / videoPlayComplete / accountLink</summary>
    public string EventType { get; set; } = "unknown";

    /// <summary>幂等键：message 用 message.id；unsend 用 unsend_{messageId}；其余事件用合成键</summary>
    public string DedupeKey { get; set; } = string.Empty;

    /// <summary>LINE 的 userId（source.type=user 时）；群/房间事件里可能为 null</summary>
    public string? UserId { get; set; }

    /// <summary>source.type：user / group / room</summary>
    public string SourceType { get; set; } = "user";

    /// <summary>group/room 的 groupId / roomId</summary>
    public string? ChatId { get; set; }

    /// <summary>事件模式：active（正常）/ bot（渠道受限，禁止回复）</summary>
    public string Mode { get; set; } = "active";

    /// <summary>事件时间戳（LINE 毫秒）</summary>
    public long Timestamp;

    /// <summary>replyToken（message/follow/postback 等事件携带）</summary>
    public string? ReplyToken { get; set; }

    /// <summary>聊天启用功能的已读令牌</summary>
    public string? MarkAsReadToken { get; set; }

    /// <summary>消息类型：text / image / video / audio / file / location / sticker</summary>
    public string MessageType { get; set; } = "unknown";

    public string? MessageId { get; set; }

    public string? TextBody { get; set; }

    /// <summary>被引用消息 id</summary>
    public string? QuoteMessageId { get; set; }

    /// <summary>入站媒体需从 api-data.line.me 下载：内容接口的消息 id</summary>
    public string? ContentMessageId { get; set; }

    /// <summary>file 消息的原始文件名</summary>
    public string? FileName { get; set; }

    /// <summary>file 消息声明的字节数</summary>
    public long? FileSize { get; set; }

    /// <summary>location 字段</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? LocationAddress { get; set; }

    /// <summary>location 消息标题</summary>
    public string? Title { get; set; }

    /// <summary>postback 的 data</summary>
    public string? PostbackData { get; set; }

    /// <summary>unsend 事件撤销的消息 id 列表</summary>
    public List<string> UnsentMessageIds { get; set; } = [];

    /// <summary>是否为需要落库的客户消息（message 事件）</summary>
    public bool IsCustomerMessage => EventType == "message";

    /// <summary>
    /// 是否为不可回复事件。
    /// LINE 的 mode 只有 "active" 与 "standby" 两种取值：standby 表示该频道处于待机状态
    /// （被停用或由别的系统代为应答），此时即使收到事件也不得回复。
    /// 历史实现按 mode == "bot" 判断，而 LINE 从不发送该值 ⇒ 待机状态下仍会自动回复，
    /// 与人工或另一个系统抢答。
    /// </summary>
    public bool IsReplyForbidden => string.Equals(Mode, "standby", StringComparison.OrdinalIgnoreCase);
}

/// <summary>LINE 入站处理结果</summary>
public class LineIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDuplicate { get; set; }
    public int? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>本次事件数（含被过滤的非消息事件）</summary>
    public int EventCount { get; set; }

    /// <summary>mode=bot 被跳过回复的事件数</summary>
    public int SkippedStandbyCount { get; set; }

    /// <summary>自动回复内容（用于测试断言）</summary>
    public string? AutoReplyContent { get; set; }
}

/// <summary>Webhook 连通性自检结果（对应 /v2/bot/channel/webhook/test）</summary>
public class LineWebhookCheckResult
{
    public bool Success { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>LINE 设置页测试结果</summary>
public class LineTestResult
{
    public bool Success { get; set; }
    public string? ExternalMessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
