using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Entities;

public class MailTemplate : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public MailTemplateType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string? VariablesJson { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
    public int? MailboxId { get; set; }
    public Mailbox? Mailbox { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
