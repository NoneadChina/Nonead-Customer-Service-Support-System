using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 创建日历事件模板请求
/// </summary>
public class CreateCalendarEventTemplateRequest : IValidatableObject
{
    /// <summary>模板名称</summary>
    [Required(ErrorMessage = "模板名称不能为空")]
    [MaxLength(100, ErrorMessage = "模板名称最多 100 个字符")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>事件标题</summary>
    [Required(ErrorMessage = "事件标题不能为空")]
    [MaxLength(200, ErrorMessage = "事件标题最多 200 个字符")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>事件类型：due_date / task / meeting / reminder</summary>
    [Required(ErrorMessage = "事件类型不能为空")]
    [MaxLength(20, ErrorMessage = "事件类型最多 20 个字符")]
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "task";

    /// <summary>默认时长（分钟），0 表示全天</summary>
    [Range(0, 1440, ErrorMessage = "时长必须在 0-1440 分钟之间")]
    [JsonPropertyName("durationMin")]
    public int DurationMin { get; set; } = 60;

    /// <summary>是否为全天事件</summary>
    [JsonPropertyName("isAllDay")]
    public bool IsAllDay { get; set; } = false;

    /// <summary>模板描述</summary>
    [MaxLength(500, ErrorMessage = "描述最多 500 个字符")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>默认地点</summary>
    [MaxLength(200, ErrorMessage = "地点最多 200 个字符")]
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>排序顺序</summary>
    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; } = 0;

    /// <summary>是否全局共享</summary>
    [JsonPropertyName("isShared")]
    public bool IsShared { get; set; } = true;

    /// <summary>关联邮箱 ID</summary>
    [JsonPropertyName("mailboxId")]
    public int? MailboxId { get; set; }

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
    }
}

/// <summary>
/// 更新日历事件模板请求
/// </summary>
public class UpdateCalendarEventTemplateRequest : IValidatableObject
{
    [Required(ErrorMessage = "模板名称不能为空")]
    [MaxLength(100, ErrorMessage = "模板名称最多 100 个字符")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "事件标题不能为空")]
    [MaxLength(200, ErrorMessage = "事件标题最多 200 个字符")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "事件类型不能为空")]
    [MaxLength(20, ErrorMessage = "事件类型最多 20 个字符")]
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = "task";

    [Range(0, 1440, ErrorMessage = "时长必须在 0-1440 分钟之间")]
    [JsonPropertyName("durationMin")]
    public int DurationMin { get; set; } = 60;

    [JsonPropertyName("isAllDay")]
    public bool IsAllDay { get; set; } = false;

    [MaxLength(500, ErrorMessage = "描述最多 500 个字符")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [MaxLength(200, ErrorMessage = "地点最多 200 个字符")]
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; } = 0;

    [JsonPropertyName("isShared")]
    public bool IsShared { get; set; } = true;

    [JsonPropertyName("mailboxId")]
    public int? MailboxId { get; set; }

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
    }
}

/// <summary>
/// 日历批量操作请求
/// </summary>
public class CalendarBatchOperationRequest : IValidatableObject
{
    /// <summary>要操作的事件 ID 列表</summary>
    [Required(ErrorMessage = "事件 ID 列表不能为空")]
    [MinLength(1, ErrorMessage = "至少选择一个事件")]
    [MaxLength(500, ErrorMessage = "单次批量操作最多 500 个事件")]
    [JsonPropertyName("ids")]
    public List<int> Ids { get; set; } = new();

    /// <summary>操作类型：delete / restore / setStatus / setEventType / moveOffset</summary>
    [Required(ErrorMessage = "操作类型不能为空")]
    [MaxLength(20, ErrorMessage = "操作类型最多 20 个字符")]
    [JsonPropertyName("op")]
    public string Op { get; set; } = string.Empty;

    /// <summary>setStatus 时的新状态：pending / in_progress / completed / cancelled / missed</summary>
    [MaxLength(20, ErrorMessage = "状态最多 20 个字符")]
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>setEventType 时的新类型：due_date / task / meeting / reminder</summary>
    [MaxLength(20, ErrorMessage = "事件类型最多 20 个字符")]
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    /// <summary>moveOffset 时的天数偏移（正数向前，负数向后）</summary>
    [JsonPropertyName("daysOffset")]
    public int? DaysOffset { get; set; }

    /// <summary>moveOffset 时的分钟偏移（正数向后，负数向前）</summary>
    [JsonPropertyName("minutesOffset")]
    public int? MinutesOffset { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var validOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "delete", "restore", "setStatus", "setEventType", "moveOffset"
        };
        if (!validOps.Contains(Op))
        {
            yield return new ValidationResult(
                $"操作类型 '{Op}' 无效，有效值为：{string.Join(", ", validOps)}",
                new[] { nameof(Op) });
        }

        if (string.Equals(Op, "setStatus", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(Status))
            {
                yield return new ValidationResult(
                    "setStatus 操作必须指定 status 参数",
                    new[] { nameof(Status) });
            }
            else
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
        }

        if (string.Equals(Op, "setEventType", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(EventType))
            {
                yield return new ValidationResult(
                    "setEventType 操作必须指定 eventType 参数",
                    new[] { nameof(EventType) });
            }
            else
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
        }

        if (string.Equals(Op, "moveOffset", StringComparison.OrdinalIgnoreCase))
        {
            if (!DaysOffset.HasValue && !MinutesOffset.HasValue)
            {
                yield return new ValidationResult(
                    "moveOffset 操作必须至少指定 daysOffset 或 minutesOffset 之一",
                    new[] { nameof(DaysOffset), nameof(MinutesOffset) });
            }
        }

        if (Ids != null)
        {
            foreach (var id in Ids)
            {
                if (id <= 0)
                {
                    yield return new ValidationResult(
                        $"事件 ID '{id}' 无效，必须大于 0",
                        new[] { nameof(Ids) });
                    break;
                }
            }
        }
    }
}
