using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class MacroRepository : IMacroRepository
{
    private readonly NoneadCSSDbContext _context;
    private readonly DbSet<Macro> _dbSet;

    public MacroRepository(NoneadCSSDbContext context)
    {
        _context = context;
        _dbSet = context.Macros;
    }

    private IQueryable<Macro> ApplyIncludes(IQueryable<Macro> query)
    {
        return query.Include(m => m.Mailbox);
    }

    public async Task<Macro?> GetByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null);
    }

    /// <summary>
    /// 按 ID 查询宏（tracking，含 Mailbox Include），适用于后续修改 + SaveChanges 的场景。
    /// 与 GetByIdAsync 不同，本方法保留变更跟踪（不使用 AsNoTracking），保留单独方法以与服务层约定对齐。
    /// </summary>
    public async Task<Macro?> GetByIdTrackedAsync(int id)
    {
        return await ApplyIncludes(_dbSet)
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null);
    }

    public async Task<List<Macro>> GetByMailboxAsync(int? mailboxId)
    {
        var query = _dbSet
            .AsNoTracking()
            .Where(m => m.IsActive && m.DeletedAt == null);

        if (mailboxId.HasValue)
        {
            query = query.Where(m => m.MailboxId == mailboxId || m.MailboxId == null);
        }

        return await query
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<(List<Macro> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        int? mailboxId = null,
        bool? isActive = null,
        string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking())
            .Where(m => m.DeletedAt == null)
            .AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(m => m.MailboxId == mailboxId);
        }

        if (isActive.HasValue)
        {
            query = query.Where(m => m.IsActive == isActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(m =>
                m.Name.Contains(keyword) ||
                (m.Description != null && m.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(m => m.UsageCount)
            .ThenBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> GetCountAsync(int? mailboxId = null, bool? isActive = null)
    {
        var query = _dbSet.Where(m => m.DeletedAt == null).AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(m => m.MailboxId == mailboxId);
        }

        if (isActive.HasValue)
        {
            query = query.Where(m => m.IsActive == isActive.Value);
        }

        return await query.CountAsync();
    }

    public async Task AddAsync(Macro macro)
    {
        await _dbSet.AddAsync(macro);
    }

    public async Task UpdateAsync(Macro macro)
    {
        macro.UpdatedAt = DateTime.UtcNow;
        // macro 应由调用方通过 tracked 查询获取，由 SaveChangesAsync 持久化，无需 _dbSet.Update
    }

    public async Task DeleteAsync(int id)
    {
        var macro = await _dbSet.FindAsync(id);
        if (macro != null)
        {
            _dbSet.Remove(macro);
        }
    }

    // ====== 垃圾箱 ======
    public async Task<(List<Macro> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(m => m.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(m =>
                m.Name.Contains(keyword) ||
                (m.Description != null && m.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(m => m.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(m => m.DeletedAt != null);
    }

    public async Task SoftDeleteAsync(int id)
    {
        var macro = await _dbSet.FindAsync(id);
        if (macro != null)
        {
            macro.DeletedAt = DateTime.UtcNow;
            macro.IsActive = false;
            // macro 已通过 FindAsync 查询（tracking），由 SaveChangesAsync 持久化，无需 _dbSet.Update
        }
    }

    public async Task RestoreAsync(int id)
    {
        // 直接执行 UPDATE，绕过全局查询过滤器和变更跟踪器
        await _dbSet.IgnoreQueryFilters()
            .Where(m => m.Id == id && m.DeletedAt != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.DeletedAt, (DateTime?)null)
                .SetProperty(m => m.UpdatedAt, DateTime.UtcNow));
    }

    public async Task HardDeleteAsync(int id)
    {
        // 确认存在已删除的宏（绕过全局查询过滤器）
        var exists = await _dbSet.IgnoreQueryFilters()
            .AnyAsync(m => m.Id == id && m.DeletedAt != null);
        if (!exists) return;

        // 先删除关联的执行日志（外键依赖）
        await _context.MacroExecutionLogs.Where(l => l.MacroId == id).ExecuteDeleteAsync();
        // 再删除宏本身（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters().Where(m => m.Id == id).ExecuteDeleteAsync();
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        // 统计待删除数量（绕过全局查询过滤器）
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(m => m.DeletedAt != null);
        if (count == 0) return 0;

        // 获取所有已删除宏的 ID
        var ids = await _dbSet.IgnoreQueryFilters()
            .Where(m => m.DeletedAt != null)
            .Select(m => m.Id)
            .ToListAsync();

        // 先删除关联的执行日志（外键依赖）
        await _context.MacroExecutionLogs.Where(l => ids.Contains(l.MacroId)).ExecuteDeleteAsync();
        // 再删除所有已软删除的宏（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters().Where(m => m.DeletedAt != null).ExecuteDeleteAsync();

        return count;
    }
}

public class MacroExecutionLogRepository : IMacroExecutionLogRepository
{
    private readonly NoneadCSSDbContext _context;
    private readonly DbSet<MacroExecutionLog> _dbSet;

    public MacroExecutionLogRepository(NoneadCSSDbContext context)
    {
        _context = context;
        _dbSet = context.MacroExecutionLogs;
    }

    private IQueryable<MacroExecutionLog> ApplyIncludes(IQueryable<MacroExecutionLog> query)
    {
        return query
            .Include(l => l.Macro)
            .Include(l => l.Conversation)
            .Include(l => l.ExecutedByUser);
    }

    public async Task<MacroExecutionLog?> GetByIdAsync(int id, bool includeDetails = false)
    {
        // includeDetails 为 false 时不调用 ApplyIncludes，避免无谓的 Include 关联加载
        var query = includeDetails ? ApplyIncludes(_dbSet.AsNoTracking()) : _dbSet.AsNoTracking();
        return await query.FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<(List<MacroExecutionLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        int? macroId = null,
        int? conversationId = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking()).AsQueryable();

        if (macroId.HasValue)
        {
            query = query.Where(l => l.MacroId == macroId.Value);
        }

        if (conversationId.HasValue)
        {
            query = query.Where(l => l.ConversationId == conversationId.Value);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.ExecutedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<MacroExecutionLog>> GetRecentAsync(int count)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .OrderByDescending(l => l.ExecutedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task AddAsync(MacroExecutionLog log)
    {
        await _dbSet.AddAsync(log);
    }

    public async Task<int> CountAsync()
    {
        return await _dbSet.CountAsync();
    }

    public async Task<int> CountTodayAsync()
    {
        var today = DateTime.UtcNow.Date;
        return await _dbSet.CountAsync(l => l.ExecutedAt.Date == today);
    }
}
