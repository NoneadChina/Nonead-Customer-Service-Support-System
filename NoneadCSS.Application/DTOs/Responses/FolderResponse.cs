namespace NoneadCSS.Application.DTOs.Responses;

public class FolderResponse
{
    public int Id { get; set; }
    public int MailboxId { get; set; }
    public int? UserId { get; set; }
    public int Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
    public int TotalCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
