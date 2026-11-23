using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Domain.Entities;

public class CalendarReminderAttempt : ISoftDelete
{
    public int Id { get; set; }

    public int EventId { get; set; }

    [MaxLength(20)]
    public string AttemptChannel { get; set; } = null!;

    public int AttemptNo { get; set; } = 1;

    [MaxLength(20)]
    public string Status { get; set; } = null!;

    public string? Payload { get; set; }

    public string? ErrorDetail { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTime? DeletedAt { get; set; }

    public CalendarEvent? Event { get; set; }

    public bool IsDeleted() => DeletedAt.HasValue;

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
    }

    public void Restore()
    {
        DeletedAt = null;
    }
}
