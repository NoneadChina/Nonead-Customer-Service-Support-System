using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class AuditLogRepository : Repository<AuditLog>, IAuditLogRepository
{
    private const int StatsTopN = 8;

    public AuditLogRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<(IEnumerable<AuditLog> Items, int Total)> GetByUserIdAsync(int userId, int page, int pageSize)
    {
        var query = _dbSet.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IEnumerable<AuditLog> Items, int Total)> GetByEntityAsync(string entityType, int entityId, int page, int pageSize)
    {
        var entityTypeLower = entityType.ToLowerInvariant();
        var query = _dbSet.AsNoTracking()
            .Where(a => a.EntityType.ToLower() == entityTypeLower && a.EntityId == entityId)
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IEnumerable<AuditLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = _dbSet.AsNoTracking().AsQueryable();
        query = ApplyFilters(query, action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate);

        query = query
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<AuditLogStats> GetStatsAsync(
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var baseQuery = _dbSet.AsNoTracking().AsQueryable();
        baseQuery = ApplyFilters(baseQuery, action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate);

        var totalLogs = await baseQuery.CountAsync();

        var last24hCutoff = DateTime.UtcNow.AddHours(-24);
        var last24hCount = await baseQuery.CountAsync(a => a.CreatedAt >= last24hCutoff);

        var uniqueUsers = await baseQuery
            .Where(a => a.UserId != null)
            .Select(a => a.UserId!.Value)
            .Distinct()
            .CountAsync();

        var failedActions = await baseQuery.CountAsync(a =>
            a.Action == "loginfailed" || a.Action == "lockout" || a.Action == "resetfailed");

        // 操作类型分布：Top N，按计数降序
        var actionDist = await baseQuery
            .GroupBy(a => a.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(StatsTopN)
            .ToListAsync();
        var actionDistribution = actionDist
            .Select(g => (Action: g.Action ?? "(空)", g.Count))
            .ToList();

        // 实体类型分布：Top N，按计数降序
        var entityDist = await baseQuery
            .GroupBy(a => a.EntityType)
            .Select(g => new { EntityType = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(StatsTopN)
            .ToListAsync();
        var entityDistribution = entityDist
            .Select(g => (EntityType: g.EntityType ?? "(空)", g.Count))
            .ToList();

        return new AuditLogStats
        {
            TotalLogs = totalLogs,
            UniqueUsers = uniqueUsers,
            Last24hCount = last24hCount,
            FailedActions = failedActions,
            ActionDistribution = actionDistribution,
            EntityDistribution = entityDistribution
        };
    }

    public async Task<List<AuditLog>> ExportAsync(
        string? action = null,
        string? entityType = null,
        int? userId = null,
        int? entityId = null,
        string? ipAddress = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int maxRows = 5000)
    {
        var query = _dbSet.AsNoTracking().AsQueryable();
        query = ApplyFilters(query, action, entityType, userId, entityId, ipAddress, keyword, startDate, endDate);

        query = query
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt)
            .Take(maxRows);

        return await query.ToListAsync();
    }

    /// <summary>
    /// 统一应用过滤条件，避免分页 / 统计 / 导出三处重复实现导致行为不一致。
    /// </summary>
    private static IQueryable<AuditLog> ApplyFilters(
        IQueryable<AuditLog> query,
        string? action,
        string? entityType,
        int? userId,
        int? entityId,
        string? ipAddress,
        string? keyword,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (!string.IsNullOrEmpty(action))
        {
            var actionLower = action.ToLowerInvariant();
            query = query.Where(a => a.Action.ToLower() == actionLower);
        }

        if (!string.IsNullOrEmpty(entityType))
        {
            var entityTypeLower = entityType.ToLowerInvariant();
            query = query.Where(a => a.EntityType.ToLower() == entityTypeLower);
        }

        if (userId.HasValue)
        {
            query = query.Where(a => a.UserId == userId.Value);
        }

        if (entityId.HasValue)
        {
            query = query.Where(a => a.EntityId == entityId.Value);
        }

        if (!string.IsNullOrEmpty(ipAddress))
        {
            var ipLower = ipAddress.ToLowerInvariant();
            query = query.Where(a => a.IpAddress.ToLower() == ipLower);
        }

        if (!string.IsNullOrEmpty(keyword))
        {
            var keywordLower = keyword.ToLowerInvariant();
            // 关键字在 Action / EntityType / IpAddress / UserAgent 中模糊匹配
            query = query.Where(a =>
                a.Action.ToLower().Contains(keywordLower) ||
                a.EntityType.ToLower().Contains(keywordLower) ||
                a.IpAddress.ToLower().Contains(keywordLower) ||
                a.UserAgent.ToLower().Contains(keywordLower));
        }

        if (startDate.HasValue)
        {
            query = query.Where(a => a.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(a => a.CreatedAt <= endDate.Value);
        }

        return query;
    }

    public override async Task<AuditLog?> GetByIdAsync(int id)
    {
        return await _dbSet.AsNoTracking()
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == id);
    }
}
