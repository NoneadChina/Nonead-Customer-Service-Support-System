namespace NoneadCSS.Domain.Entities;

public class ConversationTag
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int TagId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Conversation Conversation { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
