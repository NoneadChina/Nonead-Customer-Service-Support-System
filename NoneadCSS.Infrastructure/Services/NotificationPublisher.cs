using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Events;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 通知发布器实现 - 委托给 SignalR NotificationHubService
/// </summary>
public class NotificationPublisher : INotificationPublisher
{
    private readonly INotificationHubService _hubService;

    public NotificationPublisher(INotificationHubService hubService)
    {
        _hubService = hubService;
    }

    public Task NotifyAllAsync(string eventName, object data)
        => _hubService.SendToAll(eventName, data);

    public Task NotifyUserAsync(string userId, string eventName, object data)
    {
        if (int.TryParse(userId, out var id))
            return _hubService.SendToUser(id, eventName, data);
        return Task.CompletedTask;
    }

    public Task NotifyMailboxAsync(string mailboxId, string eventName, object data)
        => _hubService.SendToGroup($"mailbox_{mailboxId}", eventName, data);

    public Task NotifyGroupAsync(string groupName, string eventName, object data)
        => _hubService.SendToGroup(groupName, eventName, data);
}
