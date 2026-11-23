using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class MailTemplateRepository : Repository<MailTemplate>, IMailTemplateRepository
{
    public MailTemplateRepository(NoneadCSSDbContext context) : base(context) { }

    private IQueryable<MailTemplate> ApplyIncludes(IQueryable<MailTemplate> query)
    {
        return query.Include(t => t.Mailbox);
    }

    public async Task<List<MailTemplate>> GetByTypeAsync(MailTemplateType type)
    {
        return await ApplyIncludes(_dbSet)
            .Where(t => t.Type == type && t.DeletedAt == null)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<List<MailTemplate>> GetByMailboxIdAsync(int? mailboxId)
    {
        var query = ApplyIncludes(_dbSet).Where(t => t.DeletedAt == null).AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(t => t.MailboxId == null || t.MailboxId == mailboxId.Value);
        }

        return await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<MailTemplate?> GetBySlugAsync(string slug)
    {
        return await ApplyIncludes(_dbSet)
            .FirstOrDefaultAsync(t => t.Slug == slug && t.DeletedAt == null);
    }

    public async Task<List<MailTemplate>> GetActiveByTypeAndMailboxAsync(MailTemplateType type, int? mailboxId)
    {
        var query = ApplyIncludes(_dbSet)
            .Where(t => t.Type == type && t.IsActive && t.DeletedAt == null);

        if (mailboxId.HasValue)
        {
            query = query.Where(t => t.MailboxId == null || t.MailboxId == mailboxId.Value);
        }

        return await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<(IEnumerable<MailTemplate> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        MailTemplateType? type = null, int? mailboxId = null, bool? isActive = null, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet).Where(t => t.DeletedAt == null).AsQueryable();

        if (type.HasValue)
        {
            query = query.Where(t => t.Type == type.Value);
        }
        if (mailboxId.HasValue)
        {
            query = query.Where(t => t.MailboxId == null || t.MailboxId == mailboxId.Value);
        }
        if (isActive.HasValue)
        {
            query = query.Where(t => t.IsActive == isActive.Value);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(t =>
                t.Name.Contains(keyword) ||
                t.Subject.Contains(keyword) ||
                t.Slug.Contains(keyword));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        var activeQuery = _dbSet.Where(t => t.DeletedAt == null);
        var total = await activeQuery.CountAsync();
        var active = await activeQuery.CountAsync(t => t.IsActive);
        var system = await activeQuery.CountAsync(t => t.IsSystem);
        var custom = total - system;

        var typeCounts = await activeQuery
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Type, g => g.Count);

        var result = new Dictionary<string, int>
        {
            { "total", total },
            { "active", active },
            { "inactive", total - active },
            { "system", system },
            { "custom", custom }
        };

        foreach (var type in Enum.GetValues<MailTemplateType>())
        {
            result[$"type_{(int)type}"] = typeCounts.GetValueOrDefault(type, 0);
        }

        return result;
    }

    public async Task<(IEnumerable<MailTemplate> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null)
    {
        // 查询已软删除的记录（绕过全局查询过滤器）
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .Where(t => t.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(t =>
                t.Name.Contains(keyword) ||
                t.Subject.Contains(keyword) ||
                t.Slug.Contains(keyword));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.DeletedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(t => t.DeletedAt != null);
    }

    public async Task<bool> RestoreAsync(int id)
    {
        // 获取已删除的模板（绕过全局查询过滤器）
        var template = await _dbSet.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt != null);
        if (template == null) return false;

        // 检查 slug 是否冲突（绕过全局查询过滤器检查所有同名 slug 记录）
        var slugConflict = await _dbSet.IgnoreQueryFilters()
            .AnyAsync(t => t.Slug == template.Slug && t.Id != id);
        var newSlug = slugConflict
            ? $"{template.Slug}-restored-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : template.Slug;

        // 直接执行 UPDATE，绕过变更跟踪器
        await _dbSet.IgnoreQueryFilters()
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.DeletedAt, (DateTime?)null)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow)
                .SetProperty(t => t.Slug, newSlug));
        return true;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        // 检查是否存在已删除的记录（绕过全局查询过滤器）
        var exists = await _dbSet.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.Id == id && t.DeletedAt != null);
        if (!exists) return false;

        // 物理删除（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters()
            .Where(t => t.Id == id)
            .ExecuteDeleteAsync();
        return true;
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        // 统计待删除数量（绕过全局查询过滤器）
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(t => t.DeletedAt != null);
        if (count > 0)
        {
            // 物理删除所有已软删除的记录（绕过全局查询过滤器）
            await _dbSet.IgnoreQueryFilters()
                .Where(t => t.DeletedAt != null)
                .ExecuteDeleteAsync();
        }
        return count;
    }
}
