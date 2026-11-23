namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// TT-022: Overview 统计单次查询结果（7 项合并为 1 次 DB 往返）
/// </summary>
public record TimeEntryOverviewStats(
    int TodayMinutes,
    int WeekMinutes,
    int MonthMinutes,
    int TotalMinutes,
    int TodayCount,
    int WeekCount,
    int BillableMinutes);
