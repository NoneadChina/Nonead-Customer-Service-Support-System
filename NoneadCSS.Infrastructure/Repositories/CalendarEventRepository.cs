using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Infrastructure.Repositories;

/// <summary>
/// 日历事件仓储实现
/// </summary>
public class CalendarEventRepository : ICalendarEventRepository
{
    private readonly NoneadCSSDbContext _context;
    private readonly DbSet<CalendarEvent> _dbSet;

    public CalendarEventRepository(NoneadCSSDbContext context)
    {
        _context = context;
        _dbSet = context.Set<CalendarEvent>();
    }

    public IQueryable<CalendarEvent> GetQueryable(bool asNoTracking = true)
    {
        return asNoTracking ? _dbSet.AsNoTracking() : _dbSet.AsQueryable();
    }

    /// <summary>
    /// 应用标准的 Include 链，包含关联的会话、邮箱和用户信息。
    /// </summary>
    private IQueryable<CalendarEvent> ApplyIncludes(IQueryable<CalendarEvent> query)
    {
        return query
            .Include(e => e.Conversation)
            .Include(e => e.Mailbox)
            .Include(e => e.User)
            .Include(e => e.CreatedByUser);
    }

    public async Task<CalendarEvent?> GetByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(e => e.DeletedAt == null)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<CalendarEvent?> GetByIdTrackedAsync(int id, bool includeDeleted = false)
    {
        var query = _dbSet.AsQueryable(); // tracked (no AsNoTracking)
        if (includeDeleted)
            query = query.IgnoreQueryFilters();
        else
            query = query.Where(e => e.DeletedAt == null);
        return await ApplyIncludes(query)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<List<CalendarEvent>> GetListAsync(
        DateTime? startDate, DateTime? endDate,
        int? mailboxId, int? userId, IEnumerable<int>? userIds,
        string? eventType, string? status, string? keyword,
        int page, int pageSize,
        bool excludeRecurrenceInstances = false,
        string? sortBy = null,
        string? sortDir = null,
        bool includeNavigation = true)
    {
        var query = BuildQuery(startDate, endDate, mailboxId, userId, userIds, eventType, status, keyword, includeNavigation: includeNavigation, excludeRecurrenceInstances: excludeRecurrenceInstances);
        var ordered = ApplySort(query, sortBy, sortDir);
        return await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// 根据 sortBy/sortDir 应用动态排序。sortBy 为 null/空时按默认 StartAt 倒序。
    /// </summary>
    private static IOrderedQueryable<CalendarEvent> ApplySort(
        IQueryable<CalendarEvent> query,
        string? sortBy,
        string? sortDir)
    {
        var dir = string.IsNullOrWhiteSpace(sortDir)
            ? "desc"
            : sortDir.Trim().ToLowerInvariant();

        bool asc = dir == "asc";

        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "priority" => asc
                ? query.OrderBy(e => e.Priority ?? 0).ThenByDescending(e => e.StartAt)
                : query.OrderByDescending(e => e.Priority ?? 0).ThenByDescending(e => e.StartAt),
            "progress" => asc
                ? query.OrderBy(e => e.Progress ?? 0).ThenByDescending(e => e.StartAt)
                : query.OrderByDescending(e => e.Progress ?? 0).ThenByDescending(e => e.StartAt),
            "createdat" => asc
                ? query.OrderBy(e => e.CreatedAt).ThenByDescending(e => e.StartAt)
                : query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.StartAt),
            "time" or null or "" => asc
                ? query.OrderBy(e => e.StartAt)
                : query.OrderByDescending(e => e.StartAt),
            _ => query.OrderByDescending(e => e.StartAt)
        };
    }

    public async Task<int> CountAsync(
        DateTime? startDate, DateTime? endDate,
        int? mailboxId, int? userId, IEnumerable<int>? userIds,
        string? eventType, string? status, string? keyword,
        bool excludeRecurrenceInstances = false)
    {
        var query = BuildQuery(startDate, endDate, mailboxId, userId, userIds, eventType, status, keyword, includeNavigation: false, excludeRecurrenceInstances: excludeRecurrenceInstances);
        return await query.CountAsync();
    }

    public async Task<List<CalendarEvent>> GetAllAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(e => e.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<CalendarEvent> AddAsync(CalendarEvent entity)
    {
        await _dbSet.AddAsync(entity);
        return entity;
    }

    public Task UpdateAsync(CalendarEvent entity)
    {
        // entity 应由调用方通过 tracked 查询获取，由 SaveChangesAsync 持久化，无需 _dbSet.Update
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CalendarEvent entity)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }

    private IQueryable<CalendarEvent> BuildQuery(
        DateTime? startDate, DateTime? endDate,
        int? mailboxId, int? userId, IEnumerable<int>? userIds,
        string? eventType, string? status, string? keyword,
        bool includeNavigation = true,
        bool excludeRecurrenceInstances = false)
    {
        var baseQuery = _dbSet.AsNoTracking();
        var query = includeNavigation ? ApplyIncludes(baseQuery).AsQueryable() : baseQuery.AsQueryable();

        // 软删除兜底：只返回未软删除的日程（垃圾箱专用接口走 IgnoreQueryFilters 另查）
        query = query.Where(e => e.DeletedAt == null);

        // H-F2: 非展开模式下，过滤掉 RecurrenceParentId 非空的子实例记录
        // （Following/All 模式拆分产生的数据库子实例仅作为展开辅助，列表定义视图不显示）
        if (excludeRecurrenceInstances)
        {
            query = query.Where(e => e.RecurrenceParentId == null);
        }

        if (startDate.HasValue)
        {
            if (excludeRecurrenceInstances)
            {
                // 非展开模式：严格按日期过滤
                query = query.Where(e => e.StartAt >= startDate.Value);
            }
            else
            {
                // 展开模式：除了日期范围内的事件，还包含起始时间在范围之前但可能产生范围内实例的重复父事件。
                // RecurrenceExpansionService 会将父事件展开到实际请求的范围内，不会产生范围外的实例。
                // 修复：9月16日创建的月重复事件在查看10月/11月/12月视图时，因 StartAt(9/16) < startDate(约9/24)
                // 被过滤掉，导致后续月份的重复实例无法展开。
                query = query.Where(e =>
                    e.StartAt >= startDate.Value ||
                    (e.IsRecurring && e.RecurrenceParentId == null &&
                     (!e.RecurrenceEndDate.HasValue || e.RecurrenceEndDate >= startDate.Value)));
            }
        }
        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(e => e.StartAt < endExclusive);
        }
        if (mailboxId.HasValue)
            query = query.Where(e => e.MailboxId == mailboxId.Value);
        if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        if (userIds != null && userIds.Any())
        {
            if (userId.HasValue)
            {
                // 两者取交集
                if (!userIds.Contains(userId.Value))
                {
                    query = query.Where(e => false);
                }
                else
                {
                    query = query.Where(e => e.UserId == userId.Value);
                }
            }
            else
            {
                query = query.Where(e => e.UserId.HasValue && userIds.Contains(e.UserId.Value));
            }
        }
        if (!string.IsNullOrEmpty(eventType))
            query = query.Where(e => e.EventType == eventType);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(e => e.Status == status);
        if (!string.IsNullOrEmpty(keyword))
        {
            var kw = keyword.Trim();
            var pattern = $"%{kw}%";
            query = query.Where(e => EF.Functions.Like(e.Title, pattern)
                || (e.Description != null && EF.Functions.Like(e.Description, pattern)));
        }

        return query;
    }

    // 软删除

    public async Task<CalendarEvent?> GetByIdIgnoreQueryFilterAsync(int id)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters())
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<List<CalendarEvent>> GetTrashAsync(int page, int pageSize, string? keyword, int? userId)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(e => e.DeletedAt != null);

        // 按用户ID过滤：匹配创建者(CreatedBy，非0)或拥有者(UserId)
        if (userId.HasValue)
        {
            var uid = userId.Value;
            query = query.Where(e => (e.CreatedBy != 0 && e.CreatedBy == uid) || (e.UserId == uid));
        }

        if (!string.IsNullOrEmpty(keyword))
        {
            var kw = keyword.Trim();
            var pattern = $"%{kw}%";
            query = query.Where(e => EF.Functions.Like(e.Title, pattern)
                || (e.Description != null && EF.Functions.Like(e.Description, pattern)));
        }

        return await query
            .OrderByDescending(e => e.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> CountTrashAsync(string? keyword, int? userId)
    {
        var query = _dbSet.IgnoreQueryFilters()
            .Where(e => e.DeletedAt != null);

        if (userId.HasValue)
        {
            var uid = userId.Value;
            query = query.Where(e => (e.CreatedBy != 0 && e.CreatedBy == uid) || (e.UserId == uid));
        }

        if (!string.IsNullOrEmpty(keyword))
        {
            var kw = keyword.Trim();
            var pattern = $"%{kw}%";
            query = query.Where(e => EF.Functions.Like(e.Title, pattern)
                || (e.Description != null && EF.Functions.Like(e.Description, pattern)));
        }

        return await query.CountAsync();
    }

    public Task HardDeleteAsync(CalendarEvent entity)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task<int> ClearTrashAsync(int? userId)
    {
        var query = _dbSet.IgnoreQueryFilters()
            .Where(e => e.DeletedAt != null);

        // 按用户ID过滤：匹配创建者(CreatedBy，非0)或拥有者(UserId)
        if (userId.HasValue)
        {
            var uid = userId.Value;
            query = query.Where(e => (e.CreatedBy != 0 && e.CreatedBy == uid) || (e.UserId == uid));
        }

        // 使用 ExecuteDeleteAsync 直接执行 DELETE SQL，绕过 ChangeTracker
        return await query.ExecuteDeleteAsync();
    }

    // 重复日程

    public async Task<List<CalendarEvent>> GetRecurringEventsAsync(DateTime? startDate, DateTime? endDate)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking())
            .Where(e => e.DeletedAt == null && e.IsRecurring && e.RecurrenceParentId == null);

        if (endDate.HasValue)
            query = query.Where(e => e.StartAt <= endDate.Value);
        if (startDate.HasValue)
            query = query.Where(e => !e.RecurrenceEndDate.HasValue || e.RecurrenceEndDate >= startDate.Value);

        return await query.ToListAsync();
    }

    public async Task<List<CalendarEvent>> GetByParentIdAsync(int parentId)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(e => e.DeletedAt == null && e.RecurrenceParentId == parentId)
            .OrderBy(e => e.StartAt)
            .ToListAsync();
    }

    public async Task<List<CalendarEvent>> GetFollowingInstancesAsync(int parentId, DateTime fromDate)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(e => e.DeletedAt == null && e.RecurrenceParentId == parentId && e.StartAt >= fromDate)
            .OrderBy(e => e.StartAt)
            .ToListAsync();
    }

    /// <summary>追踪版 GetByParentIdAsync，用于写场景（修改子实例属性后 SaveChanges）。</summary>
    public async Task<List<CalendarEvent>> GetByParentIdTrackedAsync(int parentId)
    {
        return await ApplyIncludes(_dbSet)
            .Where(e => e.DeletedAt == null && e.RecurrenceParentId == parentId)
            .OrderBy(e => e.StartAt)
            .ToListAsync();
    }

    /// <summary>追踪版 GetFollowingInstancesAsync，用于写场景（修改子实例属性后 SaveChanges）。</summary>
    public async Task<List<CalendarEvent>> GetFollowingInstancesTrackedAsync(int parentId, DateTime fromDate)
    {
        return await ApplyIncludes(_dbSet)
            .Where(e => e.DeletedAt == null && e.RecurrenceParentId == parentId && e.StartAt >= fromDate)
            .OrderBy(e => e.StartAt)
            .ToListAsync();
    }

    /// <summary>
    /// 获取指定父事件的全部子实例（包括已软删除的），用于 HardDelete 等需要物理删除所有子实例的场景。
    /// 此方法绕过 EF Core 全局查询过滤器，确保已软删除的子实例也能被物理删除，避免垃圾箱残留孤儿数据。
    /// </summary>
    public async Task<List<CalendarEvent>> GetByParentIdIncludingDeletedAsync(int parentId)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(e => e.RecurrenceParentId == parentId)
            .OrderBy(e => e.StartAt)
            .ToListAsync();
    }

    public async Task BulkAddAsync(IEnumerable<CalendarEvent> entities)
    {
        await _dbSet.AddRangeAsync(entities);
    }

    public Task BulkDeleteAsync(IEnumerable<CalendarEvent> entities)
    {
        _dbSet.RemoveRange(entities);
        return Task.CompletedTask;
    }

    // 提醒

    public async Task<List<CalendarEvent>> GetUpcomingRemindersAsync(DateTime fromTime, DateTime toTime)
    {
        // 获取未发送提醒的事件 + 已发送但可能需要新周期提醒的周期性事件
        // 显式过滤软删除记录，避免对已删除事件触发提醒
        return await ApplyIncludes(_dbSet)
            .Where(e => e.DeletedAt == null
                && e.ReminderMinutes.HasValue
                && e.Status == "pending"
                && (
                    // 1. 未发送提醒的事件（常规）
                    (!e.IsReminderSent && e.StartAt >= fromTime && e.StartAt <= toTime)
                    ||
                    // 2. 已发送提醒的周期性事件（需检查是否到达新周期）
                    (e.IsRecurring && e.IsReminderSent && e.StartAt <= toTime
                        && (!e.RecurrenceEndDate.HasValue || e.RecurrenceEndDate >= fromTime))
                ))
            .OrderBy(e => e.StartAt)
            .ToListAsync();
    }

    public async Task<int> TryClaimFirstReminderAsync(int eventId, DateTime utcNow)
    {
        // 原子 Claim：仅当 IsReminderSent=false 时更新为 true，返回受影响行数（1=抢占成功）
        return await _dbSet
            .Where(e => e.Id == eventId && !e.IsReminderSent)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.IsReminderSent, true)
                .SetProperty(e => e.LastReminderSentAt, utcNow)
                .SetProperty(e => e.UpdatedAt, utcNow));
    }

    public async Task<int> TryClaimRecurringReminderResetAsync(int eventId, DateTime cycleCutoff, DateTime utcNow)
    {
        // 原子 Claim：周期性新周期提醒，仅当 DB 中 LastReminderSentAt 早于 cycleCutoff 时更新
        return await _dbSet
            .Where(e => e.Id == eventId && e.IsReminderSent && e.IsRecurring
                && e.LastReminderSentAt.HasValue && e.LastReminderSentAt.Value < cycleCutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.IsReminderSent, true)
                .SetProperty(e => e.LastReminderSentAt, utcNow)
                .SetProperty(e => e.UpdatedAt, utcNow));
    }

    /// <summary>
    /// 构建概览查询基础 query（ApplyIncludes + 用户权限过滤 + 软删除过滤）。
    /// </summary>
    private IQueryable<CalendarEvent> BuildOverviewQuery(IEnumerable<int>? userIds)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking())
            .Where(e => e.DeletedAt == null)
            .AsQueryable();

        if (userIds != null && userIds.Any())
        {
            query = query.Where(e => e.UserId.HasValue && userIds.Contains(e.UserId.Value));
        }

        return query;
    }

    public async Task<(CalendarOverviewStats Stats, List<CalendarEvent> UpcomingEvents)> GetOverviewAggregateAsync(
        IEnumerable<int>? userIds,
        DateTime now,
        int upcomingTake = 10)
    {
        var query = BuildOverviewQuery(userIds);

        var stats = await query
            .GroupBy(e => 1)
            .Select(g => new CalendarOverviewStats
            {
                Total = g.Count(),
                Pending = g.Count(e => e.Status == "pending"),
                Completed = g.Count(e => e.Status == "completed"),
                Cancelled = g.Count(e => e.Status == "cancelled"),
                Upcoming = g.Count(e => e.StartAt >= now && e.Status == "pending"),
                Overdue = g.Count(e => e.StartAt < now && e.Status == "pending")
            })
            .FirstOrDefaultAsync();

        if (stats == null)
        {
            stats = new CalendarOverviewStats();
        }

        var upcomingEvents = await query
            .Where(e => e.StartAt >= now && e.Status == "pending")
            .OrderBy(e => e.StartAt)
            .Take(upcomingTake)
            .ToListAsync();

        return (stats, upcomingEvents);
    }

    /// <summary>
    /// 硬删除所有早于 cutoff 的软删除事件（根据 DeletedAt 字段判断）。
    /// 返回删除的记录条数。
    /// </summary>
    public async Task<int> PurgeTrashAsync(DateTime cutoff, int? userId)
    {
        var query = _dbSet.IgnoreQueryFilters()
            .Where(e => e.DeletedAt != null && e.DeletedAt <= cutoff);

        // 按用户ID过滤：匹配创建者(CreatedBy，非0)或拥有者(UserId)
        if (userId.HasValue)
        {
            var uid = userId.Value;
            query = query.Where(e => (e.CreatedBy != 0 && e.CreatedBy == uid) || (e.UserId == uid));
        }

        // 使用 ExecuteDeleteAsync 直接执行 DELETE SQL，绕过 ChangeTracker
        return await query.ExecuteDeleteAsync();
    }
}
