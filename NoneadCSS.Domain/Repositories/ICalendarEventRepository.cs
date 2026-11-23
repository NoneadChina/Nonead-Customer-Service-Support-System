using System.Linq.Expressions;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// 日历概览统计聚合 DTO（仓储层返回）。
/// </summary>
public class CalendarOverviewStats
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Upcoming { get; set; }
    public int Overdue { get; set; }
}

/// <summary>
/// 日历事件仓储接口
/// </summary>
public interface ICalendarEventRepository
{
    IQueryable<CalendarEvent> GetQueryable(bool asNoTracking = true);
    Task<CalendarEvent?> GetByIdAsync(int id);
    /// <summary>
    /// 通过 ID 获取被 EF 跟踪的事件，用于写操作场景。
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="includeDeleted">是否包含软删除记录（默认 false）</param>
    Task<CalendarEvent?> GetByIdTrackedAsync(int id, bool includeDeleted = false);
    Task<List<CalendarEvent>> GetListAsync(
        DateTime? startDate, DateTime? endDate,
        int? mailboxId, int? userId, IEnumerable<int>? userIds,
        string? eventType, string? status, string? keyword,
        int page, int pageSize,
        bool excludeRecurrenceInstances = false,
        string? sortBy = null,
        string? sortDir = null,
        bool includeNavigation = true);
    Task<int> CountAsync(
        DateTime? startDate, DateTime? endDate,
        int? mailboxId, int? userId, IEnumerable<int>? userIds,
        string? eventType, string? status, string? keyword,
        bool excludeRecurrenceInstances = false);
    Task<List<CalendarEvent>> GetAllAsync();
    Task<CalendarEvent> AddAsync(CalendarEvent entity);
    Task UpdateAsync(CalendarEvent entity);
    Task DeleteAsync(CalendarEvent entity);

    // 软删除
    Task<CalendarEvent?> GetByIdIgnoreQueryFilterAsync(int id);
    /// <summary>
    /// 获取垃圾箱列表
    /// </summary>
    /// <param name="userId">创建者/拥有者用户ID过滤；null 表示不限制（SuperAdmin）</param>
    Task<List<CalendarEvent>> GetTrashAsync(int page, int pageSize, string? keyword, int? userId);
    /// <summary>
    /// 获取垃圾箱数量
    /// </summary>
    /// <param name="userId">创建者/拥有者用户ID过滤；null 表示不限制（SuperAdmin）</param>
    Task<int> CountTrashAsync(string? keyword, int? userId);
    Task HardDeleteAsync(CalendarEvent entity);
    /// <summary>
    /// 清空垃圾箱
    /// </summary>
    /// <param name="userId">创建者/拥有者用户ID过滤；null 表示不限制（SuperAdmin）</param>
    Task<int> ClearTrashAsync(int? userId);
    /// <summary>
    /// 按期清理垃圾箱（删除 DeletedAt <= cutoff 的记录）
    /// </summary>
    /// <param name="cutoff">删除截止时间（DeletedAt <= 此时间将被硬删除）</param>
    /// <param name="userId">创建者/拥有者用户ID过滤；null 表示不限制（SuperAdmin）</param>
    Task<int> PurgeTrashAsync(DateTime cutoff, int? userId);

    // 重复日程
    Task<List<CalendarEvent>> GetRecurringEventsAsync(DateTime? startDate, DateTime? endDate);
    Task<List<CalendarEvent>> GetByParentIdAsync(int parentId);
    Task<List<CalendarEvent>> GetFollowingInstancesAsync(int parentId, DateTime fromDate);
    /// <summary>获取指定父事件的全部子实例（追踪查询，用于写场景）。</summary>
    Task<List<CalendarEvent>> GetByParentIdTrackedAsync(int parentId);
    /// <summary>获取指定父事件中起始时间 >= fromDate 的子实例（追踪查询，用于写场景）。</summary>
    Task<List<CalendarEvent>> GetFollowingInstancesTrackedAsync(int parentId, DateTime fromDate);
    /// <summary>
    /// 获取指定父事件的全部子实例（包括已软删除的），用于 HardDelete 等需要物理删除所有子实例的场景。
    /// 此方法绕过 EF Core 全局查询过滤器，确保已软删除的子实例也能被物理删除，避免垃圾箱残留孤儿数据。
    /// </summary>
    Task<List<CalendarEvent>> GetByParentIdIncludingDeletedAsync(int parentId);
    Task BulkAddAsync(IEnumerable<CalendarEvent> entities);
    Task BulkDeleteAsync(IEnumerable<CalendarEvent> entities);

    // 提醒
    Task<List<CalendarEvent>> GetUpcomingRemindersAsync(DateTime fromTime, DateTime toTime);
    /// <summary>
    /// 原子 Claim：首次提醒（将 IsReminderSent=false 的事件标记为已提醒）。
    /// 返回受影响行数（1=成功抢占，0=被其他实例抢占）。
    /// </summary>
    Task<int> TryClaimFirstReminderAsync(int eventId, DateTime utcNow);
    /// <summary>
    /// 原子 Claim：周期性事件新周期重发提醒。
    /// cycleCutoff = effectiveStartAt - 1min，仅当 DB 中 LastReminderSentAt &lt; cycleCutoff 时才更新。
    /// 返回受影响行数。
    /// </summary>
    Task<int> TryClaimRecurringReminderResetAsync(int eventId, DateTime cycleCutoff, DateTime utcNow);

    /// <summary>
    /// 聚合查询日历概览统计 + 即将到来的 N 条事件（单次数据库往返，避免多 CountAsync 叠加）。
    /// </summary>
    /// <param name="userIds">有权限看到的用户 ID 范围；null 表示不限制（超级管理员）</param>
    /// <param name="now">当前 UTC 时间（用于 upcoming/overdue 计算，外部传入便于单测）</param>
    /// <param name="upcomingTake">即将到来的事件取多少条，默认 10</param>
    Task<(CalendarOverviewStats Stats, List<CalendarEvent> UpcomingEvents)> GetOverviewAggregateAsync(
        IEnumerable<int>? userIds,
        DateTime now,
        int upcomingTake = 10);
}
