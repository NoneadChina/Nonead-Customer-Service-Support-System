using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class SlaPolicyRepository : Repository<SlaPolicy>, ISlaPolicyRepository
{
    public SlaPolicyRepository(NoneadCSSDbContext context) : base(context) { }

    private IQueryable<SlaPolicy> ApplyIncludes(IQueryable<SlaPolicy> query)
    {
        return query.Include(p => p.Mailbox);
    }

    public async Task<List<SlaPolicy>> GetActiveForMailboxAsync(int mailboxId)
    {
        return await _dbSet.AsNoTracking()
            .Where(p => p.DeletedAt == null && p.IsActive && (p.MailboxId == null || p.MailboxId == mailboxId))
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<SlaPolicy?> GetPolicyForConversationAsync(int mailboxId)
    {
        // 优先匹配邮箱专属策略，其次匹配全局策略
        return await _dbSet.AsNoTracking()
            .Where(p => p.DeletedAt == null && p.IsActive && (p.MailboxId == mailboxId || p.MailboxId == null))
            .OrderByDescending(p => p.MailboxId.HasValue ? 1 : 0)
            .ThenByDescending(p => p.Priority)
            .ThenBy(p => p.SortOrder)
            .FirstOrDefaultAsync();
    }

    public async Task<Dictionary<int, SlaPolicy>> GetPoliciesForMailboxesAsync(IEnumerable<int> mailboxIds)
    {
        var idList = mailboxIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<int, SlaPolicy>();
        }

        // 一次性查询所有匹配的策略：指定邮箱的策略 + 全局策略（MailboxId == null）
        var policies = await _dbSet.AsNoTracking()
            .Where(p => p.DeletedAt == null && p.IsActive && (p.MailboxId == null || idList.Contains(p.MailboxId.Value)))
            .ToListAsync();

        var result = new Dictionary<int, SlaPolicy>();
        foreach (var mailboxId in idList)
        {
            // 优先匹配邮箱专属策略，其次匹配全局策略（与 GetPolicyForConversationAsync 保持一致）
            var selected = policies
                .Where(p => p.MailboxId == mailboxId || p.MailboxId == null)
                .OrderByDescending(p => p.MailboxId.HasValue ? 1 : 0)
                .ThenByDescending(p => p.Priority)
                .ThenBy(p => p.SortOrder)
                .FirstOrDefault();
            if (selected != null)
            {
                result[mailboxId] = selected;
            }
        }
        return result;
    }

    public async Task<(IEnumerable<SlaPolicy> Items, int Total)> GetPagedAsync(
        int page, int pageSize, int? mailboxId = null, bool? isActive = null, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking()).Where(p => p.DeletedAt == null).AsQueryable();

        if (mailboxId.HasValue)
        {
            // 同时返回全局策略（mailboxId=null）和指定邮箱的策略
            query = query.Where(p => p.MailboxId == mailboxId.Value || p.MailboxId == null);
        }
        if (isActive.HasValue)
        {
            query = query.Where(p => p.IsActive == isActive.Value);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(p => p.Name.Contains(keyword) || (p.Description != null && p.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.Priority)
            .ThenBy(p => p.SortOrder)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<SlaPolicy>> GetAllWithMailboxAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(p => p.DeletedAt == null)
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.SortOrder)
            .ThenByDescending(p => p.Id)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        var total = await _dbSet.AsNoTracking().CountAsync(p => p.DeletedAt == null);
        var active = await _dbSet.AsNoTracking().CountAsync(p => p.DeletedAt == null && p.IsActive);
        var global = await _dbSet.AsNoTracking().CountAsync(p => p.DeletedAt == null && p.MailboxId == null);
        var mailboxSpecific = total - global;
        var violations = await _context.Set<SlaViolation>().AsNoTracking().CountAsync();
        var breached = await _context.Set<SlaViolation>().AsNoTracking().CountAsync(v => v.Status == "breached");

        return new Dictionary<string, int>
        {
            { "total", total },
            { "active", active },
            { "inactive", total - active },
            { "global", global },
            { "mailboxSpecific", mailboxSpecific },
            { "violations", violations },
            { "breached", breached }
        };
    }

    // ====== 垃圾箱 ======
    public async Task<(IEnumerable<SlaPolicy> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking()).Where(p => p.DeletedAt != null).AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(p => p.Name.Contains(keyword) || (p.Description != null && p.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.DeletedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().AsNoTracking().CountAsync(p => p.DeletedAt != null);
    }

    public async Task<SlaPolicy?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt != null);
    }

    public Task SoftDeleteAsync(SlaPolicy entity)
    {
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        // entity 已由 SlaService.DeleteAsync 通过 GetQueryable(false) tracked 查询获取，
        // 仓储内不再调用 SaveChangesAsync，由调用方（SlaService.DeleteAsync）统一 SaveChanges。
        return Task.CompletedTask;
    }

    public async Task RestoreAsync(SlaPolicy entity)
    {
        // 直接执行 UPDATE，绕过变更跟踪器
        // L-004: 同步 Name 属性，支持恢复时因同名冲突而自动重命名
        await _dbSet.IgnoreQueryFilters()
            .Where(p => p.Id == entity.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DeletedAt, (DateTime?)null)
                .SetProperty(p => p.Name, entity.Name)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
    }

    public async Task HardDeleteAsync(SlaPolicy entity)
    {
        // 先删除关联的违规记录
        await _context.SlaViolations.Where(v => v.SlaPolicyId == entity.Id).ExecuteDeleteAsync();
        // 再删除策略（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters().Where(p => p.Id == entity.Id).ExecuteDeleteAsync();
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        // 统计待删除数量
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(p => p.DeletedAt != null);
        if (count > 0)
        {
            // 获取所有已删除策略的 ID
            var ids = await _dbSet.IgnoreQueryFilters()
                .Where(p => p.DeletedAt != null)
                .Select(p => p.Id)
                .ToListAsync();

            // 先删除关联的违规记录
            await _context.SlaViolations.Where(v => ids.Contains(v.SlaPolicyId)).ExecuteDeleteAsync();
            // 再删除所有已软删除的策略
            await _dbSet.IgnoreQueryFilters().Where(p => p.DeletedAt != null).ExecuteDeleteAsync();
        }
        return count;
    }
}

public class SlaViolationRepository : Repository<SlaViolation>, ISlaViolationRepository
{
    public SlaViolationRepository(NoneadCSSDbContext context) : base(context) { }

    private IQueryable<SlaViolation> ApplyIncludes(IQueryable<SlaViolation> query)
    {
        return query
            .Include(v => v.SlaPolicy)
            .Include(v => v.Conversation);
    }

    public async Task<SlaViolation?> GetByConversationAsync(int conversationId, string violationType)
    {
        return await _dbSet.AsNoTracking()
            .Where(v => v.ConversationId == conversationId && v.ViolationType == violationType)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<SlaViolation>> GetByConversationAsync(int conversationId)
    {
        return await _dbSet.AsNoTracking()
            .Where(v => v.ConversationId == conversationId)
            .Include(v => v.SlaPolicy)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
    }

    public async Task<Dictionary<(int ConversationId, string ViolationType), SlaViolation>> GetByConversationIdsAsync(IEnumerable<int> conversationIds)
    {
        var idList = conversationIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<(int, string), SlaViolation>();
        }

        // F-C1: 使用追踪查询（去掉 AsNoTracking），让 ChangeTracker 跟踪返回的实体，
        // 这样 ScanViolationsAsync 中对违规记录的状态修改能被 SaveChangesAsync 自动持久化。
        // 按 Id 降序以便取每个 (会话, 违规类型) 的最新一条（与 GetByConversationAsync(int, string) 语义一致）
        var violations = await _dbSet
            .Where(v => idList.Contains(v.ConversationId))
            .OrderByDescending(v => v.Id)
            .ToListAsync();

        return violations
            .GroupBy(v => (v.ConversationId, v.ViolationType))
            .ToDictionary(g => g.Key, g => g.First());
    }

    public async Task<(IEnumerable<SlaViolation> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? policyId = null, int? conversationId = null, string? violationType = null,
        string? status = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking()).AsQueryable();

        if (policyId.HasValue) query = query.Where(v => v.SlaPolicyId == policyId.Value);
        if (conversationId.HasValue) query = query.Where(v => v.ConversationId == conversationId.Value);
        if (!string.IsNullOrEmpty(violationType)) query = query.Where(v => v.ViolationType == violationType);
        if (!string.IsNullOrEmpty(status)) query = query.Where(v => v.Status == status);
        if (startDate.HasValue) query = query.Where(v => v.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(v => v.CreatedAt < endExclusive);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<SlaViolation>> GetActiveWarningsAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(v => v.Status == "warning")
            .OrderBy(v => v.DueAt)
            .ToListAsync();
    }

    public async Task<List<SlaViolation>> GetBreachedAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(v => v.Status == "breached")
            .OrderByDescending(v => v.DetectedAt)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetStatsAsync(DateTime? start = null, DateTime? end = null)
    {
        var query = _dbSet.AsNoTracking().AsQueryable();
        if (start.HasValue) query = query.Where(v => v.CreatedAt >= start.Value);
        if (end.HasValue) query = query.Where(v => v.CreatedAt <= end.Value);

        var total = await query.CountAsync();
        var warning = await query.CountAsync(v => v.Status == "warning");
        var breached = await query.CountAsync(v => v.Status == "breached");
        var resolved = await query.CountAsync(v => v.Status == "resolved");
        var firstResponse = await query.CountAsync(v => v.ViolationType == "first_response");
        var resolution = await query.CountAsync(v => v.ViolationType == "resolution");

        return new Dictionary<string, int>
        {
            { "total", total },
            { "warning", warning },
            { "breached", breached },
            { "resolved", resolved },
            { "firstResponse", firstResponse },
            { "resolution", resolution }
        };
    }

    public async Task<List<SlaViolation>> GetRecentAsync(int limit = 20)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .OrderByDescending(v => v.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }
}
