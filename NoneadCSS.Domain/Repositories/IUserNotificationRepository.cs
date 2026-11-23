using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// 站内通知（UserNotifications）仓储。
/// 用于把通知规则命中的收件人持久化为站内信，解决「通知中心只有邮件、页面刷新即丢」的问题。
/// </summary>
public interface IUserNotificationRepository : IRepository<UserNotification>
{
    /// <summary>按用户分页查询站内通知（时间倒序）。</summary>
    Task<(IEnumerable<UserNotification> Items, int Total)> GetByUserIdAsync(int userId, int page, int pageSize);

    /// <summary>统计用户未读站内通知数。</summary>
    Task<int> CountUnreadAsync(int userId);
}
