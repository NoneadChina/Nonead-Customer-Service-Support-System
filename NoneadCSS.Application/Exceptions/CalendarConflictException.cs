namespace NoneadCSS.Application.Exceptions;

/// <summary>
/// 日历事件时间冲突异常（硬冲突：重叠时间超过阈值）。
/// 由 CalendarService.CreateAsync / UpdateAsync 在检测到服务端冲突时抛出，
/// GlobalExceptionMiddleware 映射为 HTTP 409 Conflict。
/// </summary>
public class CalendarConflictException : Exception
{
    /// <summary>
    /// 冲突的事件列表（id + title + startAt + endAt）
    /// </summary>
    public List<ConflictEventInfo> Conflicts { get; }

    /// <summary>
    /// 最大重叠分钟数
    /// </summary>
    public int OverlapMinutes { get; }

    public CalendarConflictException(List<ConflictEventInfo> conflicts, int overlapMinutes)
        : base($"时间冲突：与 {conflicts.Count} 个已有事件重叠 {overlapMinutes} 分钟（超过硬冲突阈值）")
    {
        Conflicts = conflicts;
        OverlapMinutes = overlapMinutes;
    }
}

/// <summary>
/// 冲突事件摘要信息
/// </summary>
public class ConflictEventInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
}
