using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// LINE 入站服务：Webhook 载荷（已验签）落库、客户/会话复用、媒体转存、自动回复与人工接管判定。
/// </summary>
public interface ILineIngestionService
{
    /// <summary>处理整个 Webhook 载荷（可能含多事件）；单事件失败不影响其余事件</summary>
    Task<LineIngestionResult> ProcessWebhookPayloadAsync(string? payload);

    /// <summary>处理单个事件（便于队列重试与单测）</summary>
    Task<LineIngestionResult> ProcessEventAsync(LineWebhookEvent evt, string? destination);
}
