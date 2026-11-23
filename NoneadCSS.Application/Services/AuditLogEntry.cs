namespace NoneadCSS.Application.Services;

/// <summary>
/// 审计日志条目
/// 用于事件监听器中快速记录操作
/// </summary>
public class AuditLogEntry
{
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public int? UserId { get; set; }
    public string? Description { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
}
