using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 日历事件 DTO
/// </summary>
public class CalendarEventDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "task";

    [JsonPropertyName("eventTypeLabel")]
    public string EventTypeLabel { get; set; } = string.Empty;

    /// <summary>
    /// 开始时间（ISO 8601 字符串）
    /// </summary>
    [JsonPropertyName("startAt")]
    public string StartAt { get; set; } = string.Empty;

    /// <summary>
    /// 结束时间（ISO 8601 字符串，可为空）
    /// </summary>
    [JsonPropertyName("endAt")]
    public string? EndAt { get; set; }

    [JsonPropertyName("isAllDay")]
    public bool IsAllDay { get; set; }

    [JsonPropertyName("conversationId")]
    public int? ConversationId { get; set; }

    [JsonPropertyName("conversationSubject")]
    public string? ConversationSubject { get; set; }

    [JsonPropertyName("mailboxId")]
    public int? MailboxId { get; set; }

    [JsonPropertyName("mailboxName")]
    public string? MailboxName { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("statusLabel")]
    public string StatusLabel { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("deletedAt")]
    public string? DeletedAt { get; set; }

    [JsonPropertyName("isRecurring")]
    public bool IsRecurring { get; set; }

    [JsonPropertyName("recurrenceType")]
    public int? RecurrenceType { get; set; }

    [JsonPropertyName("recurrenceInterval")]
    public int? RecurrenceInterval { get; set; }

    [JsonPropertyName("recurrenceEndDate")]
    public string? RecurrenceEndDate { get; set; }

    [JsonPropertyName("recurrenceCount")]
    public int? RecurrenceCount { get; set; }

    [JsonPropertyName("recurrenceParentId")]
    public int? RecurrenceParentId { get; set; }

    [JsonPropertyName("recurrenceOriginalStart")]
    public string? RecurrenceOriginalStart { get; set; }

    [JsonPropertyName("reminderMinutes")]
    public int? ReminderMinutes { get; set; }

    [JsonIgnore]
    public bool IsReminderSent { get; set; }

    [JsonPropertyName("reminderEmails")]
    public string? ReminderEmails { get; set; }

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = string.Empty;

    [JsonPropertyName("createdBy")]
    public int CreatedBy { get; set; }

    /// <summary>创建者用户姓名（始终为创建者，不随修改者变化；SuperAdmin 修改他人日程后仍显示原创建者）</summary>
    [JsonPropertyName("createdByName")]
    public string? CreatedByName { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("meetingUrl")]
    public string? MeetingUrl { get; set; }

    [JsonPropertyName("attendees")]
    public List<int> Attendees { get; set; } = new();

    [JsonPropertyName("subTasks")]
    public List<SubTaskDto> SubTasks { get; set; } = new();

    [JsonPropertyName("relatedEventId")]
    public int? RelatedEventId { get; set; }

    [JsonPropertyName("recurrenceExclusions")]
    public RecurrenceExclusionsDto? RecurrenceExclusions { get; set; }

    // ====== 事件类型专属字段（2026-08-17 持久化） ======
    /// <summary>主持人用户 ID 数组（会议类型专属）</summary>
    [JsonPropertyName("hostUserIds")]
    public List<int> HostUserIds { get; set; } = new();

    /// <summary>议程 ID 数组（会议类型专属）</summary>
    [JsonPropertyName("agendaIds")]
    public List<int> AgendaIds { get; set; } = new();

    /// <summary>任务负责人用户 ID（任务类型专属）</summary>
    [JsonPropertyName("taskOwnerId")]
    public int? TaskOwnerId { get; set; }

    /// <summary>任务负责人姓名（任务类型专属，展示层使用；为空时表示未设置或非任务类型）</summary>
    [JsonPropertyName("taskOwnerName")]
    public string? TaskOwnerName { get; set; }

    /// <summary>任务截止时间（ISO 8601，任务类型专属，区别于 EndAt）</summary>
    [JsonPropertyName("dueAt")]
    public string? DueAt { get; set; }

    /// <summary>提醒方式：popup / email / sms（提醒类型专属）</summary>
    [JsonPropertyName("reminderType")]
    public string? ReminderType { get; set; }

    /// <summary>每周哪几天（0=周日..6=周六，重复日程面板专属）</summary>
    [JsonPropertyName("weekdaysSelected")]
    public List<int> WeekdaysSelected { get; set; } = new();

    /// <summary>RRULE 原始字符串（重复日程面板专属）</summary>
    [JsonPropertyName("recurrenceRule")]
    public string? RecurrenceRule { get; set; }

    /// <summary>
    /// 乐观锁版本令牌（RowVersion 的 base64 字符串）。
    /// 更新时携带 If-Match 可确保仅匹配到当前版本才写库；为 null 表示忽略版本校验（兼容旧客户端）。
    /// </summary>
    [JsonPropertyName("rowVersion")]
    public string? RowVersion { get; set; }
}

/// <summary>
/// 创建日历事件请求
/// </summary>
public class CreateCalendarEventRequest : NoneadCSS.Application.Common.IUserIdCarrier, IValidatableObject
{
    [Required(ErrorMessage = "事件标题不能为空")]
    [MaxLength(200, ErrorMessage = "事件标题最多 200 个字符")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000, ErrorMessage = "描述最多 4000 个字符")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "事件类型不能为空")]
    [MaxLength(20, ErrorMessage = "事件类型最多 20 个字符")]
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "task";

    /// <summary>
    /// 开始时间（ISO 8601 字符串）
    /// </summary>
    [Required(ErrorMessage = "开始时间不能为空")]
    [JsonPropertyName("startAt")]
    public string StartAt { get; set; } = string.Empty;

    /// <summary>
    /// 结束时间（ISO 8601 字符串，可为空）
    /// </summary>
    [JsonPropertyName("endAt")]
    public string? EndAt { get; set; }

    [JsonPropertyName("isAllDay")]
    public bool IsAllDay { get; set; } = true;

    [JsonPropertyName("conversationId")]
    public int? ConversationId { get; set; }

    [JsonPropertyName("mailboxId")]
    public int? MailboxId { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [MaxLength(30, ErrorMessage = "颜色值最多 30 个字符")]
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("isRecurring")]
    public bool IsRecurring { get; set; }

    [JsonPropertyName("recurrenceType")]
    public int? RecurrenceType { get; set; }

    [JsonPropertyName("recurrenceInterval")]
    public int? RecurrenceInterval { get; set; }

    [MaxLength(30, ErrorMessage = "重复结束日期最多 30 个字符")]
    [JsonPropertyName("recurrenceEndDate")]
    public string? RecurrenceEndDate { get; set; }

    [JsonPropertyName("recurrenceCount")]
    public int? RecurrenceCount { get; set; }

    [JsonPropertyName("reminderMinutes")]
    public int? ReminderMinutes { get; set; }

    [MaxLength(2000, ErrorMessage = "提醒邮箱最多 2000 个字符")]
    [JsonPropertyName("reminderEmails")]
    public string? ReminderEmails { get; set; }

    [Range(0, 5, ErrorMessage = "优先级必须在 0-5 之间")]
    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [Range(0, 100, ErrorMessage = "进度必须在 0-100 之间")]
    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [MaxLength(200, ErrorMessage = "地点最多 200 个字符")]
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [MaxLength(500, ErrorMessage = "会议链接最多 500 个字符")]
    [JsonPropertyName("meetingUrl")]
    public string? MeetingUrl { get; set; }

    [JsonPropertyName("attendees")]
    public List<int> Attendees { get; set; } = new();

    [JsonPropertyName("subTasks")]
    public List<SubTaskDto> SubTasks { get; set; } = new();

    [JsonPropertyName("relatedEventId")]
    public int? RelatedEventId { get; set; }

    [JsonPropertyName("recurrenceExclusions")]
    public RecurrenceExclusionsDto? RecurrenceExclusions { get; set; }

    // ====== 事件类型专属字段（2026-08-17 持久化） ======
    /// <summary>主持人用户 ID 数组（会议类型专属）</summary>
    [JsonPropertyName("hostUserIds")]
    public List<int> HostUserIds { get; set; } = new();

    /// <summary>议程 ID 数组（会议类型专属）</summary>
    [JsonPropertyName("agendaIds")]
    public List<int> AgendaIds { get; set; } = new();

    /// <summary>任务负责人用户 ID（任务类型专属）</summary>
    [JsonPropertyName("taskOwnerId")]
    public int? TaskOwnerId { get; set; }

    /// <summary>任务截止时间（ISO 8601，任务类型专属，区别于 EndAt）</summary>
    [JsonPropertyName("dueAt")]
    public string? DueAt { get; set; }

    /// <summary>提醒方式：popup / email / sms（提醒类型专属）</summary>
    [MaxLength(20, ErrorMessage = "提醒方式最多 20 个字符")]
    [JsonPropertyName("reminderType")]
    public string? ReminderType { get; set; }

    /// <summary>每周哪几天（0=周日..6=周六，重复日程面板专属）</summary>
    [JsonPropertyName("weekdaysSelected")]
    public List<int> WeekdaysSelected { get; set; } = new();

    /// <summary>RRULE 原始字符串（重复日程面板专属）</summary>
    [MaxLength(500, ErrorMessage = "重复规则最多 500 个字符")]
    [JsonPropertyName("recurrenceRule")]
    public string? RecurrenceRule { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var validEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "due_date", "task", "meeting", "reminder",
            "personal", "travel", "work", "other"
        };
        if (!validEventTypes.Contains(EventType))
        {
            yield return new ValidationResult(
                $"事件类型 '{EventType}' 无效，有效值为：{string.Join(", ", validEventTypes)}",
                new[] { nameof(EventType) });
        }

        if (!DateTime.TryParse(StartAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startAt))
        {
            yield return new ValidationResult(
                "开始时间格式无效，必须是有效的 ISO 8601 日期时间字符串",
                new[] { nameof(StartAt) });
        }
        else
        {
            if (!string.IsNullOrEmpty(EndAt))
            {
                if (!DateTime.TryParse(EndAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var endAt))
                {
                    yield return new ValidationResult(
                        "结束时间格式无效，必须是有效的 ISO 8601 日期时间字符串",
                        new[] { nameof(EndAt) });
                }
                else if (endAt <= startAt)
                {
                    yield return new ValidationResult(
                        "结束时间必须晚于开始时间",
                        new[] { nameof(EndAt) });
                }
            }

            if (IsRecurring)
            {
                if (RecurrenceType.HasValue)
                {
                    if (!Enum.IsDefined(typeof(Domain.Enums.RecurrenceType), RecurrenceType.Value))
                    {
                        yield return new ValidationResult(
                            $"重复类型 '{RecurrenceType.Value}' 无效，有效值为：{string.Join(", ", Enum.GetValues<Domain.Enums.RecurrenceType>().Cast<int>())}",
                            new[] { nameof(RecurrenceType) });
                    }
                }

                if (!string.IsNullOrEmpty(RecurrenceEndDate))
                {
                    if (!DateTime.TryParse(RecurrenceEndDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var recurrenceEndDate))
                    {
                        yield return new ValidationResult(
                            "重复结束日期格式无效，必须是有效的日期时间字符串",
                            new[] { nameof(RecurrenceEndDate) });
                    }
                    else if (recurrenceEndDate <= startAt)
                    {
                        yield return new ValidationResult(
                            "重复结束日期必须晚于开始时间",
                            new[] { nameof(RecurrenceEndDate) });
                    }
                }
            }
        }

        if (ReminderMinutes.HasValue)
        {
            // 0 = 「准时」（在开始时刻提醒）：`CalendarReminderService` 用 `AddMinutes(-0)` 正是这个语义，
            // 界面 `REMINDER_OPTIONS` 也提供「准时」。此前这里写成 `<= 0`，把界面提供的合法档位一律驳回
            // （2026-09-24 用户裁定：放宽为 `>= 0`）。上限仍是 10080 分钟（一周），与 `CalendarOptions`
            // 的扫描窗口注释一致；两处校验（本处 + CalendarMutationService.ValidateFields）必须同区间。
            if (ReminderMinutes.Value < 0 || ReminderMinutes.Value > 10080)
            {
                yield return new ValidationResult(
                    "提醒时间必须在 0-10080 分钟之间（一周内）",
                    new[] { nameof(ReminderMinutes) });
            }
        }
    }
}

/// <summary>
/// 更新日历事件请求
/// </summary>
public class UpdateCalendarEventRequest : CreateCalendarEventRequest, IValidatableObject
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [MaxLength(20, ErrorMessage = "状态最多 20 个字符")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    /// <summary>
    /// 更新模式：single=仅本次，following=本次及后续所有，all=全部
    /// </summary>
    [MaxLength(20, ErrorMessage = "更新模式最多 20 个字符")]
    [JsonPropertyName("updateMode")]
    public string UpdateMode { get; set; } = "single";

    /// <summary>
    /// 乐观锁匹配令牌（后端返回的 rowVersion）。
    /// 非空时将与数据库当前 RowVersion 对比；不匹配会抛出 CalendarConcurrencyException 并返回 409 + latest 快照。
    /// </summary>
    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }

    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pending", "in_progress", "completed", "cancelled", "missed"
        };
        if (!validStatuses.Contains(Status))
        {
            yield return new ValidationResult(
                $"状态 '{Status}' 无效，有效值为：{string.Join(", ", validStatuses)}",
                new[] { nameof(Status) });
        }

        var validUpdateModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "single", "following", "all"
        };
        if (!validUpdateModes.Contains(UpdateMode))
        {
            yield return new ValidationResult(
                $"更新模式 '{UpdateMode}' 无效，有效值为：{string.Join(", ", validUpdateModes)}",
                new[] { nameof(UpdateMode) });
        }

        if (Id <= 0)
        {
            yield return new ValidationResult(
                "事件 ID 必须大于 0",
                new[] { nameof(Id) });
        }
    }
}

/// <summary>
/// 日历事件查询参数
/// </summary>
public class CalendarEventQuery : IValidatableObject
{
    [JsonPropertyName("startDate")]
    public DateTime? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public DateTime? EndDate { get; set; }

    [JsonPropertyName("mailboxId")]
    public int? MailboxId { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    /// <summary>
    /// 基于角色权限过滤的用户ID集合（后端在服务层注入，禁止前端直接指定以绕过权限）。
    /// 当 UserId 同时指定时，两者取交集。
    /// </summary>
    [JsonPropertyName("userIds")]
    public IEnumerable<int>? UserIds { get; set; }

    [MaxLength(20, ErrorMessage = "事件类型最多 20 个字符")]
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [MaxLength(20, ErrorMessage = "状态最多 20 个字符")]
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [MaxLength(200, ErrorMessage = "关键词最多 200 个字符")]
    [JsonPropertyName("keyword")]
    public string? Keyword { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "页码必须大于 0")]
    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [Range(1, 500, ErrorMessage = "每页数量必须在 1-500 之间")]
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// 是否展开重复日程（默认 true）
    /// </summary>
    [JsonPropertyName("expandRecurring")]
    public bool ExpandRecurring { get; set; } = true;

    /// <summary>
    /// 排序字段：time(默认) / priority / progress / createdAt。
    /// 为空时保持默认行为：按 StartAt 倒序。
    /// </summary>
    [JsonPropertyName("sortBy")]
    public string? SortBy { get; set; }

    /// <summary>
    /// 排序方向：asc / desc（默认 desc）
    /// </summary>
    [JsonPropertyName("sortDir")]
    public string? SortDir { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate.HasValue && EndDate.HasValue && EndDate.Value <= StartDate.Value)
        {
            yield return new ValidationResult(
                "结束日期必须晚于开始日期",
                new[] { nameof(EndDate) });
        }

        if (!string.IsNullOrEmpty(EventType))
        {
            var validEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "due_date", "task", "meeting", "reminder",
                "personal", "travel", "work", "other"
            };
            if (!validEventTypes.Contains(EventType))
            {
                yield return new ValidationResult(
                    $"事件类型 '{EventType}' 无效，有效值为：{string.Join(", ", validEventTypes)}",
                    new[] { nameof(EventType) });
            }
        }

        if (!string.IsNullOrEmpty(Status))
        {
            var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pending", "in_progress", "completed", "cancelled", "missed"
            };
            if (!validStatuses.Contains(Status))
            {
                yield return new ValidationResult(
                    $"状态 '{Status}' 无效，有效值为：{string.Join(", ", validStatuses)}",
                    new[] { nameof(Status) });
            }
        }

        if (!string.IsNullOrEmpty(SortBy))
        {
            var validSortFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "time", "priority", "progress", "createdAt"
            };
            if (!validSortFields.Contains(SortBy))
            {
                yield return new ValidationResult(
                    $"排序字段 '{SortBy}' 无效，有效值为：{string.Join(", ", validSortFields)}",
                    new[] { nameof(SortBy) });
            }
        }

        if (!string.IsNullOrEmpty(SortDir))
        {
            var validSortDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "asc", "desc"
            };
            if (!validSortDirs.Contains(SortDir))
            {
                yield return new ValidationResult(
                    $"排序方向 '{SortDir}' 无效，有效值为：{string.Join(", ", validSortDirs)}",
                    new[] { nameof(SortDir) });
            }
        }
    }
}

/// <summary>
/// 复制日历事件请求（所有字段可选，不传则使用原事件值）
/// </summary>
public class DuplicateCalendarEventRequest
{
    /// <summary>新开始时间（ISO 8601）。不传则使用原事件开始时间。</summary>
    [JsonPropertyName("startAt")]
    public string? StartAt { get; set; }

    /// <summary>新结束时间（ISO 8601）。不传则根据开始时间偏移自动计算。</summary>
    [JsonPropertyName("endAt")]
    public string? EndAt { get; set; }

    /// <summary>新标题。不传则使用原标题 + " (副本)"。</summary>
    [MaxLength(200, ErrorMessage = "事件标题最多 200 个字符")]
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// <summary>
/// 快速更新日历事件请求（仅更新指定字段，其余保持不变）
/// </summary>
public class QuickUpdateCalendarEventRequest
{
    [MaxLength(20, ErrorMessage = "状态最多 20 个字符")]
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [Range(0, 5, ErrorMessage = "优先级必须在 0-5 之间")]
    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    [Range(0, 100, ErrorMessage = "进度必须在 0-100 之间")]
    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [MaxLength(200, ErrorMessage = "地点最多 200 个字符")]
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [MaxLength(500, ErrorMessage = "会议链接最多 500 个字符")]
    [JsonPropertyName("meetingUrl")]
    public string? MeetingUrl { get; set; }

    [MaxLength(200, ErrorMessage = "事件标题最多 200 个字符")]
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [MaxLength(4000, ErrorMessage = "描述最多 4000 个字符")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [MaxLength(30, ErrorMessage = "颜色值最多 30 个字符")]
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("reminderMinutes")]
    public int? ReminderMinutes { get; set; }

    [MaxLength(20, ErrorMessage = "提醒类型最多 20 个字符")]
    [JsonPropertyName("reminderType")]
    public string? ReminderType { get; set; }

    /// <summary>
    /// 乐观锁匹配令牌（后端返回的 rowVersion）。
    /// 非空时将与数据库当前 RowVersion 对比；不匹配会抛出 CalendarConcurrencyException 并返回 409 + latest 快照。
    /// </summary>
    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}

/// <summary>
/// 日历概览响应
/// </summary>
public class CalendarOverviewResponse
{
    [JsonPropertyName("stats")]
    public Dictionary<string, int> Stats { get; set; } = new();

    [JsonPropertyName("upcomingEvents")]
    public List<CalendarEventDto> UpcomingEvents { get; set; } = new();

    [JsonPropertyName("eventTypeOptions")]
    public Dictionary<string, string> EventTypeOptions { get; set; } = new();

    [JsonPropertyName("statusOptions")]
    public Dictionary<string, string> StatusOptions { get; set; } = new();
}
