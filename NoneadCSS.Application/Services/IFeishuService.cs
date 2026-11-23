using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 飞书机器人管理服务 - 配置读写、连接状态查询、消息回复。
/// 配置持久化到 SystemOptions 数据库表（敏感字段加密），通过 IFeishuSettingsProvider 刷新内存缓存。
/// </summary>
public interface IFeishuService
{
    /// <summary>获取当前飞书配置（敏感字段脱敏）。</summary>
    Task<FeishuSettingsDto> GetSettingsAsync();

    /// <summary>更新飞书配置，持久化到 SystemOptions 并刷新内存缓存。</summary>
    Task<FeishuSettingsDto> UpdateSettingsAsync(UpdateFeishuSettingsRequest request);

    /// <summary>获取飞书连接状态。</summary>
    Task<FeishuStatusDto> GetStatusAsync();

    /// <summary>通过飞书消息 API 回复客户，并在会话中创建回复线程。</summary>
    Task<FeishuReplyResult> ReplyAsync(FeishuReplyRequest request, int userId);
}

/// <summary>飞书回复结果</summary>
public class FeishuReplyResult
{
    public bool Sent { get; set; }
    public bool ThreadCreated { get; set; }
    public int? ThreadId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>飞书连接状态 DTO</summary>
public class FeishuStatusDto
{
    public bool Configured { get; set; }
    public bool Enabled { get; set; }

    /// <summary>Token 是否有效。Stream 模式下为 null（不显示 Token 状态，只看 Stream 连接状态）</summary>
    public bool? TokenValid { get; set; }
    public string? CallbackUrl { get; set; }

    /// <summary>事件接收模式："Http" 或 "Stream"</summary>
    public string? ReceiveMode { get; set; }

    /// <summary>Stream 长连接是否已建立（仅 Stream 模式有意义）</summary>
    public bool StreamConnected { get; set; }

    /// <summary>Stream 最近断开原因（仅 Stream 模式有意义）</summary>
    public string? StreamError { get; set; }
}
