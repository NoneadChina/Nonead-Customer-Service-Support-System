using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 日历事件 ICalendar (.ics) 导入/导出服务实现。
/// 遵循 RFC 5545 基本格式，不引入第三方库。
/// T000096 修复：使用 X-NONEAD-* 扩展属性保存完整字段，确保导出/导入数据一致。
/// </summary>
public class CalendarExportService : ICalendarExportService
{
    private const string ProdId = "-//NoneadCSS//Calendar//EN";
    // T000098 修复：添加 METHOD:PUBLISH 属性。
    // RFC 5545 规定 METHOD 描述 iCalendar 对象的日历方法，PUBLISH 用于发布事件，
    // Outlook 订阅源需要此属性才能正确识别并解析订阅 ICS。
    // T000098 v2：添加 X-WR-CALNAME 和 X-WR-TIMEZONE 属性，提高 Outlook 兼容性。
    // X-WR-CALNAME：日历显示名称（Outlook/Apple 日历用此名称标识订阅源）
    // X-WR-TIMEZONE：默认时区（Outlook 用此解析浮动时间，避免事件时间漂移）
    private const string CalendarHeader =
        "BEGIN:VCALENDAR\r\n" +
        "VERSION:2.0\r\n" +
        $"PRODID:{ProdId}\r\n" +
        "CALSCALE:GREGORIAN\r\n" +
        "METHOD:PUBLISH\r\n" +
        "X-WR-CALNAME:NoneadCSS 日历\r\n" +
        "X-WR-TIMEZONE:UTC\r\n";
    private const string CalendarFooter = "END:VCALENDAR\r\n";

    // T000096: X- 前缀扩展属性命名空间（RFC 5545 允许自定义 X- 属性）
    private const string XPrefix = "X-NONEAD-";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICalendarService _calendarService;
    private readonly ILogger<CalendarExportService>? _logger;
    private readonly IConfiguration? _configuration;

    public CalendarExportService(IUnitOfWork unitOfWork, ICalendarService calendarService, ILogger<CalendarExportService>? logger = null, IConfiguration? configuration = null)
    {
        _unitOfWork = unitOfWork;
        _calendarService = calendarService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<string> ExportEventAsync(int eventId)
    {
        var evt = await _unitOfWork.CalendarEvents.GetByIdAsync(eventId);
        if (evt == null || evt.DeletedAt != null)
        {
            return WrapCalendar(string.Empty);
        }

        return WrapCalendar(BuildVEvent(BuildExportDataFromEntity(evt)));
    }

    /// <inheritdoc />
    public async Task<string> ExportRangeAsync(DateTime startDate, DateTime endDate, int? mailboxId, int? userId = null)
    {
        // 使用 ICalendarService.GetListAsync 以复用权限过滤逻辑（ResolveAuthorizedUserIdsAsync）。
        // ExpandRecurring = false：导出原始事件而非展开后的虚拟实例，避免相同 Id 重复出现在 .ics 中。
        var pageSize = _configuration?.GetValue<int?>("Calendar:ExportPageSize") ?? 200;
        var page = 1;
        var sb = new StringBuilder();

        while (true)
        {
            var query = new CalendarEventQuery
            {
                StartDate = startDate,
                EndDate = endDate,
                MailboxId = mailboxId,
                UserId = userId,
                Page = page,
                PageSize = pageSize,
                ExpandRecurring = false
            };

            var (items, total) = await _calendarService.GetListAsync(query);
            if (items.Count == 0) break;

            foreach (var dto in items)
            {
                sb.Append(BuildVEvent(BuildExportDataFromDto(dto)));
            }

            if (page * pageSize >= total) break;
            page++;
            // 防御性上限，避免异常情况下无限循环
            if (page > 1000) break;
        }

        return WrapCalendar(sb.ToString());
    }

    /// <inheritdoc />
    public async Task<string> ExportRangeForFeedAsync(DateTime startDate, DateTime endDate, int userId)
    {
        // H-38 修复：供订阅源（Feed）使用。匿名访问 Feed 时 _currentUserAccessor.UserId 为 0，
        // 经 CalendarService.GetListAsync -> ResolveAuthorizedUserIdsAsync 会得到空权限交集，返回空列表。
        // 此方法直接查询指定用户的事件，绕过权限交集过滤；调用方必须已通过 Token 鉴权。
        var pageSize = _configuration?.GetValue<int?>("Calendar:ExportPageSize") ?? 200;
        var page = 1;
        var sb = new StringBuilder();

        while (true)
        {
            // 直接调用仓储，userIds 传 null 表示不做权限交集，仅按 userId 过滤该用户自己的事件
            var items = await _unitOfWork.CalendarEvents.GetListAsync(
                startDate, endDate,
                null, userId, null,
                null, null, null,
                page, pageSize);

            if (items.Count == 0) break;

            foreach (var dto in items.Select(e => e.ToDto()))
            {
                sb.Append(BuildVEvent(BuildExportDataFromDto(dto)));
            }

            // 仓储 GetListAsync 不返回总数，用 CountAsync 获取以正确判断分页终止
            var total = await _unitOfWork.CalendarEvents.CountAsync(
                startDate, endDate,
                null, userId, null,
                null, null, null);

            if (page * pageSize >= total) break;
            page++;
            // 防御性上限，避免异常情况下无限循环
            if (page > 1000) break;
        }

        return WrapCalendar(sb.ToString());
    }

    /// <inheritdoc />
    public async Task<(int Imported, int Skipped)> ImportAsync(string icsContent, int? mailboxId)
    {
        if (string.IsNullOrWhiteSpace(icsContent))
        {
            return (0, 0);
        }

        // H-19：限制 ICS 内容大小（10MB），防止超大文件导致内存耗尽或解析 DoS
        const int maxIcsContentSize = 10 * 1024 * 1024;
        if (icsContent.Length > maxIcsContentSize)
        {
            throw new ArgumentException($"ICS 文件内容过大（{icsContent.Length} 字节），最大允许 {maxIcsContentSize} 字节");
        }

        // H-19：限制导入事件数量上限
        const int maxImportEvents = 1000;

        var imported = 0;
        var skipped = 0;

        // 标准化换行符，按行分割
        var normalized = icsContent.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');

        // 解析时处理行折叠：RFC 5545 中以空格/制表符开头的行属于上一行的续行
        var unfolded = UnfoldLines(lines);

        // 提取所有 VEVENT 块
        var events = ExtractVEvents(unfolded);
        if (events.Count == 0)
        {
            return (0, 0);
        }

        // H-19：校验事件数量上限
        if (events.Count > maxImportEvents)
        {
            throw new ArgumentException($"ICS 文件包含 {events.Count} 个事件，超过单次导入上限 {maxImportEvents}");
        }

        // H-26：事务包裹整个导入过程，任一事件创建失败时回滚以保证数据一致性
        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var index = 0;
            foreach (var eventLines in events)
            {
                index++;
                try
                {
                    var props = ParseEventProperties(eventLines);

                    // SUMMARY 必填（标题），DTSTART 必填（开始时间）
                    if (!props.TryGetValue("SUMMARY", out var title) || string.IsNullOrWhiteSpace(title))
                    {
                        _logger?.LogWarning("导入 VEVENT 跳过（第 {Index} 个）：缺少 SUMMARY", index);
                        skipped++;
                        continue;
                    }

                    if (!props.TryGetValue("DTSTART", out var dtStartRaw) || string.IsNullOrWhiteSpace(dtStartRaw))
                    {
                        _logger?.LogWarning("导入 VEVENT 跳过（第 {Index} 个）：缺少 DTSTART", index);
                        skipped++;
                        continue;
                    }

                    var (startAt, isAllDay) = ParseIcsDate(dtStartRaw);
                    if (startAt == null)
                    {
                        _logger?.LogWarning("导入 VEVENT 跳过（第 {Index} 个）：DTSTART 解析失败 ({Value})", index, dtStartRaw);
                        skipped++;
                        continue;
                    }

                    DateTime? endAt = null;
                    if (props.TryGetValue("DTEND", out var dtEndRaw) && !string.IsNullOrWhiteSpace(dtEndRaw))
                    {
                        var (parsedEnd, _) = ParseIcsDate(dtEndRaw);
                        // T000096 修复：全天事件 DTEND 对称处理
                        // 导出时 DTEND = EndAt + 1 天（RFC 5545 排他边界），导入时需减回 1 天
                        endAt = parsedEnd != null && isAllDay
                            ? parsedEnd.Value.AddDays(-1)
                            : parsedEnd;
                    }

                    props.TryGetValue("DESCRIPTION", out var description);
                    props.TryGetValue("LOCATION", out var location);
                    props.TryGetValue("STATUS", out var icsStatus);

                    // T000096: 优先从 X-NONEAD-STATUS 恢复完整状态（5 种），
                    // 回退到 RFC 标准 STATUS 的 3 种映射
                    string internalStatus;
                    if (props.TryGetValue($"{XPrefix}STATUS", out var xStatus) && !string.IsNullOrWhiteSpace(xStatus))
                    {
                        internalStatus = xStatus;
                    }
                    else
                    {
                        internalStatus = MapIcsStatusToInternal(icsStatus);
                    }

                    // T000096: 从 X- 属性恢复 EventType，默认 "task"
                    var eventType = props.TryGetValue($"{XPrefix}EVENTTYPE", out var et) && !string.IsNullOrWhiteSpace(et)
                        ? et : "task";

                    var request = new CreateCalendarEventRequest
                    {
                        Title = title,
                        Description = description,
                        EventType = eventType,
                        StartAt = startAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                        EndAt = endAt.HasValue
                            ? endAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                            : null,
                        IsAllDay = isAllDay,
                        MailboxId = mailboxId,
                        Location = location
                    };

                    // T000096: 恢复 X- 扩展属性中的字段
                    PopulateRequestFromXProps(request, props);

                    // T000096 v3: 修复 MailboxId 不对称问题
                    //   场景：原事件 MailboxId=null（无邮箱归属），导出时 AppendXProp 跳过空值，
                    //   .ics 中无 X-NONEAD-MAILBOXID。导入时若仅依赖 query 参数 mailboxId 兜底，
                    //   会被前端"启用第一个邮箱"覆盖为非 null 值，导致与原数据不匹配。
                    //   修复：如果 .ics 来自本系统（含 X-NONEAD-EVENTTYPE 标记）但未声明 MAILBOXID，
                    //   说明原 MailboxId 为 null，强制覆盖为 null（保持与原数据一致）。
                    //   非 X-NONEAD-* 文件（如 Outlook/iCloud 导出）保持 query 参数兜底行为。
                    if (props.ContainsKey($"{XPrefix}EVENTTYPE") &&
                        !props.ContainsKey($"{XPrefix}MAILBOXID"))
                    {
                        request.MailboxId = null;
                    }

                    var created = await _calendarService.CreateAsync(request);

                    // T000096 v3: 恢复 CreateAsync 无法直接设置的"不可变"字段
                    //   - CreatedBy / CreatedAt / RecurrenceParentId / RecurrenceOriginalStart
                    //   CreateCalendarEventRequest 不暴露这些字段（语义安全），
                    //   但导入场景下需要恢复原值以保持数据完全一致。
                    await RestoreImmutableFieldsAsync(created.Id, props);

                    // 状态更新：仅在状态非 pending 时执行
                    if (!string.Equals(internalStatus, "pending", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            await _calendarService.UpdateStatusAsync(created.Id, internalStatus);
                        }
                        catch (Exception ex)
                        {
                            // 状态更新失败不影响导入计数，仅记录日志
                            _logger?.LogWarning(ex, "导入 VEVENT（第 {Index} 个）状态更新失败，保持默认 pending", index);
                        }
                    }

                    imported++;
                }
                catch (Exception ex)
                {
                    // H-22/M-13：记录失败日志以便排查，区分错误类型
                    _logger?.LogWarning(ex, "导入 VEVENT 失败（第 {Index} 个），跳过", index);
                    skipped++;
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.SafeRollbackAsync();
            }
            _unitOfWork.ClearChanges();
            throw;
        }

        return (imported, skipped);
    }

    // ===== T000096: 导出数据载体 =====

    /// <summary>
    /// 导出数据载体，包含 CalendarEvent 实体的所有需要导出的字段。
    /// 从实体或 DTO 构造，统一传递给 BuildVEvent。
    /// T000096 v2 修复：补充 MailboxId / CreatedAt / CreatedBy / RelatedEventId /
    /// RecurrenceExclusions / RecurrenceParentId / RecurrenceOriginalStart 等遗漏字段，
    /// 确保导出/导入数据完全一致。
    /// </summary>
    private sealed record CalendarExportData(
        int Id,
        string Title,
        string? Description,
        DateTime StartAt,
        DateTime? EndAt,
        bool IsAllDay,
        string? Location,
        string Status,
        string EventType,
        int? ConversationId,
        int? MailboxId,
        int? UserId,
        int? Priority,
        int? Progress,
        string? MeetingUrl,
        string? Color,
        string? AttendeesJson,
        string? SubTasksJson,
        int? TaskOwnerId,
        DateTime? DueAt,
        int? ReminderMinutes,
        string? ReminderType,
        string? ReminderEmails,
        bool IsRecurring,
        int? RecurrenceType,
        int? RecurrenceInterval,
        DateTime? RecurrenceEndDate,
        int? RecurrenceCount,
        string? RecurrenceRule,
        string? WeekdaysSelectedJson,
        string? HostUserIdsJson,
        string? AgendaIdsJson,
        int? RelatedEventId,
        string? RecurrenceExclusionsJson,
        int? RecurrenceParentId,
        DateTime? RecurrenceOriginalStart,
        DateTime CreatedAt,
        int CreatedBy);

    /// <summary>
    /// 从 CalendarEvent 实体构造导出数据（ExportEventAsync 使用，含全部字段）
    /// </summary>
    private static CalendarExportData BuildExportDataFromEntity(Domain.Entities.CalendarEvent evt)
    {
        return new CalendarExportData(
            Id: evt.Id,
            Title: evt.Title,
            Description: evt.Description,
            StartAt: evt.StartAt,
            EndAt: evt.EndAt,
            IsAllDay: evt.IsAllDay,
            Location: evt.Location,
            Status: evt.Status,
            EventType: evt.EventType,
            ConversationId: evt.ConversationId,
            MailboxId: evt.MailboxId,
            UserId: evt.UserId,
            Priority: evt.Priority,
            Progress: evt.Progress,
            MeetingUrl: evt.MeetingUrl,
            Color: evt.Color,
            AttendeesJson: evt.AttendeesJson,
            SubTasksJson: evt.SubTasksJson,
            TaskOwnerId: evt.TaskOwnerId,
            DueAt: evt.DueAt,
            ReminderMinutes: evt.ReminderMinutes,
            ReminderType: evt.ReminderType,
            ReminderEmails: evt.ReminderEmails,
            IsRecurring: evt.IsRecurring,
            RecurrenceType: evt.RecurrenceType,
            RecurrenceInterval: evt.RecurrenceInterval,
            RecurrenceEndDate: evt.RecurrenceEndDate,
            RecurrenceCount: evt.RecurrenceCount,
            RecurrenceRule: evt.RecurrenceRule,
            WeekdaysSelectedJson: evt.WeekdaysSelectedJson,
            HostUserIdsJson: evt.HostUserIdsJson,
            AgendaIdsJson: evt.AgendaIdsJson,
            RelatedEventId: evt.RelatedEventId,
            RecurrenceExclusionsJson: evt.RecurrenceExclusionsJson,
            RecurrenceParentId: evt.RecurrenceParentId,
            RecurrenceOriginalStart: evt.RecurrenceOriginalStart,
            CreatedAt: evt.CreatedAt,
            CreatedBy: evt.CreatedBy);
    }

    /// <summary>
    /// 从 CalendarEventDto 构造导出数据（ExportRangeAsync 使用，DTO 字段略少于实体）
    /// </summary>
    private static CalendarExportData BuildExportDataFromDto(CalendarEventDto dto)
    {
        // DTO 中的 attendees/subTasks/hostUserIds/agendaIds/weekdaysSelected 是 List<int>/List<SubTaskDto>
        // 需序列化为 JSON 字符串以与实体字段对齐
        var attendeesJson = dto.Attendees.Count > 0
            ? JsonSerializer.Serialize(dto.Attendees) : null;
        var subTasksJson = dto.SubTasks.Count > 0
            ? JsonSerializer.Serialize(dto.SubTasks) : null;
        var hostUserIdsJson = dto.HostUserIds.Count > 0
            ? JsonSerializer.Serialize(dto.HostUserIds) : null;
        var agendaIdsJson = dto.AgendaIds.Count > 0
            ? JsonSerializer.Serialize(dto.AgendaIds) : null;
        var weekdaysSelectedJson = dto.WeekdaysSelected.Count > 0
            ? JsonSerializer.Serialize(dto.WeekdaysSelected) : null;
        var recurrenceExclusionsJson = dto.RecurrenceExclusions != null
            ? JsonSerializer.Serialize(dto.RecurrenceExclusions) : null;

        // DTO 中 StartAt/EndAt/RecurrenceEndDate/DueAt/CreatedAt 是字符串，需解析为 DateTime
        var startAt = ParseDtoDate(dto.StartAt);
        DateTime? endAt = string.IsNullOrEmpty(dto.EndAt) ? null : ParseDtoDate(dto.EndAt);
        DateTime? recurrenceEndDate = string.IsNullOrEmpty(dto.RecurrenceEndDate)
            ? null : ParseDtoDate(dto.RecurrenceEndDate);
        DateTime? dueAt = string.IsNullOrEmpty(dto.DueAt) ? null : ParseDtoDate(dto.DueAt);
        DateTime? recurrenceOriginalStart = string.IsNullOrEmpty(dto.RecurrenceOriginalStart)
            ? null : ParseDtoDate(dto.RecurrenceOriginalStart);
        var createdAt = ParseDtoDate(dto.CreatedAt);

        return new CalendarExportData(
            Id: dto.Id,
            Title: dto.Title,
            Description: dto.Description,
            StartAt: startAt,
            EndAt: endAt,
            IsAllDay: dto.IsAllDay,
            Location: dto.Location,
            Status: dto.Status,
            EventType: dto.EventType,
            ConversationId: dto.ConversationId,
            MailboxId: dto.MailboxId,
            UserId: dto.UserId,
            Priority: dto.Priority,
            Progress: dto.Progress,
            MeetingUrl: dto.MeetingUrl,
            Color: dto.Color,
            AttendeesJson: attendeesJson,
            SubTasksJson: subTasksJson,
            TaskOwnerId: dto.TaskOwnerId,
            DueAt: dueAt,
            ReminderMinutes: dto.ReminderMinutes,
            ReminderType: dto.ReminderType,
            ReminderEmails: dto.ReminderEmails,
            IsRecurring: dto.IsRecurring,
            RecurrenceType: dto.RecurrenceType,
            RecurrenceInterval: dto.RecurrenceInterval,
            RecurrenceEndDate: recurrenceEndDate,
            RecurrenceCount: dto.RecurrenceCount,
            RecurrenceRule: dto.RecurrenceRule,
            WeekdaysSelectedJson: weekdaysSelectedJson,
            HostUserIdsJson: hostUserIdsJson,
            AgendaIdsJson: agendaIdsJson,
            RelatedEventId: dto.RelatedEventId,
            RecurrenceExclusionsJson: recurrenceExclusionsJson,
            RecurrenceParentId: dto.RecurrenceParentId,
            RecurrenceOriginalStart: recurrenceOriginalStart,
            CreatedAt: createdAt,
            CreatedBy: dto.CreatedBy);
    }

    // ===== 私有辅助方法 =====

    private static string WrapCalendar(string vevents)
    {
        return CalendarHeader + vevents + CalendarFooter;
    }

    /// <summary>
    /// T000096 修复后的 BuildVEvent：输出标准 RFC 5545 属性 + X-NONEAD-* 扩展属性
    /// </summary>
    private static string BuildVEvent(CalendarExportData d)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:{d.Id}@noneadcss\r\n");
        sb.Append($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}\r\n");
        // T000098 v2：添加 Outlook 兼容性属性
        // SEQUENCE：事件修订序列号（默认 0），Outlook 用此判断事件是否更新
        // LAST-MODIFIED：最后修改时间（RFC 5545 可选），Outlook 用此同步增量
        // TRANSP：透明度（OPAQUE=忙碌/TRANSPARENT=空闲），Outlook 用此显示空闲/忙碌状态
        sb.Append("SEQUENCE:0\r\n");
        sb.Append($"LAST-MODIFIED:{ToUtcSafe(d.CreatedAt):yyyyMMddTHHmmssZ}\r\n");
        sb.Append("TRANSP:OPAQUE\r\n");

        if (d.IsAllDay)
        {
            sb.Append($"DTSTART;VALUE=DATE:{d.StartAt:yyyyMMdd}\r\n");
            if (d.EndAt.HasValue)
            {
                // RFC 5545: DTEND 为全天事件结束日的次日（排他边界）
                sb.Append($"DTEND;VALUE=DATE:{d.EndAt.Value.AddDays(1):yyyyMMdd}\r\n");
            }
        }
        else
        {
            // T000096 时区修复：用 ToUtcSafe 代替 ToUniversalTime，避免 SQLite 读回的 Unspecified 被二次转换
            var startUtc = ToUtcSafe(d.StartAt);
            sb.Append($"DTSTART:{startUtc:yyyyMMddTHHmmssZ}\r\n");
            if (d.EndAt.HasValue)
            {
                var endUtc = ToUtcSafe(d.EndAt.Value);
                sb.Append($"DTEND:{endUtc:yyyyMMddTHHmmssZ}\r\n");
            }
        }

        if (!string.IsNullOrWhiteSpace(d.Title))
        {
            sb.Append($"SUMMARY:{EscapeText(d.Title)}\r\n");
        }
        if (!string.IsNullOrWhiteSpace(d.Description))
        {
            sb.Append($"DESCRIPTION:{EscapeText(d.Description)}\r\n");
        }
        if (!string.IsNullOrWhiteSpace(d.Location))
        {
            sb.Append($"LOCATION:{EscapeText(d.Location)}\r\n");
        }

        // T000096: STATUS 输出 RFC 标准 3 种 + X-NONEAD-STATUS 保存完整 5 种
        var icsStatus = MapInternalStatusToIcs(d.Status);
        if (!string.IsNullOrEmpty(icsStatus))
        {
            sb.Append($"STATUS:{icsStatus}\r\n");
        }
        // 始终输出 X-NONEAD-STATUS 以保存完整状态值（含 in_progress/missed）
        sb.Append($"{XPrefix}STATUS:{EscapeText(d.Status)}\r\n");

        // ===== T000096: X-NONEAD-* 扩展属性（完整字段导出） =====
        AppendXProp(sb, "EVENTTYPE", d.EventType);
        AppendXProp(sb, "MAILBOXID", d.MailboxId);
        AppendXProp(sb, "CONVERSATIONID", d.ConversationId);
        AppendXProp(sb, "USERID", d.UserId);
        AppendXProp(sb, "PRIORITY", d.Priority);
        AppendXProp(sb, "PROGRESS", d.Progress);
        AppendXProp(sb, "MEETINGURL", d.MeetingUrl);
        AppendXProp(sb, "COLOR", d.Color);
        AppendXProp(sb, "ATTENDEES", d.AttendeesJson);
        AppendXProp(sb, "SUBTASKS", d.SubTasksJson);
        AppendXProp(sb, "TASKOWNERID", d.TaskOwnerId);
        AppendXProp(sb, "DUEAT", d.DueAt.HasValue
            ? ToUtcSafe(d.DueAt.Value).ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
            : null);
        AppendXProp(sb, "REMINDERMINUTES", d.ReminderMinutes);
        AppendXProp(sb, "REMINDERTYPE", d.ReminderType);
        AppendXProp(sb, "REMINDEREMAILS", d.ReminderEmails);
        AppendXProp(sb, "ISRECURRING", d.IsRecurring ? "true" : "false");
        AppendXProp(sb, "RECURRENCETYPE", d.RecurrenceType);
        AppendXProp(sb, "RECURRENCEINTERVAL", d.RecurrenceInterval);
        AppendXProp(sb, "RECURRENCEENDDATE",
            d.RecurrenceEndDate.HasValue
                ? ToUtcSafe(d.RecurrenceEndDate.Value).ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
                : null);
        AppendXProp(sb, "RECURRENCECOUNT", d.RecurrenceCount);
        AppendXProp(sb, "RECURRENCERULE", d.RecurrenceRule);
        AppendXProp(sb, "WEEKDAYSSELECTED", d.WeekdaysSelectedJson);
        AppendXProp(sb, "HOSTUSERIDS", d.HostUserIdsJson);
        AppendXProp(sb, "AGENDAIDS", d.AgendaIdsJson);
        // T000096 v2: 补充遗漏字段
        AppendXProp(sb, "RELATEDEVENTID", d.RelatedEventId);
        AppendXProp(sb, "RECURRENCEEXCLUSIONS", d.RecurrenceExclusionsJson);
        AppendXProp(sb, "RECURRENCEPARENTID", d.RecurrenceParentId);
        AppendXProp(sb, "RECURRENCEORIGINALSTART",
            d.RecurrenceOriginalStart.HasValue
                ? ToUtcSafe(d.RecurrenceOriginalStart.Value).ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
                : null);
        AppendXProp(sb, "CREATEDAT",
            ToUtcSafe(d.CreatedAt).ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture));
        AppendXProp(sb, "CREATEDBY", d.CreatedBy);

        sb.Append("END:VEVENT\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// 追加 X-NONEAD-* 扩展属性（值为 null/空时跳过）
    /// </summary>
    private static void AppendXProp(StringBuilder sb, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append($"{XPrefix}{name}:{EscapeText(value)}\r\n");
    }

    private static void AppendXProp(StringBuilder sb, string name, int? value)
    {
        if (!value.HasValue) return;
        sb.Append($"{XPrefix}{name}:{value.Value}\r\n");
    }

    /// <summary>
    /// T000096 时区修复：将 DateTime 安全地标记为 UTC。
    /// SQLite 不保留 DateTimeKind，读回的 DateTime.Kind=Unspecified，
    /// 但其值实际上已是 UTC（由 CalendarMutationService.ParseDate 统一归一化）。
    /// 使用 ToUniversalTime() 会对 Unspecified 值做二次转换（把 UTC 当本地时间再减时区偏移），
    /// 导致导出时间比实际少 8 小时（UTC+8 服务器）。正确做法是用 SpecifyKind 重新标记，不做数值转换。
    /// </summary>
    private static DateTime ToUtcSafe(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// T000096: 从 X-NONEAD-* 属性恢复字段到 CreateCalendarEventRequest
    /// </summary>
    private void PopulateRequestFromXProps(CreateCalendarEventRequest request, Dictionary<string, string> props)
    {
        if (props.TryGetValue($"{XPrefix}MAILBOXID", out var mbid) && int.TryParse(mbid, out var mailboxId))
            request.MailboxId = mailboxId;

        if (props.TryGetValue($"{XPrefix}CONVERSATIONID", out var cid) && int.TryParse(cid, out var conversationId))
            request.ConversationId = conversationId;

        if (props.TryGetValue($"{XPrefix}USERID", out var uid) && int.TryParse(uid, out var userId))
            request.UserId = userId;

        if (props.TryGetValue($"{XPrefix}PRIORITY", out var pri) && int.TryParse(pri, out var priority))
            request.Priority = priority;

        if (props.TryGetValue($"{XPrefix}PROGRESS", out var prog) && int.TryParse(prog, out var progress))
            request.Progress = progress;

        if (props.TryGetValue($"{XPrefix}MEETINGURL", out var mu))
            request.MeetingUrl = mu;

        if (props.TryGetValue($"{XPrefix}COLOR", out var color))
            request.Color = color;

        if (props.TryGetValue($"{XPrefix}ATTENDEES", out var attJson) && !string.IsNullOrWhiteSpace(attJson))
        {
            try
            {
                var attendees = JsonSerializer.Deserialize<List<int>>(attJson);
                if (attendees != null) request.Attendees = attendees;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "导入 X-NONEAD-ATTENDEES 解析失败: {Json}", attJson);
            }
        }

        if (props.TryGetValue($"{XPrefix}SUBTASKS", out var stJson) && !string.IsNullOrWhiteSpace(stJson))
        {
            try
            {
                var subTasks = JsonSerializer.Deserialize<List<SubTaskDto>>(stJson);
                if (subTasks != null) request.SubTasks = subTasks;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "导入 X-NONEAD-SUBTASKS 解析失败: {Json}", stJson);
            }
        }

        if (props.TryGetValue($"{XPrefix}TASKOWNERID", out var toid) && int.TryParse(toid, out var taskOwnerId))
            request.TaskOwnerId = taskOwnerId;

        if (props.TryGetValue($"{XPrefix}DUEAT", out var dueAtStr) && !string.IsNullOrWhiteSpace(dueAtStr))
            request.DueAt = dueAtStr;

        if (props.TryGetValue($"{XPrefix}REMINDERMINUTES", out var rm) && int.TryParse(rm, out var reminderMinutes))
            request.ReminderMinutes = reminderMinutes;

        if (props.TryGetValue($"{XPrefix}REMINDERTYPE", out var rt))
            request.ReminderType = rt;

        if (props.TryGetValue($"{XPrefix}REMINDEREMAILS", out var re))
            request.ReminderEmails = re;

        if (props.TryGetValue($"{XPrefix}ISRECURRING", out var ir) && bool.TryParse(ir, out var isRecurring))
            request.IsRecurring = isRecurring;

        if (props.TryGetValue($"{XPrefix}RECURRENCETYPE", out var rtype) && int.TryParse(rtype, out var recurrenceType))
            request.RecurrenceType = recurrenceType;

        if (props.TryGetValue($"{XPrefix}RECURRENCEINTERVAL", out var rint) && int.TryParse(rint, out var recurrenceInterval))
            request.RecurrenceInterval = recurrenceInterval;

        if (props.TryGetValue($"{XPrefix}RECURRENCEENDDATE", out var redStr) && !string.IsNullOrWhiteSpace(redStr))
            request.RecurrenceEndDate = redStr;

        if (props.TryGetValue($"{XPrefix}RECURRENCECOUNT", out var rc) && int.TryParse(rc, out var recurrenceCount))
            request.RecurrenceCount = recurrenceCount;

        if (props.TryGetValue($"{XPrefix}RECURRENCERULE", out var rr))
            request.RecurrenceRule = rr;

        if (props.TryGetValue($"{XPrefix}WEEKDAYSSELECTED", out var wdsJson) && !string.IsNullOrWhiteSpace(wdsJson))
        {
            try
            {
                var weekdays = JsonSerializer.Deserialize<List<int>>(wdsJson);
                if (weekdays != null) request.WeekdaysSelected = weekdays;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "导入 X-NONEAD-WEEKDAYSSELECTED 解析失败: {Json}", wdsJson);
            }
        }

        if (props.TryGetValue($"{XPrefix}HOSTUSERIDS", out var huidsJson) && !string.IsNullOrWhiteSpace(huidsJson))
        {
            try
            {
                var hostUserIds = JsonSerializer.Deserialize<List<int>>(huidsJson);
                if (hostUserIds != null) request.HostUserIds = hostUserIds;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "导入 X-NONEAD-HOSTUSERIDS 解析失败: {Json}", huidsJson);
            }
        }

        if (props.TryGetValue($"{XPrefix}AGENDAIDS", out var aidsJson) && !string.IsNullOrWhiteSpace(aidsJson))
        {
            try
            {
                var agendaIds = JsonSerializer.Deserialize<List<int>>(aidsJson);
                if (agendaIds != null) request.AgendaIds = agendaIds;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "导入 X-NONEAD-AGENDAIDS 解析失败: {Json}", aidsJson);
            }
        }

        // T000096 v2: 恢复 RelatedEventId
        if (props.TryGetValue($"{XPrefix}RELATEDEVENTID", out var reid) && int.TryParse(reid, out var relatedEventId))
            request.RelatedEventId = relatedEventId;

        // T000096 v2: 恢复 RecurrenceExclusions
        if (props.TryGetValue($"{XPrefix}RECURRENCEEXCLUSIONS", out var exclJson) && !string.IsNullOrWhiteSpace(exclJson))
        {
            try
            {
                var excl = JsonSerializer.Deserialize<RecurrenceExclusionsDto>(exclJson);
                if (excl != null) request.RecurrenceExclusions = excl;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "导入 X-NONEAD-RECURRENCEEXCLUSIONS 解析失败: {Json}", exclJson);
            }
        }
    }

    // T000096 v2: 不可恢复字段（仅供日志记录，不写入 request）
    // - RecurrenceParentId / RecurrenceOriginalStart / CreatedAt / CreatedBy
    //   原因：CreateCalendarEventRequest 未暴露这些字段，且语义上不应允许任意覆盖
    //   （如：导入操作者不应冒充原创建者；CreatedAt 应使用导入时刻）
    //   这些字段虽无法精确恢复，但已通过 X-NONEAD-* 导出到 .ics 文件中，
    //   未来若需要扩展 request 可直接从 props 读取。
    // T000096 v3: 实际仍需恢复这些字段以保证导入数据与原数据完全一致，
    //   通过 RestoreImmutableFieldsAsync 在 CreateAsync 后直接修改实体实现。

    /// <summary>
    /// T000096 v3: 导入后恢复 CreateCalendarEventRequest 未暴露的"不可变"字段。
    ///   通过仓储直接修改实体，绕过 Request 字段限制。
    ///   注意：调用方需在事务中调用，外层 SaveChangesAsync 会持久化修改。
    ///   T000096 v4 修复：必须使用 GetByIdTrackedAsync（tracked），
    ///   GetByIdAsync 使用 AsNoTracking()，修改不会被 ChangeTracker 跟踪，
    ///   导致 SaveChangesAsync 不生成 UPDATE SQL，字段恢复失效。
    /// </summary>
    private async Task RestoreImmutableFieldsAsync(int eventId, Dictionary<string, string> props)
    {
        var entity = await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(eventId);
        if (entity == null) return;

        var changed = false;

        // 恢复 CreatedBy（原创建者用户ID）
        if (props.TryGetValue($"{XPrefix}CREATEDBY", out var cb) && int.TryParse(cb, out var createdBy))
        {
            entity.CreatedBy = createdBy;
            changed = true;
        }

        // 恢复 CreatedAt（原创建时间，UTC）
        if (props.TryGetValue($"{XPrefix}CREATEDAT", out var caStr) && !string.IsNullOrWhiteSpace(caStr))
        {
            var (parsedDate, _) = ParseIcsDate(caStr);
            if (parsedDate.HasValue)
            {
                entity.CreatedAt = parsedDate.Value;
                changed = true;
            }
        }

        // 恢复 RecurrenceParentId（重复日程父ID）
        if (props.TryGetValue($"{XPrefix}RECURRENCEPARENTID", out var pid) && int.TryParse(pid, out var parentId))
        {
            entity.RecurrenceParentId = parentId;
            changed = true;
        }

        // 恢复 RecurrenceOriginalStart（重复日程原始开始时间，UTC）
        if (props.TryGetValue($"{XPrefix}RECURRENCEORIGINALSTART", out var rosStr) && !string.IsNullOrWhiteSpace(rosStr))
        {
            var (parsedDate, _) = ParseIcsDate(rosStr);
            if (parsedDate.HasValue)
            {
                entity.RecurrenceOriginalStart = parsedDate.Value;
                changed = true;
            }
        }

        if (changed)
        {
            // UpdatedAt 保持导入时刻（修改即更新），不恢复原值
            entity.UpdatedAt = DateTime.UtcNow;
            // SaveChangesAsync 由外层 ImportAsync 调用
        }
    }

    /// <summary>
    /// RFC 5545 行折叠展开：以空格或制表符开头的行应附加到上一行。
    /// </summary>
    private static List<string> UnfoldLines(string[] lines)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (result.Count > 0 && (line.StartsWith(" ") || line.StartsWith("\t")))
            {
                result[result.Count - 1] += line.Substring(1);
            }
            else
            {
                result.Add(line);
            }
        }
        return result;
    }

    /// <summary>
    /// 从已展开的行集合中提取每个 VEVENT 块的行列表。
    /// </summary>
    private static List<List<string>> ExtractVEvents(List<string> lines)
    {
        var events = new List<List<string>>();
        List<string>? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new List<string>();
            }
            else if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current != null)
                {
                    events.Add(current);
                    current = null;
                }
            }
            else if (current != null && line.Length > 0)
            {
                current.Add(line);
            }
        }

        return events;
    }

    /// <summary>
    /// 解析 VEVENT 内的 KEY:VALUE 行。
    /// 支持 KEY;PARAM=...:VALUE 形式（参数被丢弃，仅保留 KEY 与 VALUE）。
    /// </summary>
    private static Dictionary<string, string> ParseEventProperties(List<string> eventLines)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in eventLines)
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var keyPart = line.Substring(0, colonIndex);
            var value = line.Substring(colonIndex + 1);

            // 去掉 KEY 后的 ;PARAM=... 部分
            var semiIndex = keyPart.IndexOf(';');
            var key = semiIndex >= 0 ? keyPart.Substring(0, semiIndex) : keyPart;

            if (!string.IsNullOrWhiteSpace(key))
            {
                props[key.ToUpperInvariant()] = UnescapeText(value);
            }
        }

        return props;
    }

    /// <summary>
    /// 解析 ICS 日期值，返回 (DateTime?, IsAllDay)。
    /// 支持格式：
    ///   - yyyyMMddTHHmmssZ（UTC 时间）
    ///   - yyyyMMdd（全天事件）
    ///   - yyyyMMddTHHmmss（本地时间，按 UTC 解析）
    /// </summary>
    private static (DateTime? Date, bool IsAllDay) ParseIcsDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, false);

        // 去掉可能的 VALUE=DATE 参数前缀（已被 ParseEventProperties 处理为只剩 value）
        var value = raw.Trim();

        // 全天事件：yyyyMMdd (8 位纯数字)
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var allDayDate))
        {
            return (DateTime.SpecifyKind(allDayDate, DateTimeKind.Utc), true);
        }

        // UTC 时间：yyyyMMddTHHmmssZ
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = value.Substring(0, value.Length - 1);
            if (DateTime.TryParseExact(trimmed, "yyyyMMddTHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utcDate))
            {
                return (DateTime.SpecifyKind(utcDate, DateTimeKind.Utc), false);
            }
        }

        // 本地时间（无 Z）：按 RFC 5545 视为浮动本地时间（Floating），
        // 转换为 UTC 时 AssumeUniversal + AdjustToUniversal => 保留数值并标记为 UTC
        if (DateTime.TryParseExact(value, "yyyyMMddTHHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var localDate))
        {
            return (DateTime.SpecifyKind(localDate, DateTimeKind.Utc), false);
        }

        // 兜底：尝试通用解析
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fallback))
        {
            return (DateTime.SpecifyKind(fallback, DateTimeKind.Utc), false);
        }

        return (null, false);
    }

    private static string? MapInternalStatusToIcs(string? internalStatus)
    {
        return internalStatus?.ToLowerInvariant() switch
        {
            "pending" => "TENTATIVE",
            "in_progress" => "CONFIRMED",   // T000096: 进行中映射为 CONFIRMED（已确认正在进行）
            "completed" => "CONFIRMED",     // T000096: 已完成映射为 CONFIRMED（X-NONEAD-STATUS 保存完整值）
            "cancelled" => "CANCELLED",
            "missed" => "CANCELLED",         // T000096: 已错过映射为 CANCELLED（X-NONEAD-STATUS 保存完整值）
            _ => null
        };
    }

    private static string MapIcsStatusToInternal(string? icsStatus)
    {
        return icsStatus?.ToUpperInvariant() switch
        {
            "TENTATIVE" => "pending",
            "CONFIRMED" => "in_progress",   // T000096: CONFIRMED 回退为 in_progress（完整状态优先从 X-NONEAD-STATUS 恢复）
            "CANCELLED" => "cancelled",
            _ => "pending"
        };
    }

    private static DateTime ParseDtoDate(string s)
    {
        return DateTime.Parse(s, null, DateTimeStyles.RoundtripKind);
    }

    /// <summary>
    /// RFC 5545 文本转义：逗号、分号、换行、反斜杠需转义。
    /// T000096: 统一处理 \r\n 和 \n，确保导出/导入对称。
    /// </summary>
    private static string EscapeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("\\", "\\\\")
            .Replace("\r\n", "\\n")    // CRLF 先于单独 \r/\n 处理
            .Replace("\r", "\\n")      // 单独的 CR 也转为 \n 转义
            .Replace("\n", "\\n")      // LF 转为 \n 转义
            .Replace(";", "\\;")
            .Replace(",", "\\,");
    }

    /// <summary>
    /// RFC 5545 文本反转义。
    /// T000096: 对称处理，将 \n 转义还原为统一换行符。
    /// </summary>
    private static string UnescapeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("\\n", "\n")
            .Replace("\\N", "\n")
            .Replace("\\,", ",")
            .Replace("\\;", ";")
            .Replace("\\\\", "\\");
    }
}
