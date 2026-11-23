namespace NoneadCSS.Application.DTOs.Responses;

public class ThreadResponse
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int? UserId { get; set; }
    public int? CustomerId { get; set; }
    public int Type { get; set; }
    /// <summary>
    /// 来源类型：1=Email、2=Web、3=Api、4=Phone、5=Chat、6=Social、7=WeChat
    /// </summary>
    public int SourceType { get; set; }
    /// <summary>
    /// 线程状态：1=Active、2=Draft、3=Deleted
    /// </summary>
    public int State { get; set; }
    public string Body { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Cc { get; set; } = string.Empty;
    public string Bcc { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public bool IsInternal { get; set; }
    public bool HasAttachments { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 发送状态（M-F3）：1=Pending、2=Sending、3=Sent、4=Failed、5=DeliveryError。
    /// 钉钉/微信/邮件等渠道回复发送失败时通过此字段让前端感知并提示用户。
    /// null 表示非外发消息（如客户消息、内部笔记），无发送状态。
    /// </summary>
    public int? SendStatus { get; set; }

    public UserResponse? User { get; set; }
    public List<AttachmentResponse>? Attachments { get; set; }
}
