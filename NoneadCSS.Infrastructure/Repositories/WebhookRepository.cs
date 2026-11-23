using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class WebhookRepository : Repository<Webhook>, IWebhookRepository
{
    public WebhookRepository(NoneadCSSDbContext context) : base(context) { }

    private IQueryable<Webhook> ApplyIncludes(IQueryable<Webhook> query)
    {
        return query.Include(w => w.Mailbox);
    }

    public async Task<List<Webhook>> GetActiveForEventAsync(string eventType, int? mailboxId = null)
    {
        var query = _dbSet
            .AsNoTracking()
            .Where(w => w.IsActive && w.Events.Contains(eventType));

        if (mailboxId.HasValue)
        {
            query = query.Where(w => w.MailboxId == null || w.MailboxId == mailboxId.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// 原子递增 Webhook 触发统计。使用 ExecuteUpdateAsync 直发原子 UPDATE（不经过 ChangeTracker），
    /// 避免并发触发同一 webhook 时因 RowVersion 并发令牌不匹配抛出 DbUpdateConcurrencyException。
    /// </summary>
    public async Task IncrementTriggerStatsAsync(int webhookId, bool isSuccess, string? errorMessage, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await _dbSet
            .Where(w => w.Id == webhookId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.TriggerCount, w => w.TriggerCount + 1)
                .SetProperty(w => w.SuccessCount, w => isSuccess ? w.SuccessCount + 1 : w.SuccessCount)
                .SetProperty(w => w.FailCount, w => isSuccess ? w.FailCount : w.FailCount + 1)
                .SetProperty(w => w.LastTriggeredAt, now)
                // 成功时保留原 LastError，失败时写入错误信息（三元表达式翻译为 SQL CASE WHEN）
                .SetProperty(w => w.LastError, w => isSuccess ? w.LastError : errorMessage)
                .SetProperty(w => w.UpdatedAt, now), cancellationToken);
    }

    public async Task<(IEnumerable<Webhook> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? mailboxId = null, bool? isActive = null, string? keyword = null, string? eventType = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking()).AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(w => w.MailboxId == mailboxId.Value || w.MailboxId == null);
        }
        if (isActive.HasValue)
        {
            query = query.Where(w => w.IsActive == isActive.Value);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(w =>
                w.Name.Contains(keyword) ||
                w.Url.Contains(keyword));
        }
        if (!string.IsNullOrEmpty(eventType))
        {
            query = query.Where(w => w.Events.Contains(eventType));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(w => w.IsActive)
            .ThenByDescending(w => w.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<Webhook>> GetAllWithMailboxAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .OrderByDescending(w => w.Id)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        var total = await _dbSet.CountAsync(w => w.DeletedAt == null);
        var active = await _dbSet.CountAsync(w => w.DeletedAt == null && w.IsActive);
        var global = await _dbSet.CountAsync(w => w.DeletedAt == null && w.MailboxId == null);

        var totalTriggers = await _dbSet.Where(w => w.DeletedAt == null).SumAsync(w => (int?)w.TriggerCount) ?? 0;
        var totalSuccess = await _dbSet.Where(w => w.DeletedAt == null).SumAsync(w => (int?)w.SuccessCount) ?? 0;
        var totalFail = await _dbSet.Where(w => w.DeletedAt == null).SumAsync(w => (int?)w.FailCount) ?? 0;

        return new Dictionary<string, int>
        {
            { "total", total },
            { "active", active },
            { "inactive", total - active },
            { "global", global },
            { "mailboxSpecific", total - global },
            { "totalTriggers", totalTriggers },
            { "totalSuccess", totalSuccess },
            { "totalFail", totalFail },
        };
    }

    public async Task<Webhook?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .FirstOrDefaultAsync(w => w.Id == id && w.DeletedAt != null);
    }

    public async Task<(IEnumerable<Webhook> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(w => w.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(w =>
                w.Name.Contains(keyword) ||
                w.Url.Contains(keyword));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(w => w.DeletedAt)
            .ThenByDescending(w => w.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(w => w.DeletedAt != null);
    }

    public async Task RestoreAsync(int id)
    {
        await _dbSet.IgnoreQueryFilters()
            .Where(w => w.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.DeletedAt, (DateTime?)null)
                .SetProperty(w => w.UpdatedAt, DateTime.UtcNow));
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(w => w.Id == id && w.DeletedAt != null);
        if (!exists) return false;
        await _context.WebhookLogs.IgnoreQueryFilters()
            .Where(l => l.WebhookId == id)
            .ExecuteDeleteAsync();
        await _dbSet.IgnoreQueryFilters()
            .Where(w => w.Id == id)
            .ExecuteDeleteAsync();
        return true;
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var deletedIds = await _dbSet.IgnoreQueryFilters()
            .Where(w => w.DeletedAt != null)
            .Select(w => w.Id)
            .ToListAsync();

        if (deletedIds.Count > 0)
        {
            await _context.WebhookLogs.IgnoreQueryFilters()
                .Where(l => deletedIds.Contains(l.WebhookId))
                .ExecuteDeleteAsync();
        }

        return await _dbSet.IgnoreQueryFilters()
            .Where(w => w.DeletedAt != null)
            .ExecuteDeleteAsync();
    }
}

public class WebhookLogRepository : Repository<WebhookLog>, IWebhookLogRepository
{
    public WebhookLogRepository(NoneadCSSDbContext context) : base(context) { }

    public async Task<List<WebhookLog>> GetByWebhookAsync(int webhookId, int limit = 50)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(l => l.WebhookId == webhookId)
            .OrderByDescending(l => l.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<(IEnumerable<WebhookLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? webhookId = null, string? eventType = null, bool? isSuccess = null)
    {
        var query = _dbSet.AsNoTracking().AsQueryable();

        if (webhookId.HasValue)
        {
            query = query.Where(l => l.WebhookId == webhookId.Value);
        }
        if (!string.IsNullOrEmpty(eventType))
        {
            query = query.Where(l => l.EventType == eventType);
        }
        if (isSuccess.HasValue)
        {
            query = query.Where(l => l.IsSuccess == isSuccess.Value);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Dictionary<string, int>> GetStatsAsync(DateTime? start = null, DateTime? end = null)
    {
        var query = _dbSet.AsNoTracking().AsQueryable();

        if (start.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= start.Value);
        }
        if (end.HasValue)
        {
            query = query.Where(l => l.CreatedAt <= end.Value);
        }

        var total = await query.CountAsync();
        var success = await query.CountAsync(l => l.IsSuccess);
        var fail = total - success;

        return new Dictionary<string, int>
        {
            { "total", total },
            { "success", success },
            { "fail", fail },
        };
    }
}
