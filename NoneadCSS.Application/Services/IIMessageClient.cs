namespace NoneadCSS.Application.Services;

using NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// iMessage 出站传输抽象（IMessageProvider 契约的发送侧）。
/// 接口定义在 Application 层，实现按模式落在 Infrastructure 层：
///   bridge  → IMessageClient（BlueBubbles REST /api/v1，默认）
///   amb_*   → IMessageAmbClient（Apple Messages for Business；MSP 直连为实验性）
/// 两套传输共用同一身份作用域 (channel="imessage", ChannelId="&lt;mode&gt;:&lt;externalId&gt;")。
/// </summary>
public interface IIMessageClient
{
    /// <summary>
    /// 发送文本消息。
    /// </summary>
    /// <param name="destination">bridge：BlueBubbles chatGuid（保留服务前缀，如 iMessage;-;+15551234567）；amb：不透明客户 sourceId</param>
    /// <param name="body">文本内容</param>
    /// <param name="idempotencyId">自生成 id（bridge 用作 tempGuid、amb 用作 body.id/Id 头），用于对账自己的出站回声，勿按文本匹配</param>
    /// <returns>发送成功返回平台消息 id（bridge 为 rowid/guid，amb 为 Apple 返回或本地 id），失败返回 null</returns>
    Task<string?> SendTextMessageAsync(string destination, string body, string? idempotencyId = null);

    /// <summary>
    /// 发送文本消息并返回**带 HTTP 状态码的发送结果**。
    /// 出站队列据此区分"永久失败（400/403/404/410 等，重试只会无限烧配额）"与"可重试失败（5xx/429/超时/网络）"，
    /// 这是 <see cref="SendTextMessageAsync"/> 用 null 折叠所有错误所无法表达的信息。
    /// </summary>
    /// <param name="destination">同 <see cref="SendTextMessageAsync"/></param>
    /// <param name="body">文本内容</param>
    /// <param name="idempotencyId">重试时应传入上一次尝试的同一 id，Apple 侧据此幂等；为空则由实现生成</param>
    Task<IMessageSendOutcome> SendTextMessageWithOutcomeAsync(string destination, string body, string? idempotencyId = null);

    /// <summary>
    /// 发送附件（bridge：multipart 到 /api/v1/message/attachment；amb：preUpload + AES/CTR 加密后上传）。
    /// </summary>
    /// <param name="destination">同 SendTextMessageAsync</param>
    /// <param name="data">文件字节</param>
    /// <param name="filename">文件名</param>
    /// <param name="mimeType">MIME 类型</param>
    /// <returns>平台消息 id；失败返回 null</returns>
    Task<string?> SendAttachmentAsync(string destination, byte[] data, string filename, string mimeType);

    /// <summary>
    /// 下载入站附件字节（bridge：BlueBubbles 附件端点；amb：preDownload → 下载 → AES/CTR 解密）。
    /// 返回**类型化结果**：调用方必须能区分"下载到字节"、"协议不支持/缺少解密输入"、"彻底失败"，
    /// 不得像旧版那样把失败一律返回 null 而让入库路径把附件静默丢掉。
    /// </summary>
    /// <param name="attachment">附件元数据（amb 需要 url/signature/owner，bridge 用 guid 拼附件数据路径）</param>
    /// <param name="mode">当前传输模式（bridge / amb_msp / amb_apple_direct）</param>
    Task<IMessageAttachmentDownloadResult> DownloadAttachmentAsync(IMessageAttachmentRef attachment, string mode);

    /// <summary>标记入站消息已读（bridge 走 private-api，未关闭 SIP 时会失败；amb 无此操作）。</summary>
    Task<bool> MarkAsReadAsync(string chatGuidOrSourceId);

    /// <summary>
    /// 健康检查。bridge：GET /api/v1/ping（或 /server/info）；amb：GET 网关可达性探测（不发消息）。
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 向 BlueBubbles 注册 webhook（POST /api/v1/server/webhook，等价 /api/v1/push）。
    /// 仅在管理员显式触发时调用；AMB 模式下返回 false（入站由 Apple/MSP 侧配置）。
    /// </summary>
    Task<bool> RegisterWebhookAsync(string publicWebhookUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取桥接服务器版本（bridge：GET /api/v1/server/info），用于与 MinServerVersion 比对。
    /// REST 需 ≥0.2.0、webhook 需 ≥1.0.0；AMB 模式返回 null。
    /// </summary>
    Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出桥接服务器上已登记的 webhook 地址（GET /api/v1/push）。
    /// 返回结果必须已剔除任何可能带密钥的查询串；AMB 模式返回空列表。
    /// </summary>
    Task<List<string>> ListRegisteredWebhooksAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// iMessage 出站发送结果（fix 5）：把 HTTP 状态码与"是否可重试"显式带回队列。
/// 旧实现把所有失败折叠成 null，队列无法区分 410（用户已关闭会话，永久失败）与 503（Apple 暂时不可用），
/// 于是 410 也会被重试到 MaxAttempts，既浪费配额又拖慢队列。
/// </summary>
/// <param name="Success">是否发送成功</param>
/// <param name="ExternalMessageId">成功时的平台消息 id（同时是本次尝试的幂等 id）</param>
/// <param name="HttpStatusCode">平台返回的 HTTP 状态码；超时/网络异常时为空</param>
/// <param name="ProviderErrorCode">平台错误码（AMB body.code / bridge error.type，若有）</param>
/// <param name="ErrorMessage">可读错误原因（可直接落 ImMessageLogs.ErrorMessage）</param>
/// <param name="IsTransient">是否可重试：仅 5xx / 429 / 408 / 超时 / 网络异常为 true；其余一律 false</param>
public record IMessageSendOutcome(
    bool Success,
    string? ExternalMessageId = null,
    int? HttpStatusCode = null,
    string? ProviderErrorCode = null,
    string? ErrorMessage = null,
    bool IsTransient = false)
{
    public static IMessageSendOutcome Ok(string externalMessageId) => new(true, externalMessageId);

    /// <summary>永久失败：重试只会无限重复同一错误（400/403/404/410/配置缺失等）。</summary>
    public static IMessageSendOutcome Terminal(string errorMessage, int? status = null, string? providerErrorCode = null)
        => new(false, null, status, providerErrorCode, errorMessage, IsTransient: false);

    /// <summary>可重试失败：5xx / 429 / 408 / 超时 / 网络异常。</summary>
    public static IMessageSendOutcome Retryable(string errorMessage, int? status = null, string? providerErrorCode = null)
        => new(false, null, status, providerErrorCode, errorMessage, IsTransient: true);
}

/// <summary>入站附件的下载/解密结果类型。</summary>
public enum IMessageAttachmentOutcomeKind
{
    /// <summary>字节已就绪（bridge 原始字节，或 AMB 已解密）。</summary>
    Success = 0,

    /// <summary>协议路径可走，但缺少解密所需的输入（如 preDownload 未返回 key/iv）⇒ 不可入库，只能记占位。</summary>
    Unsupported = 1,

    /// <summary>下载/解密失败（HTTP 错误、网络异常、密文损坏等）。</summary>
    Failed = 2,
}

/// <summary>
/// 入站附件下载结果。
/// 之所以要有 <see cref="IMessageAttachmentOutcomeKind.Unsupported"/>：docs/integration/imessage.md §3
/// 只描述了 AES/CTR + 全零 IV，却没有规定"入站附件的 key 从哪里来"（key 由上传方生成）。
/// 若 preDownload 响应里拿不到 key/iv，就只能诚实上报"协议输入缺失"，绝不把密文当明文入库。
/// </summary>
/// <param name="Kind">结果类型</param>
/// <param name="Data">成功时的字节</param>
/// <param name="MimeType">平台给出的 MIME 类型（若有）</param>
/// <param name="Filename">平台给出的文件名（若有）</param>
/// <param name="ErrorMessage">失败/不支持的具体原因（写日志与占位说明）</param>
public record IMessageAttachmentDownloadResult(
    IMessageAttachmentOutcomeKind Kind,
    byte[]? Data = null,
    string? MimeType = null,
    string? Filename = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Kind == IMessageAttachmentOutcomeKind.Success && Data is { Length: > 0 };

    public static IMessageAttachmentDownloadResult Ok(byte[] data, string? mimeType = null, string? filename = null)
        => new(IMessageAttachmentOutcomeKind.Success, data, mimeType, filename);

    public static IMessageAttachmentDownloadResult Unsupported(string reason, string? mimeType = null, string? filename = null)
        => new(IMessageAttachmentOutcomeKind.Unsupported, null, mimeType, filename, reason);

    public static IMessageAttachmentDownloadResult Failed(string reason, string? mimeType = null, string? filename = null)
        => new(IMessageAttachmentOutcomeKind.Failed, null, mimeType, filename, reason);
}
