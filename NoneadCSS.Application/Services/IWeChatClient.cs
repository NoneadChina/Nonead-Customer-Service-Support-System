using System.Text.Json;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 微信 API HTTP 客户端接口 - 封装客服消息发送、素材下载、用户信息查询、模板消息、菜单管理等 API 调用。
/// 接口定义在 Application 层，实现在 Infrastructure 层的 WeChatClient。
/// </summary>
public interface IWeChatClient
{
    /// <summary>
    /// 发送客服文本消息（48 小时客服消息窗口内有效）。
    /// </summary>
    Task<bool> SendTextMessageAsync(string openId, string content);

    /// <summary>
    /// 发送客服图片消息。
    /// </summary>
    Task<bool> SendImageMessageAsync(string openId, string mediaId);

    /// <summary>
    /// 发送客服语音消息。
    /// </summary>
    Task<bool> SendVoiceMessageAsync(string openId, string mediaId);

    /// <summary>
    /// 发送客服视频消息。
    /// </summary>
    Task<bool> SendVideoMessageAsync(string openId, string mediaId);

    /// <summary>
    /// 上传临时素材（图片、语音、视频等），返回 media_id。
    /// 临时素材有效期为 3 天，仅在客服消息发送时使用。
    /// </summary>
    /// <param name="data">文件字节流</param>
    /// <param name="filename">文件名（含扩展名）</param>
    /// <param name="mimeType">文件 MIME 类型</param>
    /// <param name="mediaType">微信素材类型：image、voice、video、thumb</param>
    /// <returns>media_id；失败返回 null</returns>
    Task<string?> UploadMediaAsync(byte[] data, string filename, string mimeType, string mediaType);

    /// <summary>
    /// 下载临时素材（图片、语音、视频等），返回二进制流。
    /// </summary>
    Task<(byte[] data, string? extension)?> DownloadMediaAsync(string mediaId);

    /// <summary>
    /// 获取用户基本信息（昵称、头像、性别等）。
    /// </summary>
    Task<JsonElement?> GetUserInfoAsync(string openId);

    /// <summary>
    /// 发送模板消息（旧版，新注册服务号已停用，建议使用订阅消息）。
    /// 不受 48 小时客服消息窗口限制，但需要用户已关注服务号。
    /// </summary>
    /// <param name="openId">接收者 OpenId</param>
    /// <param name="templateId">模板 ID（在微信公众平台模板消息中获取）</param>
    /// <param name="data">模板数据（键值对，值为 { value, color } 结构）</param>
    /// <param name="url">点击跳转的 URL（可选）</param>
    /// <param name="miniProgram">小程序跳转配置（可选）</param>
    Task<bool> SendTemplateMessageAsync(string openId, string templateId, object data, string? url = null, object? miniProgram = null);

    /// <summary>
    /// 发送订阅通知（新版，替代旧版模板消息）。
    /// 需用户主动订阅后才能下发，不受 48 小时客服消息窗口限制。
    /// </summary>
    /// <param name="openId">接收者 OpenId</param>
    /// <param name="templateId">订阅消息模板 ID</param>
    /// <param name="data">模板数据（键值对，值为 { value } 结构）</param>
    /// <param name="page">点击跳转的页面路径（可选，如 pages/index）</param>
    /// <param name="miniprogram">小程序跳转配置（可选）</param>
    Task<bool> SendSubscribeMessageAsync(string openId, string templateId, object data, string? page = null, string? miniprogram = null);

    /// <summary>
    /// 创建自定义菜单。
    /// </summary>
    Task<bool> CreateMenuAsync(object menu);

    /// <summary>
    /// 查询自定义菜单。
    /// </summary>
    Task<JsonElement?> GetMenuAsync();

    /// <summary>
    /// 删除自定义菜单。
    /// </summary>
    Task<bool> DeleteMenuAsync();
}
