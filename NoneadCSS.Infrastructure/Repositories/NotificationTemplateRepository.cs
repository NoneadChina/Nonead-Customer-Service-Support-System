using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class NotificationTemplateRepository : Repository<NotificationTemplate>, INotificationTemplateRepository
{
    public NotificationTemplateRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<NotificationTemplate?> GetByEventTypeAsync(string eventType)
    {
        return await _dbSet
            .FirstOrDefaultAsync(t => t.EventType == eventType && t.IsActive && t.DeletedAt == null);
    }

    public async Task<IEnumerable<NotificationTemplate>> GetActiveAsync()
    {
        return await _dbSet
            .Where(t => t.IsActive && t.DeletedAt == null)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<bool> IsNameExistsAsync(string name, int? excludeId = null)
    {
        var query = _dbSet.Where(t => t.Name == name && t.DeletedAt == null);
        if (excludeId.HasValue)
        {
            query = query.Where(t => t.Id != excludeId.Value);
        }
        return await query.AnyAsync();
    }

    public async Task<(IEnumerable<NotificationTemplate> Items, int TotalCount)> GetPagedAsync(
        string? eventType, bool? isActive, string? search, int page, int pageSize)
    {
        var query = _dbSet.Where(t => t.DeletedAt == null).AsQueryable();

        if (!string.IsNullOrEmpty(eventType))
        {
            query = query.Where(t => t.EventType == eventType);
        }

        if (isActive.HasValue)
        {
            query = query.Where(t => t.IsActive == isActive.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(t =>
                t.Name.Contains(search) ||
                t.Subject.Contains(search) ||
                t.EventType.Contains(search));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    // ====== 垃圾箱 ======
    public async Task<(IEnumerable<NotificationTemplate> Items, int TotalCount)> GetDeletedPagedAsync(
        string? search, int page, int pageSize)
    {
        var query = _dbSet.IgnoreQueryFilters().Where(t => t.DeletedAt != null).AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(t =>
                t.Name.Contains(search) ||
                t.Subject.Contains(search) ||
                t.EventType.Contains(search));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(t => t.DeletedAt != null);
    }

    public async Task SoftDeleteAsync(int id)
    {
        var tpl = await _dbSet.FindAsync(id);
        if (tpl != null)
        {
            tpl.DeletedAt = DateTime.UtcNow;
            tpl.IsActive = false;
            tpl.UpdatedAt = DateTime.UtcNow;
            // tpl 已通过 FindAsync 查询（tracking），由 SaveChangesAsync 持久化，无需 _dbSet.Update
        }
    }

    public async Task RestoreAsync(int id)
    {
        await _dbSet.IgnoreQueryFilters()
            .Where(t => t.Id == id && t.DeletedAt != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.DeletedAt, (DateTime?)null)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow));
    }

    public async Task HardDeleteAsync(int id)
    {
        await _dbSet.IgnoreQueryFilters()
            .Where(t => t.Id == id && t.DeletedAt != null)
            .ExecuteDeleteAsync();
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(t => t.DeletedAt != null);
        if (count > 0)
        {
            await _dbSet.IgnoreQueryFilters().Where(t => t.DeletedAt != null).ExecuteDeleteAsync();
        }
        return count;
    }
}
