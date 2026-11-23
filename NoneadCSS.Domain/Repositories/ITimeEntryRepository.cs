using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ITimeEntryRepository
{
    Task<TimeEntry?> GetByIdAsync(int id);
    Task<TimeEntry?> GetByIdTrackedAsync(int id);
    Task<List<TimeEntry>> GetListAsync(
        int? userId, int? conversationId, int? customerId,
        string? entryType, bool? billable,
        DateTime? startDate, DateTime? endDate,
        string? search, int page, int pageSize, int[]? userIds = null);
    Task<int> CountAsync(
        int? userId, int? conversationId, int? customerId,
        string? entryType, bool? billable,
        DateTime? startDate, DateTime? endDate,
        string? search, int[]? userIds = null);
    Task<List<TimeEntry>> GetAllAsync();
    Task<TimeEntry?> GetActiveTimerAsync(int userId);
    Task<List<TimeEntry>> GetAllActiveTimersAsync();
    Task<TimeEntry> AddAsync(TimeEntry entry);
    Task<TimeEntry?> UpdateAsync(TimeEntry entry);
    Task<bool> DeleteAsync(int id);
    Task<bool> HardDeleteAsync(int id);

    // 垃圾箱
    Task<List<TimeEntry>> GetTrashAsync(int page, int pageSize, string? keyword);
    Task<int> CountTrashAsync(string? keyword = null);
    Task<bool> RestoreAsync(int id);
    Task<int> ClearTrashAsync();

    // 统计
    Task<int> GetTotalMinutesAsync(int? userId, DateTime? startDate, DateTime? endDate, int[]? userIds = null);
    Task<int> GetBillableMinutesAsync(int? userId, DateTime? startDate, DateTime? endDate, int[]? userIds = null);
    Task<int> GetCountAsync(int? userId, DateTime? startDate, DateTime? endDate, int[]? userIds = null);

    /// <summary>
    /// TT-022: 单次查询返回 Overview 所需的全部统计（7 项合并为 1 次 DB 往返）
    /// </summary>
    Task<TimeEntryOverviewStats> GetOverviewStatsAsync(
        DateTime todayStart, DateTime todayEnd,
        DateTime weekStart, DateTime weekEnd,
        DateTime monthStart, DateTime monthEnd,
        int[]? userIds = null);
    Task<List<(string Label, int TotalMinutes, int Count, int BillableMinutes)>> GetByUserAsync(DateTime startDate, DateTime endDate, int? userId = null, int[]? userIds = null);
    Task<List<(string Label, int TotalMinutes, int Count, int BillableMinutes)>> GetByConversationAsync(DateTime startDate, DateTime endDate, int? userId = null, int[]? userIds = null);
    Task<List<(string Label, int TotalMinutes, int Count, int BillableMinutes)>> GetByDateAsync(DateTime startDate, DateTime endDate, int? userId = null, int[]? userIds = null);
}
