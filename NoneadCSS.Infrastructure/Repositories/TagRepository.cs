using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class TagRepository : Repository<Tag>, ITagRepository
{
    public TagRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Tag>> GetByMailboxIdAsync(int? mailboxId = null)
    {
        var query = _dbSet.AsNoTracking().Where(t => t.DeletedAt == null).AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(t => t.MailboxId == mailboxId.Value || t.MailboxId == null);
        }
        else
        {
            query = query.Where(t => t.MailboxId == null);
        }

        return await query
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<Tag?> GetByNameAsync(string name, int? mailboxId = null)
    {
        var query = _dbSet.AsNoTracking().Where(t => t.DeletedAt == null).AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(t => t.MailboxId == mailboxId.Value || t.MailboxId == null);
        }
        else
        {
            query = query.Where(t => t.MailboxId == null);
        }

        return await query.FirstOrDefaultAsync(t => t.Name == name);
    }

    public async Task<IEnumerable<Tag>> GetByConversationIdAsync(int conversationId)
    {
        return await _context.ConversationTags.AsNoTracking()
            .Where(ct => ct.ConversationId == conversationId && ct.Tag.DeletedAt == null)
            .Include(ct => ct.Tag)
            .Select(ct => ct.Tag)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Tag>> GetByIdsAsync(IEnumerable<int> ids)
    {
        return await _dbSet.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<(IEnumerable<Tag> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? query = null)
    {
        var queryable = _dbSet
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query))
        {
            queryable = queryable.Where(t => t.Name.Contains(query));
        }

        var total = await queryable.CountAsync();

        var items = await queryable
            .OrderByDescending(t => t.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().AsNoTracking().CountAsync(t => t.DeletedAt != null);
    }

    public async Task<bool> RestoreAsync(int id)
    {
        // 检查是否存在已删除的记录（绕过全局查询过滤器）
        var exists = await _dbSet.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.Id == id && t.DeletedAt != null);
        if (!exists) return false;

        // 直接执行 UPDATE，绕过变更跟踪器
        await _dbSet.IgnoreQueryFilters()
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.DeletedAt, (DateTime?)null)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow));
        return true;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        // 检查是否存在已删除的记录（绕过全局查询过滤器）
        var exists = await _dbSet.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(t => t.Id == id && t.DeletedAt != null);
        if (!exists) return false;

        // 先删除关联的会话-标签关联记录
        await _context.ConversationTags.Where(ct => ct.TagId == id).ExecuteDeleteAsync();
        // 再物理删除标签（绕过全局查询过滤器）
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
            // 获取所有已删除标签的 ID
            var ids = await _dbSet.IgnoreQueryFilters()
                .Where(t => t.DeletedAt != null)
                .Select(t => t.Id)
                .ToListAsync();

            // 先删除关联的会话-标签关联记录
            await _context.ConversationTags.Where(ct => ids.Contains(ct.TagId)).ExecuteDeleteAsync();
            // 再删除所有已软删除的标签（绕过全局查询过滤器）
            await _dbSet.IgnoreQueryFilters().Where(t => t.DeletedAt != null).ExecuteDeleteAsync();
        }
        return count;
    }

    /// <summary>
    /// 批量获取指定标签集合的会话使用计数。
    /// 单次 SQL（GROUP BY TagId COUNT(*)）即可取回所有计数，避免对每个标签单独查询。
    /// 仅返回存在关联记录的 TagId；未在结果中出现的 TagId 计数应视为 0。
    /// </summary>
    public async Task<Dictionary<int, int>> GetTagCountsAsync(IEnumerable<int> tagIds)
    {
        // 物化集合以避免多次枚举 IEnumerable，并支持空集合短路。
        var idList = tagIds as List<int> ?? tagIds.ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        return await _context.ConversationTags
            .Where(ct => idList.Contains(ct.TagId))
            .GroupBy(ct => ct.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count);
    }

    /// <summary>
    /// 获取单个标签的会话使用计数。翻译为 SQL COUNT(*) WHERE TagId = @tagId，
    /// 不再将整张 ConversationTags 表加载到内存。
    /// </summary>
    public async Task<int> GetTagCountAsync(int tagId)
    {
        return await _context.ConversationTags
            .CountAsync(ct => ct.TagId == tagId);
    }
}
