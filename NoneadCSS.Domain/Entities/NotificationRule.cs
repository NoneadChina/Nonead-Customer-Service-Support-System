namespace NoneadCSS.Domain.Entities;

public class NotificationRule : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int? MailboxId { get; set; }
    public int? UserId { get; set; }
    public int? RoleId { get; set; }
    public int? TeamId { get; set; }
    public int? RecipientMailboxId { get; set; }
    public int? TemplateId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 乐观锁 RowVersion；规则更新/启用停用并发写时防止 lost update（M8）。
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Mailbox? Mailbox { get; set; }
    public User? User { get; set; }
    public Role? Role { get; set; }
    public Team? Team { get; set; }
    public Mailbox? RecipientMailbox { get; set; }
    public NotificationTemplate? Template { get; set; }

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

    public bool IsGlobalRule() => !MailboxId.HasValue && !UserId.HasValue;

    public bool IsMailboxRule() => MailboxId.HasValue && !UserId.HasValue;

    public bool IsPersonalRule() => UserId.HasValue;

    public bool AppliesToMailbox(int mailboxId) => !MailboxId.HasValue || MailboxId.Value == mailboxId;

    public bool AppliesToUser(int userId) => !UserId.HasValue || UserId.Value == userId;

    public bool CanExecuteFor(int mailboxId, int? userId) => IsActive && !IsDeleted() && AppliesToMailbox(mailboxId) && (!userId.HasValue || AppliesToUser(userId.Value));

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateName(string newName)
    {
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEventType(string eventType)
    {
        EventType = eventType;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateTemplate(int? templateId)
    {
        TemplateId = templateId;
        UpdatedAt = DateTime.UtcNow;
    }
}
