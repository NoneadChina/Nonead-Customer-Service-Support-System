namespace NoneadCSS.Application.DTOs.Responses;

public class CustomReportDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public string ReportConfig { get; set; } = "{}";
    public bool IsDashboardPinned { get; set; }
    public int DashboardSortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}

public class CustomReportExecutionResultDto
{
    public ReportMetaDto ReportMeta { get; set; } = new();
    public List<SectionResultDto> Sections { get; set; } = new();
}

public class ReportMetaDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int? MailboxId { get; set; }
    public string GroupBy { get; set; } = "none";
}

public class SectionResultDto
{
    public string Key { get; set; } = string.Empty;
    public string ChartType { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class CustomReportScheduleDto
{
    public int Id { get; set; }
    public int CustomReportId { get; set; }
    public string Recurrence { get; set; } = string.Empty;
    public int? WeekDay { get; set; }
    public string SendTime { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = new();
    public string Subject { get; set; } = string.Empty;
    public string AttachmentFormat { get; set; } = "pdf";
    public bool IsActive { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}
