using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 会话状态变更的事件监听器
/// 对应 PHP: app/Events/ConversationStatusChanged.php
/// <para>
/// 只负责「会话状态已变更」站内通知：审计写入已由 AuditActionFilter（action="status_change"）覆盖，
/// SignalR 实时推送已由 ConversationStatusService 的 NotifyMailboxAsync("conversation_status_changed") 覆盖，
/// 在此重复写入会产生双份审计记录。
/// </para>
/// </summary>
public class ConversationStatusChangedListener : IListener<ConversationStatusChangedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<ConversationStatusChangedListener> _logger;

    public ConversationStatusChangedListener(
        INotificationService notificationService,
        ILogger<ConversationStatusChangedListener> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task HandleAsync(ConversationStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        var conversation = @event.Conversation;
        var newStatus = @event.NewStatus;

        if (conversation.UserId.HasValue)
        {
            await _notificationService.SendNotificationAsync(
                conversation.UserId.Value,
                "会话状态已变更",
                $"会话 #{conversation.Id} 状态变更为: {newStatus}",
                $"/conversations/{conversation.Id}");
        }

        _logger.LogInformation("会话 {ConversationId} 状态已变更: {OldStatus} -> {NewStatus}", conversation.Id, @event.OldStatus, newStatus);
    }
}
