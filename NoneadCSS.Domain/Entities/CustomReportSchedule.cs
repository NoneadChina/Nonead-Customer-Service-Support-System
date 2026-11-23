namespace NoneadCSS.Domain.Entities;

public class CustomReportSchedule : ISoftDelete
{
    public int Id { get; set; }
    public int CustomReportId { get; set; }
    public string Recurrence { get; set; } = "daily";
    public int? WeekDay { get; set; }
    public string SendTime { get; set; } = "09:00";
    public string Recipients { get; set; } = "[]";
    public string Subject { get; set; } = string.Empty;
    public string AttachmentFormat { get; set; } = "pdf";
    public bool IsActive { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public int OwnerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public CustomReport? CustomReport { get; set; }
    public User? Owner { get; set; }

    public bool IsDeleted() => DeletedAt.HasValue;

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Restore()
    {
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
