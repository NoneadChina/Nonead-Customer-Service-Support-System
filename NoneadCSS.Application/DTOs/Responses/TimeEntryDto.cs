namespace NoneadCSS.Application.DTOs.Responses;

public class TimeEntryDto
{
    public int Id { get; set; }
    public string EntryType { get; set; } = "manual";
    public string? Description { get; set; }
    public int? ConversationId { get; set; }
    public int? ConversationNumber { get; set; }
    public string? ConversationSubject { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int? MailboxId { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string? EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsRunning { get; set; }
    public bool Billable { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    /// <summary>乐观锁版本令牌（RowVersion base64）；更新请求用作 If-Match，接收推送/列表时用于丢弃旧版本</summary>
    public string? RowVersion { get; set; }
}

public class TimeTrackingOverviewResponse
{
    public int TodayMinutes { get; set; }
    public int WeekMinutes { get; set; }
    public int MonthMinutes { get; set; }
    public int TotalMinutes { get; set; }
    public int? ActiveTimerId { get; set; }
    public string? ActiveTimerDescription { get; set; }
    public string? ActiveTimerStartTime { get; set; }
    public int TodayCount { get; set; }
    public int WeekCount { get; set; }
    public int BillableMinutes { get; set; }
}

public class TimeTrackingReportItem
{
    public string Label { get; set; } = string.Empty;
    public int TotalMinutes { get; set; }
    public int Count { get; set; }
    public int BillableMinutes { get; set; }
}

public class TimeTrackingReportResponse
{
    public List<TimeTrackingReportItem> ByUser { get; set; } = new();
    public List<TimeTrackingReportItem> ByConversation { get; set; } = new();
    public List<TimeTrackingReportItem> ByDate { get; set; } = new();
    public int TotalMinutes { get; set; }
    public int TotalCount { get; set; }
}

public class CreateTimeEntryRequest
{
    public string EntryType { get; set; } = "manual";
    public string? Description { get; set; }
    public int? ConversationId { get; set; }
    public int? CustomerId { get; set; }
    public int? MailboxId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? DurationMinutes { get; set; }
    public bool Billable { get; set; } = false;
}

public class UpdateTimeEntryRequest
{
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? DurationMinutes { get; set; }
    public bool Billable { get; set; } = false;
    /// <summary>乐观锁匹配令牌（rowVersion base64），后端返回 409 + latest 快照。HTTP If-Match 头优先级高于此字段</summary>
    public string? IfMatch { get; set; }
}

public class StartTimerRequest
{
    public string? Description { get; set; }
    public int? ConversationId { get; set; }
    public int? CustomerId { get; set; }
    public int? MailboxId { get; set; }
    public bool Billable { get; set; } = false;
}

public class TimeEntryQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? UserId { get; set; }
    public int? ConversationId { get; set; }
    public int? CustomerId { get; set; }
    public string? EntryType { get; set; }
    public bool? Billable { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Search { get; set; }
}
