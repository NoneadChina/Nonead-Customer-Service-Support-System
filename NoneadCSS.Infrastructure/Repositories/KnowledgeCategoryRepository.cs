using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Infrastructure.Repositories;

public class KnowledgeCategoryRepository : IKnowledgeCategoryRepository
{
    private readonly NoneadCSSDbContext _context;

    public KnowledgeCategoryRepository(NoneadCSSDbContext context)
    {
        _context = context;
    }

    private IQueryable<KnowledgeCategory> ApplyIncludes(IQueryable<KnowledgeCategory> query)
    {
        return query.Include(c => c.Children);
    }

    public async Task<KnowledgeCategory?> GetByIdAsync(int id)
    {
        return await ApplyIncludes(_context.KnowledgeCategories)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<IReadOnlyList<KnowledgeCategory>> GetListAsync(int? parentId = null, int? mailboxId = null, bool? isActive = null, string? keyword = null)
    {
        var query = BuildQuery(parentId, mailboxId, isActive, keyword);
        return await query.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync();
    }

    public async Task<IReadOnlyList<KnowledgeCategory>> GetAllAsync()
    {
        return await _context.KnowledgeCategories
            .Where(c => c.DeletedAt == null)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToListAsync();
    }

    public async Task<int> CountAsync(int? parentId = null, int? mailboxId = null, bool? isActive = null, string? keyword = null)
    {
        var query = BuildQuery(parentId, mailboxId, isActive, keyword);
        return await query.CountAsync();
    }

    public async Task AddAsync(KnowledgeCategory category)
    {
        await _context.KnowledgeCategories.AddAsync(category);
    }

    /// <summary>
    /// ⚠️ 反模式：推荐改用 tracked 查询（默认 tracking，不带 AsNoTracking）+ 直接修改属性后 SaveChangesAsync，
    /// 避免全字段 UPDATE 以及对象图附加导致的跟踪冲突。
    /// </summary>
    [Obsolete("推荐改用 tracked 查询 + 直接修改属性")]
    public void Update(KnowledgeCategory category)
    {
        _context.KnowledgeCategories.Update(category);
    }

    public void Remove(KnowledgeCategory category)
    {
        _context.KnowledgeCategories.Remove(category);
    }

    public async Task<int> CountArticlesAsync(int categoryId)
    {
        return await _context.KnowledgeArticles.CountAsync(a => a.DeletedAt == null && a.CategoryId == categoryId);
    }

    /// <summary>
    /// 按 CategoryId 列表批量获取文章数（GROUP BY 聚合，避免 N+1 查询）
    /// </summary>
    public async Task<Dictionary<int, int>> GetArticleCountsByCategoryIdsAsync(IEnumerable<int> categoryIds)
    {
        var idList = categoryIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        return await _context.KnowledgeArticles
            .Where(a => a.DeletedAt == null && idList.Contains(a.CategoryId ?? 0))
            .GroupBy(a => a.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);
    }

    /// <summary>
    /// 将指定分类下未删除文章的 CategoryId 置 null（解除关联）。
    /// 用于删除分类前清理活跃文章引用，防止悬空外键导致权限绕过。
    /// </summary>
    public async Task UnlinkArticlesOfCategoryAsync(int categoryId)
    {
        await _context.KnowledgeArticles
            .Where(a => a.CategoryId == categoryId && a.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.CategoryId, (int?)null)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow));
    }

    private IQueryable<KnowledgeCategory> BuildQuery(int? parentId, int? mailboxId, bool? isActive, string? keyword)
    {
        var query = _context.KnowledgeCategories.Where(c => c.DeletedAt == null).AsQueryable();
        if (parentId.HasValue)
        {
            query = query.Where(c => c.ParentId == parentId.Value);
        }
        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value);
        }
        if (isActive.HasValue)
        {
            query = query.Where(c => c.IsActive == isActive.Value);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(kw) || (c.Description != null && c.Description.ToLower().Contains(kw)));
        }
        return query;
    }

    // ====== 垃圾箱 ======

    public async Task<KnowledgeCategory?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_context.KnowledgeCategories.IgnoreQueryFilters().AsNoTracking())
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt != null);
    }

    public async Task<(IReadOnlyList<KnowledgeCategory> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = _context.KnowledgeCategories
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(kw) || (c.Description != null && c.Description.ToLower().Contains(kw)));
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

    public async Task<int> CountDeletedAsync()
    {
        return await _context.KnowledgeCategories.IgnoreQueryFilters().CountAsync(c => c.DeletedAt != null);
    }

    public async Task RestoreAsync(int id)
    {
        await _context.KnowledgeCategories.IgnoreQueryFilters()
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.DeletedAt, (DateTime?)null)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
    }

    /// <summary>
    /// 恢复已删除分类，同时清空其 ParentId（用于父分类已删除的场景）。
    /// 通过单条 ExecuteUpdateAsync 原子完成，避免 tracked 实体修改+ExecuteUpdate 双写不一致问题。
    /// </summary>
    public async Task RestoreWithParentResetAsync(int id)
    {
        await _context.KnowledgeCategories.IgnoreQueryFilters()
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.DeletedAt, (DateTime?)null)
                .SetProperty(c => c.ParentId, (int?)null)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
    }

    public async Task HardDeleteAsync(int id)
    {
        // 硬删除分类前先解除该分类下所有文章（含未删除）的关联，防止悬空 CategoryId
        await _context.KnowledgeArticles.IgnoreQueryFilters()
            .Where(a => a.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CategoryId, (int?)null));

        await _context.KnowledgeCategories.IgnoreQueryFilters()
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync();
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var deletedIds = await _context.KnowledgeCategories.IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null)
            .Select(c => c.Id)
            .ToListAsync();

        if (deletedIds.Count == 0) return 0;

        // 先解除所有引用这些分类的文章关联（含活跃文章），防止悬空外键
        await _context.KnowledgeArticles.IgnoreQueryFilters()
            .Where(a => deletedIds.Contains(a.CategoryId ?? 0))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CategoryId, (int?)null));

        return await _context.KnowledgeCategories.IgnoreQueryFilters()
            .Where(c => deletedIds.Contains(c.Id))
            .ExecuteDeleteAsync();
    }
}
