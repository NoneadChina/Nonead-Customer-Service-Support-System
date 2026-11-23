namespace NoneadCSS.Domain.Entities;

public class Tag : ISoftDelete
{
    public int Id { get; set; }
    public int? MailboxId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#00b7ff";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public Mailbox? Mailbox { get; set; }
    public ICollection<ConversationTag> ConversationTags { get; set; } = [];

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

    public bool IsGlobal() => !MailboxId.HasValue;

    public bool IsMailboxSpecific() => MailboxId.HasValue;

    public bool BelongsToMailbox(int mailboxId) => MailboxId == mailboxId;

    public void UpdateName(string newName)
    {
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateColor(string newColor)
    {
        Color = newColor;
        UpdatedAt = DateTime.UtcNow;
    }
}
