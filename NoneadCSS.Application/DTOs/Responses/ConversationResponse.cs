namespace NoneadCSS.Application.DTOs.Responses;

public class ConversationResponse
{
    public int Id { get; set; }
    public int MailboxId { get; set; }
    public int? CustomerId { get; set; }
    public int? UserId { get; set; }
    public int? FolderId { get; set; }
    public int Type { get; set; }
    public int SourceVia { get; set; }
    /// <summary>
    /// 来源类型：1=Email、2=Web、3=Api、4=Phone、5=Chat、6=Social、7=WeChat
    /// </summary>
    public int SourceType { get; set; }
    public int Status { get; set; }
    /// <summary>
    /// 优先级：0=Low、1=Normal、2=High、3=Urgent
    /// </summary>
    public int Priority { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    /// <summary>
    /// 预览文本（最新回复摘要，纯文本，已去除 HTML 标签，用于列表显示）
    /// </summary>
    public string Preview { get; set; } = string.Empty;
    public bool HasAttachments { get; set; }
    public DateTime? LastReplyAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 当前用户是否有未读消息。
    /// true 表示会话有新消息（LastReplyAt > 用户最后阅读时间或用户从未查看）。
    /// 列表页用于展示"新消息"徽章，进入会话详情后置为 false。
    /// </summary>
    public bool HasUnreadMessages { get; set; }

    public List<int> FollowerIds { get; set; } = [];

    public CustomerResponse? Customer { get; set; }
    public UserResponse? User { get; set; }
    public List<ThreadResponse>? Threads { get; set; }
    public List<TagResponse>? Tags { get; set; }
}
