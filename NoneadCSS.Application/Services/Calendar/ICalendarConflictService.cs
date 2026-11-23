#nullable enable
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Exceptions;

namespace NoneadCSS.Application.Services.Calendar;

/// <summary>
/// 日历事件冲突检测结果（公开返回对象）。
/// 与原 CalendarService 私有嵌套类 ConflictDetectionResult 等价，
/// 提升为顶级类型后可在 Query/Mutation/Facade 多层共享，
/// 且避免了 4 个子服务之间的循环耦合 —— 冲突逻辑只在 ConflictService 维护一份。
/// </summary>
public sealed class CalendarConflictDetectionResult
{
    public List<ConflictEventInfo> Conflicts { get; set; } = new();
    public int MaxOverlapMinutes { get; set; }
    public bool IsHardConflict { get; set; }
}

public interface ICalendarConflictService
{
    /// <summary>
    /// 执行服务端硬/软冲突检测。与前端 hooks.ts 保持一致：
    /// - 跳过全天事件、已取消事件
    /// - 重叠 ≤ 15 min = soft（仅返回结果，不抛异常）
    /// - 重叠 &gt; 15 min = hard（IsHardConflict=true，由调用方决定是否抛 CalendarConflictException）
    /// </summary>
    /// <param name="startAt">待检测事件开始时间（UTC）</param>
    /// <param name="endAt">待检测事件结束时间（UTC，null 视为 startAt + 1h）</param>
    /// <param name="userId">事件归属用户 ID</param>
    /// <param name="excludeEventId">需排除的事件 ID（更新/恢复场景排除自身及其子实例）</param>
    /// <param name="isAllDay">待检测事件是否为全天事件（全天直接跳过，返回 null）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>检测结果。null = 无冲突/跳过检测；非 null = 至少存在 1 条软/硬冲突</returns>
    Task<CalendarConflictDetectionResult?> CheckAsync(
        DateTime startAt,
        DateTime? endAt,
        int? userId,
        int? excludeEventId,
        bool isAllDay,
        CancellationToken ct = default);

    /// <summary>
    /// 检测硬冲突并在命中时抛 CalendarConflictException（HTTP 409）。
    /// 软冲突仅记录 Warning 日志，不阻断操作。
    /// </summary>
    Task EnforceHardConflictAsync(
        DateTime startAt,
        DateTime? endAt,
        int? userId,
        int? excludeEventId,
        bool isAllDay,
        CancellationToken ct = default);

    /// <summary>
    /// 重复日程创建时的冲突预检——展开前 10 个实例逐一检测。
    /// 任一实例命中硬冲突即拒绝整个创建。
    /// </summary>
    Task EnforceRecurringCreateConflictAsync(
        DateTime startAt,
        DateTime? endAt,
        int? userId,
        CreateCalendarEventRequest request,
        CancellationToken ct = default);
}
