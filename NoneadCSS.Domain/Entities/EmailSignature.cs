namespace NoneadCSS.Domain.Entities;

public class EmailSignature : ISoftDelete
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int? MailboxId { get; set; }
    public Mailbox? Mailbox { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>软删除标记（null 表示未删除，非空表示已删除并记录删除时间）</summary>
    public DateTime? DeletedAt { get; set; }
}
