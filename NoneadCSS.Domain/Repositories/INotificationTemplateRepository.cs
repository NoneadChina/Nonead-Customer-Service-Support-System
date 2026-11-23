using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface INotificationTemplateRepository : IRepository<NotificationTemplate>
{
    Task<NotificationTemplate?> GetByEventTypeAsync(string eventType);
    Task<IEnumerable<NotificationTemplate>> GetActiveAsync();
    Task<bool> IsNameExistsAsync(string name, int? excludeId = null);
    Task<(IEnumerable<NotificationTemplate> Items, int TotalCount)> GetPagedAsync(
        string? eventType, bool? isActive, string? search, int page, int pageSize);

    // 垃圾箱
    Task<(IEnumerable<NotificationTemplate> Items, int TotalCount)> GetDeletedPagedAsync(
        string? search, int page, int pageSize);
    Task<int> CountDeletedAsync();
    Task SoftDeleteAsync(int id);
    Task RestoreAsync(int id);
    Task HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}
