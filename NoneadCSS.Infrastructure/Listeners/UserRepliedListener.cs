using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 坐席回复的事件监听器：只负责给会话关注者发站内通知（回复者本人除外）。
/// 对应 PHP: app/Listeners/UserReplied.php
///
/// 已经由别处覆盖、因此本监听器不再重复执行的副作用：
/// - 审计日志：NoneadCSS.Api/Filters/AuditActionFilter.cs:209 对 Conversations 控制器的 reply 动作
///   已写入 action="reply"（原这里的 Action="Reply" 属重复写入，已删除）。
/// - 实时推送：NoneadCSS.Application/Services/ConversationThreadService.cs 的
///   NotifyMailboxAsync("reply_created") 已在发布事件之前执行。
///
/// 关注者是 <see cref="INotificationService.GetConversationFollowersAsync"/> 的唯一调用点，
/// 「给关注者发通知」此前没有任何等价实现，因此这段逻辑必须保留。
/// </summary>
public class UserRepliedListener : IListener<UserRepliedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<UserRepliedListener> _logger;

    public UserRepliedListener(
        INotificationService notificationService,
        ILogger<UserRepliedListener> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task HandleAsync(UserRepliedEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        var conversation = @event.Conversation;
        var thread = @event.Thread;
        var user = @event.User;

        var followers = await _notificationService.GetConversationFollowersAsync(conversation.Id);
        foreach (var followerId in followers.Where(f => f != user.Id))
        {
            await _notificationService.SendNotificationAsync(
                followerId,
                "会话有新回复",
                $"您关注的会话 #{conversation.Id} 有新回复",
                $"/conversations/{conversation.Id}");
        }

        _logger.LogInformation("用户 {UserId} 回复会话 {ConversationId}（线程 {ThreadThreadId}）的通知已发送",
            user.Id, conversation.Id, thread.Id);
    }
}
