using System.Text.Json;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 钉钉开放平台 HTTP 客户端接口 - 封装消息发送、媒体上传/下载、Access Token 获取等 API 调用。
/// 接口定义在 Application 层，实现在 Infrastructure 层的 DingDingClient。
/// </summary>
public interface IDingDingClient
{
    /// <summary>
    /// 获取钉钉 Access Token（内部自动缓存和刷新）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌（超时时底层 HTTP 请求也会被取消，避免锁竞争）</param>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 通过 sessionWebhook 回复消息（即时回复收到消息的会话）。
    /// </summary>
    /// <param name="sessionWebhook">会话 Webhook 地址</param>
    /// <param name="msgType">消息类型：text / image</param>
    /// <param name="payload">消息体 JSON 对象（text: {content}，image: {base64, md5}）</param>
    /// <returns>是否发送成功</returns>
    Task<bool> SendBySessionWebhookAsync(string sessionWebhook, string msgType, object payload);

    /// <summary>
    /// 批量发送单聊消息（主动给用户发消息）。
    /// POST /v1.0/robot/oToMessages/batchSend
    /// </summary>
    /// <param name="robotCode">机器人编码</param>
    /// <param name="userIds">接收者 userId 列表（最多 20 人）</param>
    /// <param name="msgKey">消息类型：sampleText / sampleImageMsg / sampleFile / sampleMarkdown</param>
    /// <param name="content">消息内容 JSON 字符串</param>
    /// <returns>发送结果 processQueryKey，失败返回 null</returns>
    Task<string?> BatchSendOtoMessageAsync(string robotCode, string[] userIds, string msgKey, string content);

    /// <summary>
    /// 通过 sessionWebhook 发送图片消息（base64+md5，企业内部应用机器人单聊可用）。
    /// body: {"msgtype":"image","image":{"base64":"...","md5":"..."}}
    /// </summary>
    /// <param name="sessionWebhook">会话 Webhook 地址</param>
    /// <param name="imageBase64">图片内容的 Base64 编码（不含 data:image/xxx;base64, 前缀）</param>
    /// <param name="imageMd5">图片内容的 MD5 HEX 字符串</param>
    /// <returns>是否发送成功</returns>
    Task<bool> SendImageBySessionWebhookAsync(string sessionWebhook, string imageBase64, string imageMd5);

    /// <summary>
    /// 通过批量发送单聊消息接口发送图片消息（需要先上传媒体获取 mediaId）。
    /// msgKey = sampleImageMsg, msgParam = {"photoURL":"@media_id"}
    /// </summary>
    /// <param name="robotCode">机器人编码</param>
    /// <param name="userIds">接收者 userId 列表（最多 20 人）</param>
    /// <param name="photoMediaId">通过 UploadMediaAsync 上传图片获取的 media_id</param>
    /// <returns>发送结果 processQueryKey，失败返回 null</returns>
    Task<string?> BatchSendImageMessageAsync(string robotCode, string[] userIds, string photoMediaId);

    /// <summary>
    /// 通过批量发送单聊消息接口发送文件消息（需要先上传媒体获取 mediaId）。
    /// msgKey = sampleFile, msgParam = {"mediaId":"@media_id","fileName":"xxx","fileType":"ext"}
    /// </summary>
    /// <param name="robotCode">机器人编码</param>
    /// <param name="userIds">接收者 userId 列表（最多 20 人）</param>
    /// <param name="fileMediaId">通过 UploadMediaAsync 上传文件获取的 media_id</param>
    /// <param name="fileName">显示的文件名</param>
    /// <param name="fileType">文件类型扩展名（不含点，如 xlsx、pdf、zip、doc、docx）</param>
    /// <returns>发送结果 processQueryKey，失败返回 null</returns>
    Task<string?> BatchSendFileMessageAsync(string robotCode, string[] userIds, string fileMediaId, string fileName, string fileType);

    /// <summary>
    /// 上传媒体文件获取 media_id（用于发送图片/语音/视频/文件消息）。
    /// POST /media/upload
    /// </summary>
    /// <param name="data">文件字节流</param>
    /// <param name="filename">文件名</param>
    /// <param name="mimeType">文件 MIME 类型</param>
    /// <returns>media_id，失败返回 null</returns>
    Task<string?> UploadMediaAsync(byte[] data, string filename, string mimeType);

    /// <summary>
    /// 根据手机号获取钉钉 userId。
    /// GET /user/get_by_mobile
    /// </summary>
    /// <param name="mobile">手机号码</param>
    /// <returns>userId，失败返回 null</returns>
    Task<string?> GetUserIdByMobileAsync(string mobile);

    /// <summary>
    /// 根据 userId 获取钉钉用户详情中的 email 字段。
    /// POST /topapi/v2/user/get（旧版服务端 API，企业内部应用可用，需"通讯录-个人邮箱"读取权限 field_email）。
    /// </summary>
    /// <param name="userId">钉钉用户 userId（机器人回调中的 senderStaffId）</param>
    /// <returns>email 字符串；用户未设置邮箱、无权限或调用失败时返回 null</returns>
    Task<string?> GetUserEmailAsync(string userId);

    /// <summary>
    /// 根据 userId 获取钉钉用户完整详情（姓名、头像、邮箱、手机号、职位、办公地点、unionid、备注等）。
    /// POST /topapi/v2/user/get。
    /// 企业内部应用需开通：
    ///   - "成员信息读权限"（基础）
    ///   - "企业员工手机号信息"权限（读 mobile 字段）
    ///   - "邮箱等个人信息"权限（读 email、work_place、remark、extension 字段）
    /// </summary>
    /// <param name="userId">钉钉用户 userId（senderStaffId）</param>
    /// <returns>解析后的详情 JSON（result 节点）；失败返回 null</returns>
    Task<JsonElement?> GetUserDetailAsync(string userId);

    /// <summary>
    /// 下载机器人接收消息中的媒体文件（图片/语音/视频/文件）。
    /// POST /v1.0/robot/messageFiles/download 获取临时下载链接，再从下载链接获取实际文件内容。
    /// </summary>
    /// <param name="downloadCode">机器人回调消息中的临时下载码</param>
    /// <param name="robotCode">机器人编码</param>
    /// <returns>媒体数据和扩展名（按 Content-Type 推断），失败返回 null</returns>
    Task<(byte[] data, string? extension)?> DownloadMediaAsync(string downloadCode, string robotCode);

    /// <summary>
    /// 测试 API 可达性（尝试获取 Access Token）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
