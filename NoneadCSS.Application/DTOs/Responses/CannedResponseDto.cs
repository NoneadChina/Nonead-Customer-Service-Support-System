namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 快捷回复模板响应
/// </summary>
public class CannedResponseDto
{
    public int Id { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int SortOrder { get; set; }
    public bool IsShared { get; set; }
    public int? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
