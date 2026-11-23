namespace NoneadCSS.Domain.Events;

/// <summary>
/// 通知发布器接口 - 用于 Application 层发送实时通知
/// </summary>
public interface INotificationPublisher
{
    /// <summary>向所有用户发送通知</summary>
    Task NotifyAllAsync(string eventName, object data);

    /// <summary>向指定用户发送通知</summary>
    Task NotifyUserAsync(string userId, string eventName, object data);

    /// <summary>向指定邮箱组发送通知</summary>
    Task NotifyMailboxAsync(string mailboxId, string eventName, object data);

    /// <summary>向指定组发送通知</summary>
    Task NotifyGroupAsync(string groupName, string eventName, object data);
}
