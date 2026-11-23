using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface INotificationRuleRepository : IRepository<NotificationRule>
{
    Task<IEnumerable<NotificationRule>> GetByEventTypeAsync(string eventType);
    Task<IEnumerable<NotificationRule>> GetByMailboxIdAsync(int mailboxId);
    Task<IEnumerable<NotificationRule>> GetActiveAsync();
    Task<IEnumerable<NotificationRule>> GetActiveByEventTypeAsync(string eventType);
    Task<(IEnumerable<NotificationRule> Items, int TotalCount)> GetPagedAsync(
        string? eventType, int? mailboxId, int? userId, bool? isActive, string? search, int page, int pageSize);

    // 垃圾箱
    Task<(IEnumerable<NotificationRule> Items, int TotalCount)> GetDeletedPagedAsync(
        string? search, int page, int pageSize);
    Task<int> CountDeletedAsync();
    Task SoftDeleteAsync(int id);
    Task RestoreAsync(int id);
    Task HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}
