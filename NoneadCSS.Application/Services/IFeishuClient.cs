using System.Text.Json;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 飞书 API HTTP 客户端接口 - 封装消息发送、回复、媒体上传等 API 调用。
/// 接口定义在 Application 层，实现在 Infrastructure 层的 FeishuClient。
/// </summary>
public interface IFeishuClient
{
    /// <summary>
    /// 发送消息到指定用户（通过 open_id）。
    /// </summary>
    /// <param name="openId">接收者 Open ID</param>
    /// <param name="msgType">消息类型：text / image / file / audio / media / post / interactive</param>
    /// <param name="content">消息内容 JSON 字符串</param>
    /// <returns>飞书消息 ID；失败返回 null</returns>
    Task<string?> SendMessageAsync(string openId, string msgType, string content);

    /// <summary>
    /// 回复消息（在现有消息上回复）。
    /// </summary>
    /// <param name="messageId">被回复的消息 ID</param>
    /// <param name="msgType">消息类型</param>
    /// <param name="content">消息内容 JSON 字符串</param>
    /// <returns>飞书消息 ID；失败返回 null</returns>
    Task<string?> ReplyMessageAsync(string messageId, string msgType, string content);

    /// <summary>
    /// 上传图片，返回 image_key。
    /// </summary>
    /// <param name="data">图片字节流</param>
    /// <param name="filename">文件名（含扩展名）</param>
    /// <returns>image_key；失败返回 null</returns>
    Task<string?> UploadImageAsync(byte[] data, string filename);

    /// <summary>
    /// 上传文件，返回 file_key。
    /// </summary>
    /// <param name="data">文件字节流</param>
    /// <param name="filename">文件名（含扩展名）</param>
    /// <param name="fileType">文件类型：opus / mp4 / pdf / doc / xls / ppt / stream</param>
    /// <returns>file_key；失败返回 null</returns>
    Task<string?> UploadFileAsync(byte[] data, string filename, string fileType);

    /// <summary>
    /// 下载消息中的媒体资源（图片/文件）。
    /// </summary>
    /// <param name="messageId">飞书消息 ID（用于构造下载路径）</param>
    /// <param name="fileKey">文件 key（image_key 或 file_key）</param>
    /// <returns>二进制数据和文件扩展名；失败返回 null</returns>
    Task<(byte[] data, string? extension)?> DownloadMediaAsync(string messageId, string fileKey);

    /// <summary>
    /// 获取用户信息。
    /// </summary>
    /// <param name="openId">用户 Open ID</param>
    /// <returns>用户信息 JSON；失败返回 null</returns>
    Task<JsonElement?> GetUserInfoAsync(string openId);
}
