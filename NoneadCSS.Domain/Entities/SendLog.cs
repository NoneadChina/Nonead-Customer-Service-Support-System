namespace NoneadCSS.Domain.Entities;

public class SendLog
{
    public int Id { get; set; }
    public int? ThreadId { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string? InReplyTo { get; set; }
    public string? References { get; set; }
    public string Email { get; set; } = string.Empty;
    public int MailType { get; set; }
    public int Status { get; set; }
    public int? CustomerId { get; set; }
    public int? UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int OpenCount { get; set; } = 0;
    public DateTime? FirstOpenedAt { get; set; }
    public DateTime? LastOpenedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? SentAt { get; set; }
    public int AttemptCount { get; set; } = 0;
    public string? AttachmentIds { get; set; }
    public int? MailboxId { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }

    public ConversationThread? Thread { get; set; }
    public Customer? Customer { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// 标记为已发送
    /// </summary>
    public void MarkAsSent()
    {
        Status = 3; // Sent
        SentAt = DateTime.UtcNow;
        ErrorMessage = null;
    }

    /// <summary>
    /// 标记为发送失败
    /// </summary>
    /// <param name="errorMessage">错误信息</param>
    public void MarkAsFailed(string errorMessage)
    {
        Status = 4; // Failed
        AttemptCount++;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 记录邮件打开
    /// </summary>
    public void RecordOpen()
    {
        var now = DateTime.UtcNow;
        OpenCount++;
        FirstOpenedAt ??= now;
        LastOpenedAt = now;
    }

    /// <summary>
    /// 是否已发送
    /// </summary>
    public bool IsSent() => Status == 3;

    /// <summary>
    /// 是否发送失败
    /// </summary>
    public bool IsFailed() => Status == 4 || Status == 5;
}
