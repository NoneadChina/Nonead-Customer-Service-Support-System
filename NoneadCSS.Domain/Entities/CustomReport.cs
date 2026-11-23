namespace NoneadCSS.Domain.Entities;

public class CustomReport : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "performance";
    public string Visibility { get; set; } = "private";
    public int OwnerId { get; set; }
    public int? TeamId { get; set; }
    public string ReportConfig { get; set; } = "{}";
    public bool IsDashboardPinned { get; set; }
    public int DashboardSortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public User? Owner { get; set; }
    public Team? Team { get; set; }

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
