#nullable enable
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Exceptions;
using NoneadCSS.Application.Metrics;
using NoneadCSS.Domain;

namespace NoneadCSS.Application.Services.Calendar;

/// <summary>
/// P3 E#1 新拆分出的冲突检测服务（原 CalendarService 私有方法升为公共服务）。
/// 单一职责：同一用户的「非全天、非 cancelled」事件是否与给定时间窗口重叠，
/// 区分 soft（≤15 min，日志 + 允许）和 hard（&gt;15 min，抛 CalendarConflictException）两档。
/// 调用方：CalendarMutationService.Create/Update/Restore 、CalendarService Facade 回退兼容、未来 BatchOperation 细化校验。
/// </summary>
public class CalendarConflictService : ICalendarConflictService
{
    /// <summary>硬冲突阈值（分钟），与前端 SOFT_THRESHOLD_MIN 保持一致；重叠超过此值默认拒绝。</summary>
    public const int HardThresholdMinutes = 15;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CalendarConflictService> _logger;

    public CalendarConflictService(IUnitOfWork unitOfWork, ILogger<CalendarConflictService> logger)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(logger);
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CalendarConflictDetectionResult?> CheckAsync(
        DateTime startAt,
        DateTime? endAt,
        int? userId,
        int? excludeEventId,
        bool isAllDay,
        CancellationToken ct = default)
    {
        if (isAllDay) return null;
        if (!userId.HasValue) return null;

        var effectiveEnd = endAt ?? startAt.AddHours(1);
        if (effectiveEnd <= startAt) return null;

        // P4 改进：将重叠计算下推到 SQL 层。
        // 旧方案：±1 天缓冲加载候选到内存，再逐个计算重叠（事件密度高时加载过多）。
        // 新方案：精确重叠条件 e.StartAt < effectiveEnd AND (e.EndAt > startAt OR e.EndAt IS NULL) 直接在 SQL 过滤，
        //         仅对真正重叠的事件在内存中计算重叠分钟数（用于 soft/hard 分档）。
        // 保留 ±1 天缓冲的 WHERE 条件作为安全网（吸收 ICS 导入等边界偏差），但通过精确重叠条件大幅减少内存候选集。
        var queryStart = startAt.AddDays(-1);
        var queryEnd = effectiveEnd.AddDays(1);

        var query = _unitOfWork.CalendarEvents.GetQueryable(true);
        var candidates = await query
            .Where(e => e.UserId == userId.Value
                        && !e.IsAllDay
                        && e.Status != "cancelled"
                        && e.DeletedAt == null
                        && e.StartAt < queryEnd
                        && (e.EndAt == null || e.EndAt > queryStart)
                        // P4: SQL 下推精确重叠条件——仅加载真正与新事件时间窗口重叠的候选
                        && e.StartAt < effectiveEnd
                        && (e.EndAt == null || e.EndAt > startAt))
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var conflicts = new List<ConflictEventInfo>();
        var maxOverlapMin = 0;

        foreach (var ev in candidates)
        {
            if (excludeEventId.HasValue && ev.Id == excludeEventId.Value)
                continue;
            // 同一重复系列的「父排除项」对应所有子实例互斥：它们的时间本不应重叠
            if (excludeEventId.HasValue && ev.RecurrenceParentId == excludeEventId.Value)
                continue;

            var evEnd = ev.EndAt ?? ev.StartAt.AddHours(1);
            var overlapStart = startAt > ev.StartAt ? startAt : ev.StartAt;
            var overlapEnd = effectiveEnd < evEnd ? effectiveEnd : evEnd;
            var overlapMs = (overlapEnd - overlapStart).TotalMilliseconds;
            if (!(overlapMs > 0)) continue;

            var overlapMin = (int)(overlapMs / 60000);
            if (overlapMin > maxOverlapMin) maxOverlapMin = overlapMin;

            conflicts.Add(new ConflictEventInfo
            {
                Id = ev.Id,
                Title = ev.Title,
                StartAt = ev.StartAt,
                EndAt = ev.EndAt
            });
        }

        if (conflicts.Count == 0) return null;

        var isHard = maxOverlapMin > HardThresholdMinutes;
        // P3 监控指标：记录冲突检测结果（severity=soft/hard）
        CalendarMetrics.RecordConflict(isHard ? "hard" : "soft");

        return new CalendarConflictDetectionResult
        {
            Conflicts = conflicts,
            MaxOverlapMinutes = maxOverlapMin,
            IsHardConflict = isHard
        };
    }

    public async Task EnforceHardConflictAsync(
        DateTime startAt,
        DateTime? endAt,
        int? userId,
        int? excludeEventId,
        bool isAllDay,
        CancellationToken ct = default)
    {
        var result = await CheckAsync(startAt, endAt, userId, excludeEventId, isAllDay, ct).ConfigureAwait(false);
        if (result == null) return;

        if (result.IsHardConflict)
        {
            throw new CalendarConflictException(result.Conflicts, result.MaxOverlapMinutes);
        }

        _logger.LogWarning(
            "日历事件软冲突: user_id={UserId}, overlap_minutes={OverlapMin}, threshold={Threshold}min, conflicts={ConflictCount}, conflict_titles={Titles}",
            userId, result.MaxOverlapMinutes, HardThresholdMinutes, result.Conflicts.Count,
            string.Join(", ", result.Conflicts.Select(c => c.Title)));
    }

    public async Task EnforceRecurringCreateConflictAsync(
        DateTime startAt,
        DateTime? endAt,
        int? userId,
        CreateCalendarEventRequest request,
        CancellationToken ct = default)
    {
        if (!userId.HasValue || request.IsAllDay) return;

        var duration = endAt.HasValue ? endAt.Value - startAt : TimeSpan.FromHours(1);
        var interval = request.RecurrenceInterval ?? 1;
        // P2 改进：按重复类型动态调整预检数量，覆盖月视图常见范围
        var recurrenceType = request.RecurrenceType ?? 0;
        var maxCheck = recurrenceType switch
        {
            0 => 30,  // 每日：预检 30 天（覆盖月视图）
            1 => 12,  // 每周：预检 12 周（≈84 天）
            2 => 6,   // 每月：预检 6 个月
            3 => 3,   // 每年：预检 3 年
            _ => 10
        };

        // 1. 预生成所有实例的时间窗口
        var windows = new List<(DateTime Start, DateTime End)>();
        var currentStart = startAt;
        var recurrenceEndDate = string.IsNullOrEmpty(request.RecurrenceEndDate)
            ? null
            : (DateTime?)ParseDate(request.RecurrenceEndDate);
        var recurrenceCount = request.RecurrenceCount;

        for (var i = 0; i < maxCheck; i++)
        {
            if (recurrenceEndDate.HasValue && currentStart > recurrenceEndDate.Value) break;
            if (recurrenceCount.HasValue && i >= recurrenceCount.Value) break;

            windows.Add((currentStart, currentStart + duration));

            switch (recurrenceType)
            {
                case 0: currentStart = currentStart.AddDays(interval); break;        // 每日
                case 1: currentStart = currentStart.AddDays(7 * interval); break;   // 每周
                case 2: currentStart = currentStart.AddMonths(interval); break;     // 每月
                case 3: currentStart = currentStart.AddYears(interval); break;      // 每年
                default: return;
            }
        }

        if (windows.Count == 0) return;

        // 2. 一次性查询所有候选事件（±1 天缓冲吸收时区偏移）
        // P4 改进：添加精确重叠条件，减少内存候选集
        var minStart = windows.Min(w => w.Start).AddDays(-1);
        var maxEnd = windows.Max(w => w.End).AddDays(1);
        var candidates = await _unitOfWork.CalendarEvents.GetQueryable(true)
            .Where(e => e.UserId == userId.Value
                        && !e.IsAllDay
                        && e.Status != "cancelled"
                        && e.DeletedAt == null
                        && e.StartAt < maxEnd
                        && (e.EndAt == null || e.EndAt > minStart))
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 3. 内存中按窗口逐个检查硬冲突
        foreach (var (wStart, wEnd) in windows)
        {
            var maxOverlapMin = 0;
            var conflictEvents = new List<ConflictEventInfo>();

            foreach (var ev in candidates)
            {
                var evEnd = ev.EndAt ?? ev.StartAt.AddHours(1);
                var overlapStart = wStart > ev.StartAt ? wStart : ev.StartAt;
                var overlapEnd = wEnd < evEnd ? wEnd : evEnd;
                var overlapMs = (overlapEnd - overlapStart).TotalMilliseconds;
                if (!(overlapMs > 0)) continue;

                var overlapMin = (int)(overlapMs / 60000);
                if (overlapMin > maxOverlapMin) maxOverlapMin = overlapMin;

                conflictEvents.Add(new ConflictEventInfo
                {
                    Id = ev.Id,
                    Title = ev.Title,
                    StartAt = ev.StartAt,
                    EndAt = ev.EndAt
                });
            }

            if (maxOverlapMin > HardThresholdMinutes)
            {
                // P3 监控指标：重复日程创建预检发现硬冲突
                CalendarMetrics.RecordConflict("hard");
                throw new CalendarConflictException(conflictEvents, maxOverlapMin);
            }
        }
    }

    private static DateTime ParseDate(string s)
    {
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var result))
        {
            throw new ArgumentException($"日期格式无效：{s}");
        }
        return DateTime.SpecifyKind(result, DateTimeKind.Utc);
    }
}
