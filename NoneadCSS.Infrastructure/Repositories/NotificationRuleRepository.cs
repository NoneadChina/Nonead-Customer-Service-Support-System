using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class NotificationRuleRepository : Repository<NotificationRule>, INotificationRuleRepository
{
    public NotificationRuleRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    private IQueryable<NotificationRule> ApplyIncludes(IQueryable<NotificationRule> query)
    {
        return query
            .Include(r => r.Template)
            .Include(r => r.User)
            .Include(r => r.Mailbox)
            .Include(r => r.Role)
            .Include(r => r.Team)
            .Include(r => r.RecipientMailbox);
    }

    public async Task<IEnumerable<NotificationRule>> GetByEventTypeAsync(string eventType)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(r => r.EventType == eventType && r.DeletedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<NotificationRule>> GetByMailboxIdAsync(int mailboxId)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(r => r.MailboxId == mailboxId && r.DeletedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<NotificationRule>> GetActiveAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(r => r.IsActive && r.DeletedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public override async Task<IEnumerable<NotificationRule>> GetAllAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(r => r.DeletedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<NotificationRule>> GetActiveByEventTypeAsync(string eventType)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(r => r.IsActive && r.EventType == eventType && r.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<(IEnumerable<NotificationRule> Items, int TotalCount)> GetPagedAsync(
        string? eventType, int? mailboxId, int? userId, bool? isActive, string? search, int page, int pageSize)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking())
            .Where(r => r.DeletedAt == null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(eventType))
        {
            query = query.Where(r => r.EventType == eventType);
        }

        if (mailboxId.HasValue)
        {
            query = query.Where(r => r.MailboxId == mailboxId.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(r => r.UserId == userId.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(r => r.IsActive == isActive.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(r =>
                r.Name.Contains(search) ||
                r.EventType.Contains(search) ||
                (r.Template != null && r.Template.Name.Contains(search)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    // ====== 垃圾箱 ======
    public async Task<(IEnumerable<NotificationRule> Items, int TotalCount)> GetDeletedPagedAsync(
        string? search, int page, int pageSize)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(r => r.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(r =>
                r.Name.Contains(search) ||
                r.EventType.Contains(search) ||
                (r.Template != null && r.Template.Name.Contains(search)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().AsNoTracking().CountAsync(r => r.DeletedAt != null);
    }

    public async Task SoftDeleteAsync(int id)
    {
        var rule = await _dbSet.FindAsync(id);
        if (rule != null)
        {
            rule.DeletedAt = DateTime.UtcNow;
            rule.IsActive = false;
            rule.UpdatedAt = DateTime.UtcNow;
            // rule 已通过 FindAsync 查询（tracking），由 SaveChangesAsync 持久化，无需 _dbSet.Update
        }
    }

    public async Task RestoreAsync(int id)
    {
        await _dbSet.IgnoreQueryFilters()
            .Where(r => r.Id == id && r.DeletedAt != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DeletedAt, (DateTime?)null)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
    }

    public async Task HardDeleteAsync(int id)
    {
        await _dbSet.IgnoreQueryFilters()
            .Where(r => r.Id == id && r.DeletedAt != null)
            .ExecuteDeleteAsync();
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(r => r.DeletedAt != null);
        if (count > 0)
        {
            await _dbSet.IgnoreQueryFilters().Where(r => r.DeletedAt != null).ExecuteDeleteAsync();
        }
        return count;
    }
}
