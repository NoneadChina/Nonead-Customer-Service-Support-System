using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Facebook Messenger Graph API HTTP 客户端接口 - 封装 Send API 发送、sender_action、
/// 媒体下载/上传、客户资料读取、Webhook 字段订阅与可达性探测。
/// 接口定义在 Application 层，实现在 Infrastructure 层的 MessengerClient。
/// 不引入新的 NuGet 包：统一 HttpClient（IHttpClientFactory）+ System.Text.Json。
/// </summary>
public interface IMessengerClient
{
    /// <summary>
    /// 发送文本消息（单条，调用方负责按 TextChunkChars 分片）。
    /// </summary>
    /// <param name="psid">接收者 PSID（sender.id）</param>
    /// <param name="body">文本内容</param>
    /// <param name="messageTag">窗口外主动回复的 message_tag（如 HUMAN_AGENT；null 表示不带）</param>
    /// <param name="replyToMid">回复引用的入站消息 mid（可选）</param>
    /// <returns>成功返回出站 mid，失败返回 null（错误详情见 LastError）</returns>
    Task<string?> SendTextMessageAsync(string psid, string body, string? messageTag = null, string? replyToMid = null);

    /// <summary>
    /// 发送附件消息。AttachmentSpec.Kind：url(直发外链) / id(Attachment Upload API 返回的 attachment_id)。
    /// </summary>
    /// <param name="psid">接收者 PSID</param>
    /// <param name="spec">附件描述</param>
    /// <param name="messageTag">窗口外主动回复的 message_tag（可选）</param>
    /// <returns>成功返回出站 mid，失败返回 null</returns>
    Task<string?> SendAttachmentAsync(string psid, MessengerAttachmentSpec spec, string? messageTag = null);

    /// <summary>
    /// 上传附件到 Attachment Upload API，取得 attachment_id（payload.is_reusable=true）。
    /// </summary>
    /// <param name="data">文件字节流</param>
    /// <param name="mimeType">MIME 类型（映射为 attachment type：image/audio/video/file）</param>
    /// <returns>attachment_id；失败返回 null</returns>
    Task<string?> UploadAttachmentAsync(byte[] data, string mimeType);

    /// <summary>
    /// 下载入站媒体（Webhook payload.url 为短 TTL 签名 CDN 链，收到即转存）。
    /// </summary>
    /// <param name="mediaUrl">payload.url（无需 Bearer Token；失败时调用方可改用 attachment_id 路径）</param>
    /// <returns>字节流与 MIME 类型；失败返回 null</returns>
    Task<(byte[] data, string? mimeType)?> DownloadMediaAsync(string mediaUrl);

    /// <summary>
    /// 发送 sender_action（typing_on / typing_off / mark_seen）。
    /// </summary>
    /// <param name="psid">接收者 PSID</param>
    /// <param name="action">typing_on / typing_off / mark_seen</param>
    /// <returns>是否成功</returns>
    Task<bool> SendSenderActionAsync(string psid, string action);

    /// <summary>
    /// 读取 PSID 客户资料（GET /{ver}/me/{PSID}?fields=first_name,last_name,profile_pic,locale,timezone,gender）。
    /// </summary>
    /// <param name="psid">客户 PSID</param>
    /// <returns>客户资料；失败或未配置返回 null</returns>
    Task<MessengerCustomerProfile?> GetCustomerProfileAsync(string psid);

    /// <summary>
    /// 订阅 Webhook 字段（POST /{ver}/me/subscribed_apps?subscribed_fields=…，需 pages_manage_metadata）。
    /// </summary>
    /// <param name="fields">字段清单；null 时用配置的 MessengerOptions.SubscribedFields</param>
    /// <returns>是否订阅成功</returns>
    Task<bool> SubscribeFieldsAsync(string? fields = null);

    /// <summary>
    /// 探测 Graph API 可达性（GET /{ver}/me?fields=id,name，不发送任何消息）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌（超时控制）</param>
    /// <returns>可达返回 true；超时/网络错误/未配置返回 false</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 最近一次调用失败的 Graph 错误（code / message / fbtrace_id），供服务层回报与入库便于报障。
    /// 成功调用不写入；实现按实例（Scoped）保存，不做跨请求断言。
    /// </summary>
    MessengerApiError? LastError { get; }
}
