#nullable enable
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Metrics;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services.Calendar;

/// <summary>
/// 重复日程展开服务。
/// P4 改进：DST/时区边界处理。
/// - 所有时间戳均以 UTC 标准化存储与计算（DateTimeKind.Utc），避免跨时区偏移。
/// - DST 转换检测：当展开实例跨越夏令时边界时，记录警告日志便于排查"墙钟时间偏移"问题。
/// - 局限：当前实现基于 UTC 绝对时间递增，不感知本地时区的 DST 转换规则。
///   例如：UTC+8 时区 9:00 AM 事件在 DST 结束后（UTC+7）将显示为 8:00 AM 本地时间。
///   完整修复需引入 RFC 5545 RRULE + TZID 解析（如 Ical.Net 库），按本地墙钟时间展开后再转 UTC。
///   当前方案以 UTC 标准化作为兜底，保证数据一致性；DST 警告日志帮助定位边界问题。
/// </summary>
public class RecurrenceExpansionService : IRecurrenceExpansionService
{
    private readonly IConfiguration? _configuration;
    private readonly ILogger<RecurrenceExpansionService>? _logger;
    /// <summary>本地时区（用于 DST 检测日志），默认 China Standard Time (UTC+8，无 DST)</summary>
    private readonly TimeZoneInfo? _localTimeZone;

    public RecurrenceExpansionService(IConfiguration? configuration = null, ILogger<RecurrenceExpansionService>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
        // 尝试加载配置的时区；失败则使用本地系统时区
        var tzId = configuration?["Calendar:RecurrenceTimeZone"];
        if (!string.IsNullOrEmpty(tzId))
        {
            try { _localTimeZone = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch (TimeZoneNotFoundException) { /* 忽略，使用 null 表示不检测 DST */ }
        }
    }

    public List<CalendarEventDto> ExpandSeries(List<CalendarEventDto> events, DateTime startDate, DateTime endDate)
    {
        var result = new List<CalendarEventDto>();
        var maxInstances = _configuration?.GetValue<int?>("Calendar:MaxRecurrenceInstances") ?? 365;
        var maxTotalInstances = _configuration?.GetValue<int?>("Calendar:MaxExpandedInstances") ?? 5000;

        foreach (var evt in events)
        {
            if (!evt.IsRecurring || evt.RecurrenceParentId != null)
            {
                result.Add(evt);
                continue;
            }

            if (result.Count >= maxTotalInstances)
            {
                _logger?.LogWarning("展开重复事件达到总上限 {MaxTotal}，后续事件不再展开", maxTotalInstances);
                break;
            }

            var instances = GenerateRecurrenceInstances(evt, startDate, endDate, maxInstances);
            result.AddRange(instances);

            // P3 监控指标：记录本次展开生成的实例数（source=realtime 表示实时展开，未走缓存层）
            CalendarMetrics.RecordRecurrenceExpansion(instances.Count, "realtime");
        }

        return result;
    }

    public DateTime GetNextOccurrenceStart(CalendarEvent evt, DateTime now, DateTime scanWindow)
    {
        var start = evt.StartAt;
        var interval = evt.RecurrenceInterval ?? 1;
        if (interval <= 0) interval = 1;
        var recurrenceType = evt.RecurrenceType ?? 0;
        var endDate = evt.RecurrenceEndDate;
        var recurrenceCount = evt.RecurrenceCount ?? 0;

        // FIX: Check the FIRST occurrence (evt.StartAt) before advancing to future ones.
        // The original code skipped the first occurrence entirely, causing recurring events'
        // first-occurrence reminders to never fire (especially for COUNT=1 events).
        var firstReminderTime = start.AddMinutes(-(evt.ReminderMinutes ?? 0));
        if (firstReminderTime <= now && start <= scanWindow.AddDays(1))
            return start;

        // If only 1 occurrence is expected (COUNT=1), no future occurrences exist.
        if (recurrenceCount > 0 && recurrenceCount <= 1)
            return DateTime.MinValue;

        if (start > scanWindow)
            return start;

        var current = start;
        var maxIterations = 366 * 10;
        var occurrenceIndex = 1; // start from 2nd occurrence (index=1)
        while (maxIterations-- > 0)
        {
            switch (recurrenceType)
            {
                case 0: current = current.AddDays(interval); break;
                case 1: current = current.AddDays(7 * interval); break;
                case 2: current = current.AddMonths(interval); break;
                case 3: current = current.AddYears(interval); break;
                default: return DateTime.MinValue;
            }
            occurrenceIndex++;

            if (endDate.HasValue && current > endDate.Value)
                return DateTime.MinValue;

            // Respect RecurrenceCount limit (COUNT=N means exactly N occurrences total)
            if (recurrenceCount > 0 && occurrenceIndex > recurrenceCount)
                return DateTime.MinValue;

            var reminderTime = current.AddMinutes(-(evt.ReminderMinutes ?? 0));
            if (reminderTime <= now && current <= scanWindow.AddDays(1))
                return current;

            if (current > scanWindow)
                return DateTime.MinValue;
        }

        return DateTime.MinValue;
    }

    private List<CalendarEventDto> GenerateRecurrenceInstances(CalendarEventDto parent, DateTime rangeStart, DateTime rangeEnd, int maxInstances)
    {
        var instances = new List<CalendarEventDto>();
        var currentStart = DateTime.Parse(parent.StartAt, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var endAt = string.IsNullOrEmpty(parent.EndAt) ? (DateTime?)null :
            DateTime.Parse(parent.EndAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var duration = endAt.HasValue ? endAt.Value - currentStart : TimeSpan.Zero;
        var recurrenceType = parent.RecurrenceType ?? 0;
        var interval = parent.RecurrenceInterval ?? 1;
        var recurrenceEndDate = string.IsNullOrEmpty(parent.RecurrenceEndDate) ? (DateTime?)null :
            DateTime.Parse(parent.RecurrenceEndDate, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var recurrenceCount = parent.RecurrenceCount;
        var count = 0;

        var excludedDates = new HashSet<string>();
        if (parent.RecurrenceExclusions?.Excluded != null)
        {
            foreach (var d in parent.RecurrenceExclusions.Excluded)
            {
                if (!string.IsNullOrEmpty(d))
                    excludedDates.Add(d);
            }
        }

        // P4: DST 边界检测——跟踪前一个实例的本地 UTC 偏移，偏移变化意味着跨越了 DST 转换
        TimeSpan? prevOffset = null;

        while (count < maxInstances)
        {
            if (recurrenceEndDate.HasValue && currentStart > recurrenceEndDate.Value)
                break;

            if (recurrenceCount.HasValue && count >= recurrenceCount.Value)
                break;

            if (currentStart > rangeEnd)
                break;

            // P4: 检测 DST 转换——仅当配置了本地时区时
            if (_localTimeZone != null)
            {
                var currentOffset = _localTimeZone.GetUtcOffset(currentStart);
                if (prevOffset.HasValue && currentOffset != prevOffset.Value)
                {
                    _logger?.LogWarning(
                        "重复日程展开跨越 DST 边界: event_id={EventId}, title={Title}, " +
                        "instance_utc={InstanceUtc}, prev_offset={PrevOffset}, current_offset={CurrentOffset}, " +
                        "local_time_drift={DriftMinutes}min. 注意：UTC 标准化保证数据一致性，但本地墙钟时间可能偏移。",
                        parent.Id, parent.Title, currentStart.ToString("o"),
                        prevOffset.Value, currentOffset,
                        (currentOffset - prevOffset.Value).TotalMinutes);
                }
                prevOffset = currentOffset;
            }

            if (currentStart + duration >= rangeStart && currentStart <= rangeEnd)
            {
                if (!excludedDates.Contains(currentStart.ToString("yyyy-MM-dd")))
                {
                    var instance = new CalendarEventDto
                    {
                        Id = parent.Id,
                        Title = parent.Title,
                        Description = parent.Description,
                        EventType = parent.EventType,
                        EventTypeLabel = parent.EventTypeLabel,
                        StartAt = currentStart.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        EndAt = duration > TimeSpan.Zero ? (currentStart + duration).ToString("yyyy-MM-ddTHH:mm:ssZ") : null,
                        IsAllDay = parent.IsAllDay,
                        ConversationId = parent.ConversationId,
                        ConversationSubject = parent.ConversationSubject,
                        MailboxId = parent.MailboxId,
                        MailboxName = parent.MailboxName,
                        UserId = parent.UserId,
                        UserName = parent.UserName,
                        Status = parent.Status,
                        StatusLabel = parent.StatusLabel,
                        Color = parent.Color,
                        DeletedAt = parent.DeletedAt,
                        IsRecurring = true,
                        RecurrenceType = parent.RecurrenceType,
                        RecurrenceInterval = parent.RecurrenceInterval,
                        RecurrenceEndDate = parent.RecurrenceEndDate,
                        RecurrenceCount = parent.RecurrenceCount,
                        RecurrenceParentId = parent.Id,
                        RecurrenceOriginalStart = currentStart.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ReminderMinutes = parent.ReminderMinutes,
                        IsReminderSent = parent.IsReminderSent,
                        ReminderEmails = parent.ReminderEmails,
                        CreatedBy = parent.CreatedBy,
                        CreatedByName = parent.CreatedByName,
                        Priority = parent.Priority,
                        Progress = parent.Progress,
                        Location = parent.Location,
                        MeetingUrl = parent.MeetingUrl,
                        Attendees = parent.Attendees ?? new List<int>(),
                        SubTasks = parent.SubTasks ?? new List<SubTaskDto>(),
                        RelatedEventId = parent.RelatedEventId,
                        RecurrenceExclusions = parent.RecurrenceExclusions,
                        HostUserIds = parent.HostUserIds ?? new List<int>(),
                        AgendaIds = parent.AgendaIds ?? new List<int>(),
                        TaskOwnerId = parent.TaskOwnerId,
                        TaskOwnerName = parent.TaskOwnerName,
                        DueAt = parent.DueAt,
                        ReminderType = parent.ReminderType,
                        WeekdaysSelected = parent.WeekdaysSelected ?? new List<int>(),
                        RecurrenceRule = parent.RecurrenceRule,
                        CreatedAt = parent.CreatedAt,
                        UpdatedAt = parent.UpdatedAt,
                        RowVersion = parent.RowVersion
                    };
                    instances.Add(instance);
                }
            }

            count++;

            switch (recurrenceType)
            {
                case 0: currentStart = currentStart.AddDays(interval); break;
                case 1: currentStart = currentStart.AddDays(7 * interval); break;
                case 2: currentStart = currentStart.AddMonths(interval); break;
                case 3: currentStart = currentStart.AddYears(interval); break;
                default:
                    _logger?.LogWarning("未知的重复类型 {RecurrenceType}，事件 ID={EventId}", recurrenceType, parent.Id);
                    return instances;
            }
        }

        return instances;
    }
}
