using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 微信服务号管理服务 - 配置读写、连接状态查询、客服消息回复、模板消息、菜单管理。
/// </summary>
public interface IWeChatService
{
    /// <summary>
    /// 获取当前微信配置（敏感字段脱敏）。
    /// </summary>
    Task<WeChatSettingsDto> GetSettingsAsync();

    /// <summary>
    /// 更新微信配置，持久化到 SystemOptions 数据库表并刷新内存缓存。
    /// </summary>
    /// <param name="request">配置更新请求（AppSecret/EncodingAESKey 为 null 表示不修改）</param>
    Task<WeChatSettingsDto> UpdateSettingsAsync(UpdateWeChatSettingsRequest request);

    /// <summary>
    /// 获取微信连接状态（是否已配置、是否已启用、access_token 是否有效）。
    /// </summary>
    Task<WeChatStatusDto> GetStatusAsync();

    /// <summary>
    /// 通过微信客服消息接口回复客户，并在会话中创建回复线程。
    /// </summary>
    /// <param name="request">回复请求（包含会话 ID 和回复内容）</param>
    /// <param name="userId">当前操作用户 ID</param>
    Task<WeChatReplyResult> ReplyAsync(WeChatReplyRequest request, int userId);

    /// <summary>
    /// 发送模板消息到指定微信用户（旧版，新注册服务号已停用）。
    /// </summary>
    Task<WeChatTemplateResult> SendTemplateMessageAsync(WeChatTemplateRequest request, int userId);

    /// <summary>
    /// 发送订阅通知到指定微信用户（新版，替代旧版模板消息）。
    /// 需用户主动订阅后才能下发。
    /// </summary>
    Task<WeChatTemplateResult> SendSubscribeMessageAsync(WeChatSubscribeRequest request, int userId);

    /// <summary>
    /// 创建自定义菜单。
    /// </summary>
    Task<WeChatMenuResult> CreateMenuAsync(WeChatMenuRequest request);

    /// <summary>
    /// 查询当前自定义菜单。
    /// </summary>
    Task<string?> GetMenuAsync();

    /// <summary>
    /// 删除自定义菜单。
    /// </summary>
    Task<WeChatMenuResult> DeleteMenuAsync();
}

/// <summary>
/// 微信回复结果
/// </summary>
public class WeChatReplyResult
{
    /// <summary>客服消息是否发送成功</summary>
    public bool Sent { get; set; }

    /// <summary>线程记录是否创建成功（消息已发送但线程记录失败时为 false）</summary>
    public bool ThreadCreated { get; set; } = false;

    /// <summary>创建的回复线程 ID</summary>
    public int? ThreadId { get; set; }

    /// <summary>错误信息（发送失败或部分失败时）</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 微信连接状态 DTO
/// </summary>
public class WeChatStatusDto
{
    public bool Configured { get; set; }
    public bool Enabled { get; set; }
    public bool TokenValid { get; set; }
    public string? CallbackUrl { get; set; }
}

/// <summary>
/// 微信模板消息发送结果
/// </summary>
public class WeChatTemplateResult
{
    public bool Sent { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 微信菜单操作结果
/// </summary>
public class WeChatMenuResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
