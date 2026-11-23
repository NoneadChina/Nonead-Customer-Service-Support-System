using System.Text.Json;

namespace NoneadCSS.Application.Services;

/// <summary>
/// WhatsApp Cloud API HTTP 客户端接口 - 封装消息发送、媒体上传/下载、已读标记等 API 调用。
/// 接口定义在 Application 层，实现在 Infrastructure 层的 WhatsAppClient。
/// </summary>
public interface IWhatsAppClient
{
    /// <summary>
    /// 发送文本消息。
    /// </summary>
    /// <param name="waId">接收者 WhatsApp 号码（含国家区号，无 + 号）</param>
    /// <param name="body">文本内容</param>
    /// <param name="contextMessageId">回复引用的消息 ID（可选，用于直接回复特定消息）</param>
    /// <returns>发送成功返回 wamid（消息 ID），失败返回 null</returns>
    Task<string?> SendTextMessageAsync(string waId, string body, string? contextMessageId = null);

    /// <summary>
    /// 发送媒体消息（图片/音频/视频/文档/贴纸）。
    /// </summary>
    /// <param name="waId">接收者 WhatsApp 号码</param>
    /// <param name="mediaType">媒体类型：image / audio / video / document / sticker</param>
    /// <param name="mediaId">已上传的媒体 ID</param>
    /// <param name="caption">图片/视频/文档的标题（可选）</param>
    /// <param name="filename">文档的文件名（document 类型时推荐）</param>
    /// <returns>发送成功返回 wamid，失败返回 null</returns>
    Task<string?> SendMediaMessageAsync(string waId, string mediaType, string mediaId, string? caption = null, string? filename = null);

    /// <summary>
    /// 发送模板消息（不受 24 小时客户服务窗口限制）。
    /// </summary>
    /// <param name="waId">接收者 WhatsApp 号码</param>
    /// <param name="templateName">模板名称（需在 Meta 平台预审批）</param>
    /// <param name="language">模板语言代码（如 zh_CN / en_US）</param>
    /// <param name="components">模板组件参数（JSON 序列化后的对象）</param>
    /// <returns>发送成功返回 wamid，失败返回 null</returns>
    Task<string?> SendTemplateMessageAsync(string waId, string templateName, string language, object? components = null);

    /// <summary>
    /// 上传媒体文件到 Meta，获取 media_id（有效期 30 天）。
    /// </summary>
    /// <param name="data">文件字节流</param>
    /// <param name="filename">文件名</param>
    /// <param name="mimeType">文件 MIME 类型</param>
    /// <returns>media_id；失败返回 null</returns>
    Task<string?> UploadMediaAsync(byte[] data, string filename, string mimeType);

    /// <summary>
    /// 下载媒体文件（Webhook 中的 URL 5 分钟有效，需 Bearer Token 下载）。
    /// </summary>
    /// <param name="mediaUrl">Webhook 返回的媒体 URL</param>
    /// <returns>文件字节流和 MIME 类型；失败返回 null</returns>
    Task<(byte[] data, string? mimeType)?> DownloadMediaAsync(string mediaUrl);

    /// <summary>
    /// 标记客户消息为已读。
    /// </summary>
    /// <param name="messageId">Webhook 接收到的消息 ID（wamid.xxx）</param>
    /// <returns>是否标记成功</returns>
    Task<bool> MarkAsReadAsync(string messageId);

    /// <summary>
    /// 探测 Graph API 可达性（轻量级 GET 请求，用于 GetStatusAsync 的 ApiReachable）。
    /// 通过请求 PhoneNumberId 资源返回 200 判断配置与网络是否正常，不发送任何消息。
    /// </summary>
    /// <param name="cancellationToken">取消令牌（用于超时控制）</param>
    /// <returns>API 可达返回 true；超时/网络错误/未配置返回 false</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
