namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 会话阅读状态（按用户-会话维度记录）。
/// 记录每个用户最后查看某个会话的时间，用于计算未读状态和列表排序。
/// </summary>
public class ConversationReadState
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int UserId { get; set; }

    /// <summary>
    /// 用户最后查看该会话的时间（UTC）。
    /// 当 Conversation.LastReplyAt > LastReadAt 时视为有未读消息。
    /// </summary>
    public DateTime LastReadAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Conversation Conversation { get; set; } = null!;
    public User User { get; set; } = null!;
}
