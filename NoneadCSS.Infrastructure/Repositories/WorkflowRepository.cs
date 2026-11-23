using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class WorkflowRepository : Repository<Workflow>, IWorkflowRepository
{
    public WorkflowRepository(NoneadCSSDbContext context) : base(context) { }

    private IQueryable<Workflow> ApplyIncludes(IQueryable<Workflow> query)
    {
        return query.Include(w => w.Mailbox);
    }

    public async Task<List<Workflow>> GetActiveByMailboxAsync(int mailboxId, string triggerType)
    {
        return await _dbSet.AsNoTracking()
            .Where(w => w.DeletedAt == null && w.IsActive && w.TriggerType == triggerType && (w.MailboxId == null || w.MailboxId == mailboxId))
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Id)
            .ToListAsync();
    }

    public async Task<(IEnumerable<Workflow> Items, int Total)> GetPagedAsync(
        int page, int pageSize, int? mailboxId = null, bool? isActive = null,
        string? triggerType = null, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking())
            .Where(w => w.DeletedAt == null)
            .AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(w => w.MailboxId == mailboxId.Value);
        }
        if (isActive.HasValue)
        {
            query = query.Where(w => w.IsActive == isActive.Value);
        }
        if (!string.IsNullOrEmpty(triggerType))
        {
            query = query.Where(w => w.TriggerType == triggerType);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(w => w.Name.Contains(keyword) || (w.Description != null && w.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IEnumerable<Workflow> Items, int Total)> GetDeletedPagedAsync(
        int page, int pageSize, int? mailboxId = null, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(w => w.DeletedAt != null)
            .AsQueryable();

        if (mailboxId.HasValue)
        {
            query = query.Where(w => w.MailboxId == mailboxId.Value);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(w => w.Name.Contains(keyword) || (w.Description != null && w.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(w => w.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> GetDeletedCountAsync(int? mailboxId = null)
    {
        var query = _dbSet.IgnoreQueryFilters().AsNoTracking().Where(w => w.DeletedAt != null).AsQueryable();
        if (mailboxId.HasValue)
        {
            query = query.Where(w => w.MailboxId == mailboxId.Value);
        }
        return await query.CountAsync();
    }

    public async Task<Workflow?> GetDeletedByIdAsync(int id)
    {
        // 不使用 AsNoTracking，返回追踪实体，便于 RestoreAsync 修改属性后直接 SaveChangesAsync 持久化
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .FirstOrDefaultAsync(w => w.Id == id && w.DeletedAt != null);
    }

    public async Task<int> HardDeleteAllDeletedAsync(int? mailboxId = null)
    {
        // 统计待删除数量（绕过全局查询过滤器）
        var countQuery = _dbSet.IgnoreQueryFilters().Where(w => w.DeletedAt != null);
        if (mailboxId.HasValue)
        {
            countQuery = countQuery.Where(w => w.MailboxId == mailboxId.Value);
        }
        var count = await countQuery.CountAsync();
        if (count == 0) return 0;

        // 获取所有已删除工作流的 ID
        var ids = await countQuery.Select(w => w.Id).ToListAsync();

        // 先删除关联的执行日志（外键依赖）
        await _context.Set<WorkflowExecutionLog>()
            .Where(l => ids.Contains(l.WorkflowId))
            .ExecuteDeleteAsync();
        // 再删除所有已软删除的工作流（绕过全局查询过滤器）
        await countQuery.ExecuteDeleteAsync();

        return count;
    }

    public async Task HardDeleteByIdAsync(int id)
    {
        // 确认存在已删除的工作流（绕过全局查询过滤器）
        var exists = await _dbSet.IgnoreQueryFilters()
            .AnyAsync(w => w.Id == id && w.DeletedAt != null);
        if (!exists) return;

        // 先删除关联的执行日志（外键依赖）
        await _context.Set<WorkflowExecutionLog>()
            .Where(l => l.WorkflowId == id)
            .ExecuteDeleteAsync();
        // 再删除工作流本身（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters().Where(w => w.Id == id).ExecuteDeleteAsync();
    }

    public async Task<List<WorkflowExecutionLog>> GetExecutionLogsAsync(int workflowId, int limit = 50)
    {
        return await _context.Set<WorkflowExecutionLog>().AsNoTracking()
            .Where(l => l.WorkflowId == workflowId)
            .Include(l => l.Conversation)
            .OrderByDescending(l => l.ExecutedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<(IEnumerable<WorkflowExecutionLog> Items, int Total)> GetExecutionLogsPagedAsync(
        int page, int pageSize, int? workflowId = null, int? conversationId = null,
        string? status = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _context.Set<WorkflowExecutionLog>().AsNoTracking()
            .Include(l => l.Workflow)
            .Include(l => l.Conversation)
            .AsQueryable();

        if (workflowId.HasValue) query = query.Where(l => l.WorkflowId == workflowId.Value);
        if (conversationId.HasValue) query = query.Where(l => l.ConversationId == conversationId.Value);
        if (!string.IsNullOrEmpty(status)) query = query.Where(l => l.Status == status);
        if (startDate.HasValue) query = query.Where(l => l.ExecutedAt >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(l => l.ExecutedAt < endExclusive);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.ExecutedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task AddExecutionLogAsync(WorkflowExecutionLog log)
    {
        await _context.Set<WorkflowExecutionLog>().AddAsync(log);
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        var total = await _dbSet.AsNoTracking().CountAsync(w => w.DeletedAt == null);
        var active = await _dbSet.AsNoTracking().CountAsync(w => w.DeletedAt == null && w.IsActive);
        var deleted = await _dbSet.IgnoreQueryFilters().AsNoTracking().CountAsync(w => w.DeletedAt != null);
        var executed = await _context.Set<WorkflowExecutionLog>().AsNoTracking().CountAsync();
        var succeeded = await _context.Set<WorkflowExecutionLog>().AsNoTracking().CountAsync(l => l.Status == "success");

        return new Dictionary<string, int>
        {
            { "total", total },
            { "active", active },
            { "inactive", total - active },
            { "deleted", deleted },
            { "executions", executed },
            { "succeeded", succeeded },
            { "failed", executed - succeeded }
        };
    }

    public async Task<List<Workflow>> GetAllWithMailboxAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(w => w.DeletedAt == null)
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.Id)
            .ToListAsync();
    }
}
