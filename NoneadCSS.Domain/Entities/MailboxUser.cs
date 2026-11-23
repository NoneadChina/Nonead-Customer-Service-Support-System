namespace NoneadCSS.Domain.Entities;

public class MailboxUser
{
    public int MailboxId { get; set; }
    public int UserId { get; set; }
    public string Access { get; set; } = "{}";

    public Mailbox Mailbox { get; set; } = null!;
    public User User { get; set; } = null!;
}
