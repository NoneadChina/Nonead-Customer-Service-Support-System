using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class CannedResponseRepository : Repository<CannedResponse>, ICannedResponseRepository
{
    public CannedResponseRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 应用标准的 Include 链，包含关联的邮箱和创建用户信息。
    /// </summary>
    private IQueryable<CannedResponse> ApplyIncludes(IQueryable<CannedResponse> query)
    {
        return query
            .Include(c => c.Mailbox)
            .Include(c => c.CreatedByUser);
    }

    public async Task<List<CannedResponse>> GetByMailboxAsync(int? mailboxId, int? userId = null)
    {
        // 仅查询未软删除的记录
        var query = ApplyIncludes(_dbSet.Where(c => c.DeletedAt == null)).AsQueryable();

        // 邮箱范围：指定邮箱时返回该邮箱 + 全局（MailboxId == null）；未指定时返回全部
        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value || c.MailboxId == null);
        }

        // 可见性：共享模板 + 当前用户的个人模板
        if (userId.HasValue)
        {
            query = query.Where(c => c.IsShared || c.CreatedByUserId == userId.Value);
        }
        else
        {
            query = query.Where(c => c.IsShared);
        }

        // 防御性限制：单次最多返回 500 条，防止极端场景下海量模板拖垮查询与序列化
        return await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Take(500)
            .ToListAsync();
    }

    public async Task<List<CannedResponse>> GetByCategoryAsync(string category, int? mailboxId = null)
    {
        // 仅查询未软删除的记录
        var query = _dbSet.Where(c => c.Category == category && c.DeletedAt == null);

        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value || c.MailboxId == null);
        }

        return await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<List<CannedResponse>> SearchAsync(string keyword, int? mailboxId = null, int? userId = null)
    {
        // 仅查询未软删除的记录
        var query = _dbSet.Where(c => c.DeletedAt == null).AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value || c.MailboxId == null);
        }

        if (userId.HasValue)
        {
            query = query.Where(c => c.IsShared || c.CreatedByUserId == userId.Value);
        }
        else
        {
            query = query.Where(c => c.IsShared);
        }

        keyword = keyword.Trim();
        query = query.Where(c =>
            c.Name.Contains(keyword) ||
            c.Body.Contains(keyword) ||
            (c.Category != null && c.Category.Contains(keyword)));

        return await query
            .OrderBy(c => c.Name)
            .Take(50)
            .ToListAsync();
    }

    public async Task<List<string>> GetAllCategoriesAsync(int? mailboxId = null)
    {
        // 仅查询未软删除的记录
        var query = _dbSet.Where(c => c.DeletedAt == null).AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value || c.MailboxId == null);
        }

        return await query
            .Where(c => c.Category != null && c.Category != string.Empty)
            .Select(c => c.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<(IEnumerable<CannedResponse> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null)
    {
        // 查询已软删除的记录（绕过全局查询过滤器）
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .Where(c => c.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(c =>
                c.Name.Contains(keyword) ||
                c.Body.Contains(keyword) ||
                (c.Category != null && c.Category.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.DeletedAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(c => c.DeletedAt != null);
    }

    /// <inheritdoc />
    public async Task<CannedResponse?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt != null);
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(int id)
    {
        // 检查是否存在已删除的记录（绕过全局查询过滤器）
        var exists = await _dbSet.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.Id == id && c.DeletedAt != null);
        if (!exists) return false;

        // 直接执行 UPDATE，绕过变更跟踪器
        await _dbSet.IgnoreQueryFilters()
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.DeletedAt, (DateTime?)null)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> HardDeleteAsync(int id)
    {
        // 检查是否存在已删除的记录（绕过全局查询过滤器）
        var exists = await _dbSet.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.Id == id && c.DeletedAt != null);
        if (!exists) return false;

        // 物理删除（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters()
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync();
        return true;
    }

    /// <inheritdoc />
    public async Task<int> HardDeleteAllDeletedAsync()
    {
        // 统计待删除数量（绕过全局查询过滤器）
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(c => c.DeletedAt != null);
        if (count > 0)
        {
            // 物理删除所有已软删除的记录（绕过全局查询过滤器）
            await _dbSet.IgnoreQueryFilters()
                .Where(c => c.DeletedAt != null)
                .ExecuteDeleteAsync();
        }
        return count;
    }
}
