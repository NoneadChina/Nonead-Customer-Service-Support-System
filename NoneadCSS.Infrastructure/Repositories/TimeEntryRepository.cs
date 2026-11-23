using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Infrastructure.Repositories;

/// <summary>
/// 时间追踪记录仓储实现
/// </summary>
public class TimeEntryRepository : ITimeEntryRepository
{
    private readonly NoneadCSSDbContext _context;
    private readonly DbSet<TimeEntry> _dbSet;

    public TimeEntryRepository(NoneadCSSDbContext context)
    {
        _context = context;
        _dbSet = context.Set<TimeEntry>();
    }

    private IQueryable<TimeEntry> ApplyIncludes(IQueryable<TimeEntry> query)
    {
        return query
            .Include(e => e.Conversation)
            .Include(e => e.Customer)
            .Include(e => e.User)
            .Include(e => e.Mailbox);
    }

    /// <summary>
    /// TT-017: 将 endDate 归一化为"当天结束"的排他上界（次日 00:00:00）。
    /// 避免 endDate 带时间分量时误包含次日数据。
    /// </summary>
    private static DateTime NormalizeEndExclusive(DateTime endDate) => endDate.Date.AddDays(1);

    public async Task<TimeEntry?> GetByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<TimeEntry?> GetByIdTrackedAsync(int id)
    {
        return await ApplyIncludes(_dbSet)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<List<TimeEntry>> GetListAsync(
        int? userId, int? conversationId, int? customerId,
        string? entryType, bool? billable,
        DateTime? startDate, DateTime? endDate,
        string? search, int page, int pageSize, int[]? userIds = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking());
        query = ApplyFilters(query, userId, conversationId, customerId, entryType, billable, startDate, endDate, search, userIds);
        return await query
            .OrderByDescending(e => e.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> CountAsync(
        int? userId, int? conversationId, int? customerId,
        string? entryType, bool? billable,
        DateTime? startDate, DateTime? endDate,
        string? search, int[]? userIds = null)
    {
        var query = _dbSet.AsNoTracking();
        query = ApplyFilters(query, userId, conversationId, customerId, entryType, billable, startDate, endDate, search, userIds);
        return await query.CountAsync();
    }

    private static IQueryable<TimeEntry> ApplyFilters(
        IQueryable<TimeEntry> query,
        int? userId, int? conversationId, int? customerId,
        string? entryType, bool? billable,
        DateTime? startDate, DateTime? endDate,
        string? search, int[]? userIds = null)
    {
        // 优先使用 userIds 集合过滤（主管查看团队成员数据）；否则使用单 userId
        if (userIds != null && userIds.Length > 0)
            query = query.Where(e => userIds.Contains(e.UserId));
        else if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        if (conversationId.HasValue)
            query = query.Where(e => e.ConversationId == conversationId.Value);
        if (customerId.HasValue)
            query = query.Where(e => e.CustomerId == customerId.Value);
        if (!string.IsNullOrEmpty(entryType))
            query = query.Where(e => e.EntryType == entryType);
        if (billable.HasValue)
            query = query.Where(e => e.Billable == billable.Value);
        if (startDate.HasValue)
            query = query.Where(e => e.StartTime >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = NormalizeEndExclusive(endDate.Value);
            query = query.Where(e => e.StartTime < endExclusive);
        }
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => e.Description != null && e.Description.Contains(search));
        return query;
    }

    public async Task<List<TimeEntry>> GetAllAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .OrderByDescending(e => e.StartTime)
            .ToListAsync();
    }

    public async Task<TimeEntry?> GetActiveTimerAsync(int userId)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(e => e.UserId == userId && e.EndTime == null && e.DeletedAt == null);
    }

    public async Task<List<TimeEntry>> GetAllActiveTimersAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(e => e.EndTime == null && e.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<TimeEntry> AddAsync(TimeEntry entry)
    {
        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;
        await _dbSet.AddAsync(entry);
        return entry;
    }

    public async Task<TimeEntry?> UpdateAsync(TimeEntry entry)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        return entry;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entry = await _dbSet.FindAsync(id);
        if (entry == null) return false;
        entry.DeletedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var entry = await _dbSet.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt != null);
        if (entry == null) return false;
        _dbSet.Remove(entry);
        return true;
    }

    // 垃圾箱
    public async Task<List<TimeEntry>> GetTrashAsync(int page, int pageSize, string? keyword)
    {
        var query = _dbSet.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.DeletedAt != null);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(e => e.Description != null && e.Description.Contains(keyword));
        return await ApplyIncludes(query)
            .OrderByDescending(e => e.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    // TT-012: CountTrashAsync 支持 keyword 过滤，与 GetTrashAsync 一致
    public async Task<int> CountTrashAsync(string? keyword = null)
    {
        var query = _dbSet.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.DeletedAt != null);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(e => e.Description != null && e.Description.Contains(keyword));
        return await query.CountAsync();
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var entry = await _dbSet.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id && e.DeletedAt != null);
        if (entry == null) return false;
        entry.DeletedAt = null;
        entry.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    // TT-016: ClearTrashAsync 使用 ExecuteDeleteAsync 批量删除，避免全量加载到内存
    public async Task<int> ClearTrashAsync()
    {
        return await _dbSet.IgnoreQueryFilters()
            .Where(e => e.DeletedAt != null)
            .ExecuteDeleteAsync();
    }

    // 统计
    public async Task<int> GetTotalMinutesAsync(int? userId, DateTime? startDate, DateTime? endDate, int[]? userIds = null)
    {
        var query = _dbSet.AsNoTracking().Where(e => e.EndTime != null);
        if (userIds != null && userIds.Length > 0)
            query = query.Where(e => userIds.Contains(e.UserId));
        else if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        if (startDate.HasValue)
            query = query.Where(e => e.StartTime >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = NormalizeEndExclusive(endDate.Value);
            query = query.Where(e => e.StartTime < endExclusive);
        }
        return await query.SumAsync(e => e.DurationMinutes);
    }

    public async Task<int> GetBillableMinutesAsync(int? userId, DateTime? startDate, DateTime? endDate, int[]? userIds = null)
    {
        var query = _dbSet.AsNoTracking().Where(e => e.EndTime != null && e.Billable);
        if (userIds != null && userIds.Length > 0)
            query = query.Where(e => userIds.Contains(e.UserId));
        else if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        if (startDate.HasValue)
            query = query.Where(e => e.StartTime >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = NormalizeEndExclusive(endDate.Value);
            query = query.Where(e => e.StartTime < endExclusive);
        }
        return await query.SumAsync(e => e.DurationMinutes);
    }

    public async Task<int> GetCountAsync(int? userId, DateTime? startDate, DateTime? endDate, int[]? userIds = null)
    {
        var query = _dbSet.AsNoTracking().Where(e => e.EndTime != null);
        if (userIds != null && userIds.Length > 0)
            query = query.Where(e => userIds.Contains(e.UserId));
        else if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        if (startDate.HasValue)
            query = query.Where(e => e.StartTime >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = NormalizeEndExclusive(endDate.Value);
            query = query.Where(e => e.StartTime < endExclusive);
        }
        return await query.CountAsync();
    }

    /// <summary>
    /// TT-022: 单次查询返回 Overview 所需的全部统计（7 项合并为 1 次 DB 往返）
    /// </summary>
    public async Task<TimeEntryOverviewStats> GetOverviewStatsAsync(
        DateTime todayStart, DateTime todayEnd,
        DateTime weekStart, DateTime weekEnd,
        DateTime monthStart, DateTime monthEnd,
        int[]? userIds = null)
    {
        var query = _dbSet.AsNoTracking().Where(e => e.EndTime != null);
        if (userIds != null && userIds.Length > 0)
            query = query.Where(e => userIds.Contains(e.UserId));

        // 条件聚合：单次 GroupBy + 多个带条件的 Sum/Count，1 次往返拿到 7 项统计
        var stats = await query
            .GroupBy(e => 1)
            .Select(g => new
            {
                TodayMinutes = g.Where(e => e.StartTime >= todayStart && e.StartTime < todayEnd).Sum(e => e.DurationMinutes),
                WeekMinutes = g.Where(e => e.StartTime >= weekStart && e.StartTime < weekEnd).Sum(e => e.DurationMinutes),
                MonthMinutes = g.Where(e => e.StartTime >= monthStart && e.StartTime < monthEnd).Sum(e => e.DurationMinutes),
                TotalMinutes = g.Sum(e => e.DurationMinutes),
                TodayCount = g.Count(e => e.StartTime >= todayStart && e.StartTime < todayEnd),
                WeekCount = g.Count(e => e.StartTime >= weekStart && e.StartTime < weekEnd),
                BillableMinutes = g.Where(e => e.Billable).Sum(e => e.DurationMinutes)
            })
            .FirstOrDefaultAsync();

        return stats == null
            ? new TimeEntryOverviewStats(0, 0, 0, 0, 0, 0, 0)
            : new TimeEntryOverviewStats(
                stats.TodayMinutes, stats.WeekMinutes, stats.MonthMinutes,
                stats.TotalMinutes, stats.TodayCount, stats.WeekCount, stats.BillableMinutes);
    }

    public async Task<List<(string Label, int TotalMinutes, int Count, int BillableMinutes)>> GetByUserAsync(DateTime startDate, DateTime endDate, int? userId = null, int[]? userIds = null)
    {
        var endExclusive = NormalizeEndExclusive(endDate);
        var query = _dbSet.AsNoTracking()
            .Where(e => e.EndTime != null && e.StartTime >= startDate && e.StartTime < endExclusive);
        if (userIds != null && userIds.Length > 0)
            query = query.Where(e => userIds.Contains(e.UserId));
        else if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        var results = await query
            .Join(_context.Users,
                e => e.UserId,
                u => u.Id,
                // TT-014: 姓名空值安全（null + " " + null 在 SQL 中可能产生 NULL）
                (e, u) => new { e.DurationMinutes, e.Billable, UserName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() != "" ? ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim() : (u.Email ?? "未知用户") })
            .GroupBy(x => x.UserName)
            .Select(g => new
            {
                Label = g.Key,
                TotalMinutes = g.Sum(x => x.DurationMinutes),
                Count = g.Count(),
                BillableMinutes = g.Where(x => x.Billable).Sum(x => x.DurationMinutes)
            })
            .OrderByDescending(x => x.TotalMinutes)
            .ToListAsync();

        return results.Select(r => (r.Label, r.TotalMinutes, r.Count, r.BillableMinutes)).ToList();
    }

    public async Task<List<(string Label, int TotalMinutes, int Count, int BillableMinutes)>> GetByConversationAsync(DateTime startDate, DateTime endDate, int? userId = null, int[]? userIds = null)
    {
        var endExclusive = NormalizeEndExclusive(endDate);
        var query = _dbSet.AsNoTracking()
            .Where(e => e.EndTime != null && e.ConversationId != null && e.StartTime >= startDate && e.StartTime < endExclusive);
        if (userIds != null && userIds.Length > 0)
            query = query.Where(e => userIds.Contains(e.UserId));
        else if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        var results = await query
            .Join(_context.Conversations,
                e => e.ConversationId!.Value,
                c => c.Id,
                (e, c) => new { e.DurationMinutes, e.Billable, ConvNumber = c.Number.ToString() })
            .GroupBy(x => x.ConvNumber)
            .Select(g => new
            {
                Label = g.Key,
                TotalMinutes = g.Sum(x => x.DurationMinutes),
                Count = g.Count(),
                BillableMinutes = g.Where(x => x.Billable).Sum(x => x.DurationMinutes)
            })
            .OrderByDescending(x => x.TotalMinutes)
            .Take(20)
            .ToListAsync();

        return results.Select(r => (r.Label, r.TotalMinutes, r.Count, r.BillableMinutes)).ToList();
    }

    public async Task<List<(string Label, int TotalMinutes, int Count, int BillableMinutes)>> GetByDateAsync(DateTime startDate, DateTime endDate, int? userId = null, int[]? userIds = null)
    {
        var endExclusive = NormalizeEndExclusive(endDate);
        var query = _dbSet.AsNoTracking()
            .Where(e => e.EndTime != null && e.StartTime >= startDate && e.StartTime < endExclusive);
        if (userIds != null && userIds.Length > 0)
            query = query.Where(e => userIds.Contains(e.UserId));
        else if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        var results = await query
            .GroupBy(e => e.StartTime.Date)
            .Select(g => new
            {
                Date = g.Key,
                TotalMinutes = g.Sum(e => e.DurationMinutes),
                Count = g.Count(),
                BillableMinutes = g.Where(e => e.Billable).Sum(e => e.DurationMinutes)
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return results.Select(r => (r.Date.ToString("yyyy-MM-dd"), r.TotalMinutes, r.Count, r.BillableMinutes)).ToList();
    }
}
