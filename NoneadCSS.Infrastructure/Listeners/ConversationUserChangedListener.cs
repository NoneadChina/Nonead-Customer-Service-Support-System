using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 会话分配变更的事件监听器：只负责给被分配的坐席发站内通知。
/// 对应 PHP: app/Listeners/ConversationUserChanged.php
///
/// 已经由别处覆盖、因此本监听器不再重复执行的副作用：
/// - 审计日志：NoneadCSS.Api/Filters/AuditActionFilter.cs:210 对 Conversations 控制器的 assign 动作
///   已写入 action="assign"（原这里的 Action="Assign" 属重复写入，已删除）。
/// - 实时推送：NoneadCSS.Application/Services/ConversationAssignmentService.cs 的 NotifyUserAsync
///   （SignalR 定向推送）已在发布事件之前执行。
/// </summary>
public class ConversationUserChangedListener : IListener<ConversationUserChangedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<ConversationUserChangedListener> _logger;

    public ConversationUserChangedListener(
        INotificationService notificationService,
        ILogger<ConversationUserChangedListener> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task HandleAsync(ConversationUserChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        var conversation = @event.Conversation;
        var newUserId = @event.NewUserId;

        if (newUserId.HasValue)
        {
            await _notificationService.SendNotificationAsync(
                newUserId.Value,
                "您有新的会话",
                $"会话 #{conversation.Id} ({conversation.Subject}) 已分配给您",
                $"/conversations/{conversation.Id}");
        }

        _logger.LogInformation("会话 {ConversationId} 分配通知已发送: {OldUserId} -> {NewUserId}",
            conversation.Id, @event.OldUserId, newUserId);
    }
}
