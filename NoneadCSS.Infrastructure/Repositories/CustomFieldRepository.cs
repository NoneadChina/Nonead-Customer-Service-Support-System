using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class CustomFieldRepository : Repository<CustomField>, ICustomFieldRepository
{
    public CustomFieldRepository(NoneadCSSDbContext context) : base(context) { }

    private IQueryable<CustomField> ApplyIncludes(IQueryable<CustomField> query)
    {
        return query.Include(f => f.Mailbox);
    }

    public async Task<List<CustomField>> GetActiveForEntityAsync(string entityType, string? sourceType = null)
    {
        var query = _dbSet
            .Where(f => f.IsActive && f.EntityType == entityType);

        // 按渠道过滤：sourceType 非空时只返回全局字段（SourceTypesJson 为 null/空）+ 包含该渠道的字段
        // sourceType 为 null/空时不做渠道过滤，返回所有字段（用于字段管理页等不限定场景）
        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            query = query.Where(f =>
                f.SourceTypesJson == null
                || f.SourceTypesJson == ""
                || f.SourceTypesJson.Contains("\"" + sourceType + "\""));
        }

        return await query
            // 全局字段优先（更稳定），随后按 sortOrder、id
            .OrderByDescending(f => (f.SourceTypesJson == null || f.SourceTypesJson == "") ? 1 : 0)
            .ThenBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .ToListAsync();
    }

    public async Task<(IEnumerable<CustomField> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        string? entityType = null, int? mailboxId = null, bool? isActive = null, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet).AsQueryable();

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(f => f.EntityType == entityType);
        }
        if (mailboxId.HasValue)
        {
            query = query.Where(f => f.MailboxId == mailboxId.Value || f.MailboxId == null);
        }
        if (isActive.HasValue)
        {
            query = query.Where(f => f.IsActive == isActive.Value);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(f =>
                f.Name.Contains(keyword) ||
                (f.HelpText != null && f.HelpText.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(f => f.EntityType)
            .ThenByDescending(f => f.MailboxId.HasValue ? 1 : 0)
            .ThenBy(f => f.SortOrder)
            .ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<CustomField>> GetAllWithMailboxAsync()
    {
        return await ApplyIncludes(_dbSet)
            .OrderBy(f => f.EntityType)
            .ThenBy(f => f.SortOrder)
            .ThenByDescending(f => f.Id)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        var total = await _dbSet.CountAsync(f => f.DeletedAt == null);
        var active = await _dbSet.CountAsync(f => f.DeletedAt == null && f.IsActive);
        var conversationFields = await _dbSet.CountAsync(f => f.DeletedAt == null && f.EntityType == "conversation");
        var customerFields = await _dbSet.CountAsync(f => f.DeletedAt == null && f.EntityType == "customer");
        var globalFields = await _dbSet.CountAsync(f => f.DeletedAt == null && f.MailboxId == null);

        return new Dictionary<string, int>
        {
            { "total", total },
            { "active", active },
            { "inactive", total - active },
            { "conversationFields", conversationFields },
            { "customerFields", customerFields },
            { "globalFields", globalFields },
        };
    }

    public async Task<CustomField?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .FirstOrDefaultAsync(f => f.Id == id && f.DeletedAt != null);
    }

    public async Task<(IEnumerable<CustomField> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .Where(f => f.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(f =>
                f.Name.Contains(keyword) ||
                (f.HelpText != null && f.HelpText.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => f.DeletedAt)
            .ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(f => f.DeletedAt != null);
    }

    public async Task RestoreAsync(int id)
    {
        await _dbSet.IgnoreQueryFilters()
            .Where(f => f.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.DeletedAt, (DateTime?)null)
                .SetProperty(f => f.UpdatedAt, DateTime.UtcNow));
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(f => f.Id == id && f.DeletedAt != null);
        if (!exists) return false;
        await _context.CustomFieldValues.IgnoreQueryFilters()
            .Where(v => v.CustomFieldId == id)
            .ExecuteDeleteAsync();
        await _dbSet.IgnoreQueryFilters()
            .Where(f => f.Id == id)
            .ExecuteDeleteAsync();
        return true;
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var deletedIds = await _dbSet.IgnoreQueryFilters()
            .Where(f => f.DeletedAt != null)
            .Select(f => f.Id)
            .ToListAsync();

        if (deletedIds.Count > 0)
        {
            await _context.CustomFieldValues.IgnoreQueryFilters()
                .Where(v => deletedIds.Contains(v.CustomFieldId))
                .ExecuteDeleteAsync();
        }

        return await _dbSet.IgnoreQueryFilters()
            .Where(f => f.DeletedAt != null)
            .ExecuteDeleteAsync();
    }
}

public class CustomFieldValueRepository : Repository<CustomFieldValue>, ICustomFieldValueRepository
{
    public CustomFieldValueRepository(NoneadCSSDbContext context) : base(context) { }

    public async Task<List<CustomFieldValue>> GetByEntityAsync(string entityType, int entityId)
    {
        return await _dbSet
            .Where(v => v.EntityType == entityType && v.EntityId == entityId)
            .Include(v => v.CustomField)
            .OrderBy(v => v.CustomField!.SortOrder)
            .ThenBy(v => v.Id)
            .ToListAsync();
    }

    public async Task<Dictionary<int, List<CustomFieldValue>>> GetByEntityIdsAsync(
        string entityType, IEnumerable<int> entityIds)
    {
        var ids = entityIds.ToList();
        if (ids.Count == 0) return new Dictionary<int, List<CustomFieldValue>>();

        var values = await _dbSet
            .Where(v => v.EntityType == entityType && ids.Contains(v.EntityId))
            .Include(v => v.CustomField)
            .ToListAsync();

        return values
            .GroupBy(v => v.EntityId)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.CustomField?.SortOrder).ToList());
    }

    public async Task<CustomFieldValue?> GetByFieldAndEntityAsync(
        int customFieldId, string entityType, int entityId)
    {
        return await _dbSet
            .FirstOrDefaultAsync(v =>
                v.CustomFieldId == customFieldId &&
                v.EntityType == entityType &&
                v.EntityId == entityId);
    }

    public async Task DeleteByEntityAsync(string entityType, int entityId)
    {
        var values = await _dbSet
            .Where(v => v.EntityType == entityType && v.EntityId == entityId)
            .ToListAsync();

        _dbSet.RemoveRange(values);
    }
}
