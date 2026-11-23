namespace NoneadCSS.Domain.Entities;

public class SatisfactionRating
{
    public int Id { get; set; }

    public int ConversationId { get; set; }

    public int? ThreadId { get; set; }

    public int CustomerId { get; set; }

    public int? UserId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerEmail { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Conversation? Conversation { get; set; }
    public ConversationThread? Thread { get; set; }
    public Customer? Customer { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// 是否为好评（4-5分）
    /// </summary>
    public bool IsPositive() => Rating >= 4;

    /// <summary>
    /// 是否为差评（1-2分）
    /// </summary>
    public bool IsNegative() => Rating <= 2;

    /// <summary>
    /// 是否为中性评价（3分）
    /// </summary>
    public bool IsNeutral() => Rating == 3;
}
