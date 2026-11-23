using NoneadCSS.Application.DTOs.Responses;
namespace NoneadCSS.Application.Services;

/// <summary>
/// Telegram Bot API 出站客户端抽象（Infrastructure 层用 HttpClient 实现）。
/// 约定：
///   - Token 位于 URL 路径 /bot&lt;token&gt;/&lt;method&gt;，任何日志都不得输出 URL 或 token。
///   - 返回 <see cref="TelegramSendResult"/>，调用方据此区分限流（RetryAfterSeconds）与终态不可达（Blocked/Unreachable）。
///   - ApiBaseUrl 可指向自建 Bot API 服务器，因此不写死 https://api.telegram.org。
/// </summary>
public interface ITelegramClient
{
    /// <summary>
    /// 发送文本消息（sendMessage）。chatId 为 chat.id 字符串或 @username。
    /// messageThreadId 仅在论坛群（forum）里用于指定话题：不传则发到“General”话题，传 0 会被 Telegram 拒绝，因此本参数为 null 时不写该字段。
    /// parseMode 为 null、空串或 "none"（大小写不敏感）时不写 parse_mode 字段（纯文本）；否则原样下发 HTML / Markdown / MarkdownV2。
    /// </summary>
    Task<TelegramSendResult> SendTextMessageAsync(string chatId, string text, string? replyToMessageId = null, CancellationToken cancellationToken = default, int? messageThreadId = null, string? parseMode = null);

    /// <summary>发送媒体消息（sendPhoto/sendDocument/sendVideo/sendAudio/sendVoice/sendAnimation/sendSticker），媒体值可为 file_id 或 HTTPS URL。</summary>
    Task<TelegramSendResult> SendMediaMessageAsync(string chatId, string mediaType, string fileIdOrUrl, string? caption = null, CancellationToken cancellationToken = default);

    /// <summary>以 multipart 上传本地字节并发送媒体（自建服务器可接受本地绝对路径）。</summary>
    Task<TelegramSendResult> UploadAndSendMediaAsync(string chatId, string mediaType, byte[] data, string fileName, string mimeType, string? caption = null, CancellationToken cancellationToken = default);

    /// <summary>输入状态提示（sendChatAction，typing 最长 5 秒，需重发）。</summary>
    Task<bool> SendChatActionAsync(string chatId, string action = "typing", CancellationToken cancellationToken = default);

    /// <summary>getFile：拿到 file_path（自建 --local 服务器返回本机绝对路径）。</summary>
    Task<DTOs.Responses.TelegramFileLocation?> GetFileAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>下载 getFile 返回的文件（官方 /file/bot&lt;token&gt;/&lt;file_path&gt;；本地路径直接读盘）。</summary>
    Task<(byte[] data, string? mimeType)?> DownloadFileAsync(DTOs.Responses.TelegramFileLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式下载到临时文件（入站媒体落盘专用）：
    /// 先 getFile 解析 file_path，再用 file_size / Content-Length 预检，最后带增量上限地流式写盘，
    /// 一旦超过 maxBytes 立即中止并返回 TooLarge —— 不会把超大文件整体读进内存（原来的 ReadAsByteArrayAsync 会 OOM）。
    /// 返回的 LocalPath 由调用方负责移动或删除。
    /// </summary>
    Task<DTOs.Responses.TelegramFileDownloadResult> DownloadFileToTempAsync(string fileId, long maxBytes, CancellationToken cancellationToken = default);

    /// <summary>setWebhook：登记回调地址与密钥（secret_token 由 Telegram 原样回带到 X-Telegram-Bot-Api-Secret-Token 头）。</summary>
    Task<bool> SetWebhookAsync(string url, string? secretToken, int maxConnections, IReadOnlyList<string>? allowedUpdates, bool dropPendingUpdates, string? certificate = null, string? ipAddress = null, CancellationToken cancellationToken = default);

    /// <summary>deleteWebhook：切换到轮询前必须调用，否则 getUpdates 返回 409。</summary>
    Task<bool> DeleteWebhookAsync(bool dropPendingUpdates = false, CancellationToken cancellationToken = default);

    /// <summary>getWebhookInfo：状态页展示 pending_update_count / last_error_message / last_error_time。</summary>
    Task<DTOs.Responses.TelegramWebhookInfo?> GetWebhookInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>getMe：连接性探测，同时读出 can_read_all_group_messages（群隐私模式）。</summary>
    Task<DTOs.Responses.TelegramBotInfo?> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>getUpdates：轮询模式入站（offset = 上次 update_id + 1）。</summary>
    Task<DTOs.Responses.TelegramUpdatesPage> GetUpdatesAsync(long offset, int limit, int timeout, IReadOnlyList<string>? allowedUpdates, CancellationToken cancellationToken = default);

    /// <summary>answerCallbackQuery：每个 callback_query 必须在 10 秒内应答。</summary>
    Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken cancellationToken = default);

    /// <summary>连接性探测（getMe 是否成功）。</summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
