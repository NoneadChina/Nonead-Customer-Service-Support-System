using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 钉钉管理服务 - 配置读写、连接状态查询、回复消息。
/// </summary>
public interface IDingDingService
{
    /// <summary>获取当前钉钉配置（敏感字段脱敏）。</summary>
    Task<DingDingSettingsDto> GetSettingsAsync();

    /// <summary>更新钉钉配置，持久化到 SystemOptions 数据库表并刷新内存缓存。</summary>
    Task<DingDingSettingsDto> UpdateSettingsAsync(UpdateDingDingSettingsRequest request);

    /// <summary>获取钉钉连接状态。</summary>
    Task<DingDingStatusDto> GetStatusAsync();

    /// <summary>回复客户消息（通过 sessionWebhook 或批量发送接口）。</summary>
    Task<DingDingReplyResult> ReplyAsync(DingDingReplyRequest request, int userId);
}
