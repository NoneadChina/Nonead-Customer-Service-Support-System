namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// Telegram Bot API 通用出站结果（对应 {"ok":false,"error_code":..,"description":"..","parameters":{..}}）。
/// 调用方据此区分：限流重试（RetryAfterSeconds）、终态不可达（Blocked）、需要改写 chat_id（MigrateToChatId）。
/// </summary>
public class TelegramSendResult
{
    /// <summary>是否发送成功</summary>
    public bool Success { get; set; }

    /// <summary>成功时 Telegram 返回的 message_id（用于与站内线程建立映射）</summary>
    public string? MessageId { get; set; }

    /// <summary>Telegram error_code（400/401/403/404/409/420/429/5xx）；网络异常时为 0</summary>
    public int ErrorCode { get; set; }

    /// <summary>Telegram description 原文（不含 token）</summary>
    public string? Description { get; set; }

    /// <summary>429/420 限流时 parameters.retry_after 秒数</summary>
    public int? RetryAfterSeconds { get; set; }

    /// <summary>403：客户已拉黑 bot / bot 已被移出群 ⇒ 终态，不应重试</summary>
    public bool Blocked { get; set; }

    /// <summary>400 Chat not found / bot can't initiate conversation with a user ⇒ 该 chat 不可主动投递</summary>
    public bool Unreachable { get; set; }

    /// <summary>parameters.migrate_to_chat_id：超级群迁移后的新 chat_id</summary>
    public long? MigrateToChatId { get; set; }

    /// <summary>409：webhook 与 getUpdates 冲突</summary>
    public bool Conflict { get; set; }

    /// <summary>401：token 无效（硬失败，需要在配置页告警）</summary>
    public bool InvalidToken { get; set; }

    /// <summary>人类可读错误说明（Telegram description 或本地错误，不含 token）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>可重试错误（429/420/5xx/网络异常）</summary>
    public bool Retryable => RetryAfterSeconds.HasValue || ErrorCode >= 500 || (ErrorCode == 0 && !Success);
}

/// <summary>Telegram 配置响应（脱敏：不返回 BotToken / WebhookSecretToken / LinkTokenHmacKey 原值）</summary>
public class TelegramSettingsDto
{
    public bool Enabled { get; set; }

    /// <summary>入站模式：webhook | polling</summary>
    public string Mode { get; set; } = "webhook";

    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    public bool UseLocalBotApiServer { get; set; }

    /// <summary>对外回调地址（供运维填入 setWebhook）</summary>
    public string? CallbackUrl { get; set; }

    public string? BotUsername { get; set; }

    public bool AcceptGroupMessages { get; set; }

    public bool DeleteWebhookOnSwitch { get; set; }

    public int MaxWebhookConnections { get; set; }

    public int PollingTimeout { get; set; }

    public int PollingLimit { get; set; }

    public string[] AllowedUpdates { get; set; } = [];

    public int? MailboxId { get; set; }

    public bool AutoReplyEnabled { get; set; }

    public string? AutoReplyMessage { get; set; }

    public int ConversationMatchWindowHours { get; set; }

    public int MaxMessageChars { get; set; }

    public int RateLimitGlobalPerSeconds { get; set; }

    public int RateLimitPerChatPerSeconds { get; set; }

    /// <summary>t.me 深链前缀（BotUsername 存在时给出，用于账号绑定引导）</summary>
    public string? DeepLinkPrefix { get; set; }

    /// <summary>是否已配置 BotToken（不返回原值）</summary>
    public bool HasBotToken { get; set; }

    /// <summary>是否已配置 Webhook 密钥（不返回原值）</summary>
    public bool HasWebhookSecretToken { get; set; }

    /// <summary>是否已配置深链绑定 HMAC 密钥（不返回原值）</summary>
    public bool HasLinkTokenHmacKey { get; set; }
}

/// <summary>Telegram 连接/入站状态</summary>
public class TelegramStatusDto
{
    public bool Configured { get; set; }

    public bool Enabled { get; set; }

    /// <summary>入站模式：webhook | polling</summary>
    public string Mode { get; set; } = "webhook";

    /// <summary>getMe 是否成功（token 有效且网络可达）</summary>
    public bool ApiReachable { get; set; }

    /// <summary>getMe 返回的 bot 用户名</summary>
    public string? BotUsername { get; set; }

    /// <summary>bot 是否已开启群消息读取（false = 处于隐私模式，群内只收到命令/@）</summary>
    public bool? CanReadAllGroupMessages { get; set; }

    /// <summary>Webhook 是否已登记（getWebhookInfo.url 非空）</summary>
    public bool WebhookRegistered { get; set; }

    /// <summary>Telegram 侧当前登记的 webhook url</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Telegram 待投递的 update 数（长期 &gt; 0 说明回调异常）</summary>
    public int? PendingUpdateCount { get; set; }

    /// <summary>Telegram 记录的最近一次投递错误</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>最近一次投递错误时间（Unix 秒）</summary>
    public long? LastErrorTime { get; set; }

    /// <summary>轮询后台服务是否在运行</summary>
    public bool PollingRunning { get; set; }

    /// <summary>轮询最近一次活动时间（UTC）</summary>
    public DateTime? PollingLastActivityAt { get; set; }

    /// <summary>轮询最近一次错误（409 冲突 / 429 限流 / 网络异常）</summary>
    public string? PollingLastErrorMessage { get; set; }

    /// <summary>当前持久化的轮询游标（update_id + 1）</summary>
    public long? PollingOffset { get; set; }

    /// <summary>被标记为不可达（拉黑/退出群）的 Telegram 会话数</summary>
    public int UnreachableConversationCount { get; set; }

    /// <summary>对外回调地址</summary>
    public string? CallbackUrl { get; set; }
}

/// <summary>
/// Telegram Update 解析后的入站消息（Update 至多含一个内容字段；chat.id + message_id 才是稳定去重键）。
/// </summary>
public class TelegramWebhookMessage
{
    /// <summary>Update.update_id（仅用于排序与轮询游标，约一周静默后会重置，不能作去重键）</summary>
    public long UpdateId { get; set; }

    /// <summary>去重键：{chat_id}:{message_id}（写入 ConversationThread.MessageId）</summary>
    public string DedupeKey { get; set; } = string.Empty;

    /// <summary>chat.id（私聊等于 from.id；群/频道为负数，超级群以 -100 开头）</summary>
    public long ChatId { get; set; }

    /// <summary>chat.type：private | group | supergroup | channel</summary>
    public string ChatType { get; set; } = string.Empty;

    /// <summary>chat.title（群/频道名）</summary>
    public string? ChatTitle { get; set; }

    /// <summary>Message.message_id</summary>
    public long MessageId { get; set; }

    /// <summary>from.id（64 位客户稳定标识，跨 bot 一致）；无 from 时回退为 chat.id</summary>
    public long FromId { get; set; }

    /// <summary>from.is_bot（true = 其他 bot 的消息，忽略）</summary>
    public bool IsBotSender { get; set; }

    /// <summary>from.username（可变更，仅展示）</summary>
    public string? FromUsername { get; set; }

    /// <summary>展示名：first_name + last_name（回退 username / chat.title）</summary>
    public string? DisplayName { get; set; }

    /// <summary>from.language_code（如 zh-hans）</summary>
    public string? LanguageCode { get; set; }

    /// <summary>消息类型：text | photo | document | audio | voice | video | video_note | animation | sticker | contact | location | poll | unknown</summary>
    public string MessageType { get; set; } = "unknown";

    /// <summary>Update 内容字段名：message | edited_message | channel_post | callback_query …</summary>
    public string UpdateKind { get; set; } = "message";

    /// <summary>文本内容（text 类型）</summary>
    public string? TextBody { get; set; }

    /// <summary>媒体说明文字（caption，官方上限 1024）</summary>
    public string? Caption { get; set; }

    /// <summary>媒体 file_id（可复用、bot 私有、不过期）</summary>
    public string? FileId { get; set; }

    /// <summary>媒体 file_unique_id（跨 bot 稳定，配合 file_id 避免重复下载）</summary>
    public string? FileUniqueId { get; set; }

    /// <summary>媒体 MIME 类型（由 Telegram 类型/文件名推断）</summary>
    public string? MimeType { get; set; }

    /// <summary>文件名（document 的 file_name，否则由类型推断）</summary>
    public string? Filename { get; set; }

    /// <summary>媒体字节大小（已知时）</summary>
    public long? FileSize { get; set; }

    /// <summary>位置 - 纬度</summary>
    public double? Latitude { get; set; }

    /// <summary>位置 - 经度</summary>
    public double? Longitude { get; set; }

    /// <summary>话题/主题 ID（超级群多话题时用于会话键）</summary>
    public int? MessageThreadId { get; set; }

    /// <summary>被回复消息的 message_id（出站 reply_parameters 用）</summary>
    public long? ReplyToMessageId { get; set; }

    /// <summary>是否为群/频道消息（chat.id 为负）</summary>
    public bool IsGroupMessage { get; set; }

    /// <summary>是否为 /start 命令消息（含深链参数时 StartParameter 非空）</summary>
    public string? StartParameter { get; set; }

    /// <summary>服务消息 migrate_to_chat_id：超级群迁移后的新 chat.id（需改写会话绑定的 ChannelId）</summary>
    public long? MigrateToChatId { get; set; }

    /// <summary>callback_query.id（必须在 10 秒内 answerCallbackQuery）</summary>
    public string? CallbackQueryId { get; set; }

    /// <summary>callback_query.data</summary>
    public string? CallbackData { get; set; }

    /// <summary>Message.date（Unix 秒）</summary>
    public long Timestamp { get; set; }

    /// <summary>edited_message 的编辑时间（Unix 秒，>0 表示这是编辑事件）</summary>
    public long? EditDate { get; set; }
}

/// <summary>Telegram 入站接入结果</summary>
public class TelegramIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDuplicate { get; set; }

    /// <summary>非消息类 Update（callback_query / my_chat_member 等）被识别但未建线程</summary>
    public bool IsNonMessage { get; set; }

    public int? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>自动回复内容（新会话且启用自动回复时非空）</summary>
    public string? AutoReplyContent { get; set; }
}

/// <summary>getUpdates 一页结果（已解析为入站消息列表）</summary>
public class TelegramUpdatesPage
{
    /// <summary>Telegram 返回 ok=true</summary>
    public bool Ok { get; set; }

    /// <summary>解析后的 Update 列表（含非消息类 Update，由接入层过滤）</summary>
    public List<TelegramWebhookMessage> Updates { get; set; } = [];

    /// <summary>本页最大 update_id（下一轮 offset = 该值 + 1；update_id 会在一周静默后重置，仅当游标使用）</summary>
    public long MaxUpdateId { get; set; }

    /// <summary>429/420 限流的 retry_after 秒数</summary>
    public int? RetryAfterSeconds { get; set; }

    /// <summary>409：已登记 webhook，必须先 deleteWebhook 才能轮询</summary>
    public bool WebhookConflict { get; set; }

    /// <summary>401：token 无效</summary>
    public bool InvalidToken { get; set; }

    /// <summary>错误描述（不含 token）</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>轮询单次拉取结果</summary>
public class TelegramPollResult
{
    public bool Ok { get; set; }
    public int ReceivedCount { get; set; }
    public int ProcessedCount { get; set; }
    public long NewOffset { get; set; }

    /// <summary>409：webhook 已登记，需先 deleteWebhook</summary>
    public bool WebhookConflict { get; set; }

    /// <summary>401：Bot Token 无效（需轮换凭据，不应继续轮询）</summary>
    public bool InvalidToken { get; set; }

    public int? RetryAfterSeconds { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>回复结果（与 WhatsApp 回复结果同构）</summary>
public class TelegramReplyResult
{
    public bool Sent { get; set; }
    public bool ThreadCreated { get; set; } = true;
    public int? ThreadId { get; set; }

    /// <summary>Telegram 返回的 message_id（首个成功出站消息）</summary>
    public string? MessageId { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>会话被标记为不可达（403 拉黑 / bot 不在群内）</summary>
    public bool MarkedUnreachable { get; set; }

    /// <summary>429 限流：建议的重试等待秒数（交由队列延后重试）</summary>
    public int? RetryAfterSeconds { get; set; }

    /// <summary>长文本被自动分片的段数</summary>
    public int PartsSent { get; set; }
}

/// <summary>测试连接结果</summary>
public class TelegramTestResult
{
    public bool Success { get; set; }

    /// <summary>getMe 返回的 bot id</summary>
    public long? BotId { get; set; }

    public string? BotUsername { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>getMe 结果</summary>
public class TelegramBotInfo
{
    public long Id { get; set; }
    public bool IsBot { get; set; }
    public string? FirstName { get; set; }
    public string? Username { get; set; }
    public bool CanJoinGroups { get; set; }
    public bool CanReadAllGroupMessages { get; set; }
    public bool SupportsInlineQueries { get; set; }
}

/// <summary>getWebhookInfo 结果</summary>
public class TelegramWebhookInfo
{
    public string? Url { get; set; }
    public bool HasCustomCertificate { get; set; }
    public int PendingUpdateCount { get; set; }
    public string? LastErrorMessage { get; set; }
    public long? LastErrorTime { get; set; }
    public int? MaxConnections { get; set; }
    public string[]? AllowedUpdates { get; set; }
}

/// <summary>getFile 结果（file_path 需拼 /file/bot&lt;token&gt;/&lt;file_path&gt; 下载；自建服务器返回本机绝对路径）</summary>
public class TelegramFileLocation
{
    public string FileId { get; set; } = string.Empty;
    public string? FileUniqueId { get; set; }
    public long? FileSize { get; set; }
    public string FilePath { get; set; } = string.Empty;

    /// <summary>自建 Bot API 服务器（--local）返回的绝对本地路径，可直接读盘</summary>
    public bool IsLocalPath { get; set; }
}

/// <summary>流式下载到临时文件的结果状态（用状态而不是异常区分「太大」与「失败」）</summary>
public enum TelegramFileDownloadStatus
{
    /// <summary>已完整写入临时文件，LocalPath 可读</summary>
    Success = 1,

    /// <summary>超过调用方给定的 maxBytes，已放弃下载（临时文件已清理）</summary>
    TooLarge = 2,

    /// <summary>下载失败（getFile 无 file_path / HTTP 非 2xx / IO 异常），ErrorMessage 有原因</summary>
    Failed = 3
}

/// <summary>
/// 流式下载结果。整个设计目标是「绝不为超大文件分配同等大小的内存」：
/// 调用方先看 Status，再决定是否读取 LocalPath；TooLarge 时不产生任何本地文件。
/// </summary>
public class TelegramFileDownloadResult
{
    public TelegramFileDownloadStatus Status { get; set; }

    /// <summary>下载成功的临时文件绝对路径（仅 Status=Success 时有值，调用方负责移动/删除）</summary>
    public string? LocalPath { get; set; }

    /// <summary>实际已写入的字节数（TooLarge 时是失败前已知/已读到的下界）</summary>
    public long ByteCount { get; set; }

    /// <summary>响应头里的 Content-Type（可能为空）</summary>
    public string? MimeType { get; set; }

    /// <summary>失败原因（仅 Status=Failed 时有值；不含 URL/token）</summary>
    public string? ErrorMessage { get; set; }

    public static TelegramFileDownloadResult Ok(string localPath, long byteCount, string? mimeType) => new()
    {
        Status = TelegramFileDownloadStatus.Success,
        LocalPath = localPath,
        ByteCount = byteCount,
        MimeType = mimeType
    };

    public static TelegramFileDownloadResult TooLarge(long byteCount, long maxBytes) => new()
    {
        Status = TelegramFileDownloadStatus.TooLarge,
        ByteCount = byteCount,
        ErrorMessage = $"文件大小超过限制（{byteCount} > {maxBytes} 字节）"
    };

    public static TelegramFileDownloadResult Failed(string errorMessage, long byteCount = 0) => new()
    {
        Status = TelegramFileDownloadStatus.Failed,
        ByteCount = byteCount,
        ErrorMessage = errorMessage
    };
}
