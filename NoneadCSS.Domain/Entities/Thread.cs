using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 会话线程 - 表示会话中的单条消息（客户消息、用户回复、内部备注或操作记录）
/// 注意：类名使用 ConversationThread 以避免与 System.Threading.Thread 冲突
/// </summary>
public class ConversationThread
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int? UserId { get; set; }
    public int? CustomerId { get; set; }
    
    /// <summary>
    /// 线程类型：Customer(客户消息)、Message(用户回复)、Note(内部备注)、LineItem(操作记录)
    /// </summary>
    public ThreadType Type { get; set; }
    
    /// <summary>
    /// 线程状态：Active(正常)、Draft(草稿)、Deleted(已删除)
    /// </summary>
    public ThreadState State { get; set; } = ThreadState.Active;
    
    /// <summary>
    /// 操作类型（仅LineItem类型使用）：状态变更、分配变更、删除、恢复等
    /// </summary>
    public ThreadActionType? ActionType { get; set; }
    
    /// <summary>
    /// 操作数据（JSON格式，记录操作前后的值）
    /// </summary>
    public string? ActionData { get; set; }
    
    /// <summary>
    /// 邮件正文或备注内容
    /// </summary>
    public string Body { get; set; } = string.Empty;
    
    /// <summary>
    /// 邮件主题
    /// </summary>
    public string Subject { get; set; } = string.Empty;
    
    /// <summary>
    /// 发件人地址（邮件为邮箱地址；微信为客户 OpenId）
    /// </summary>
    public string From { get; set; } = string.Empty;
    
    /// <summary>
    /// 收件人地址（邮件为 JSON 数组；微信为服务号原始 ID）
    /// </summary>
    public string To { get; set; } = string.Empty;
    
    /// <summary>
    /// 抄送地址（JSON数组）
    /// </summary>
    public string Cc { get; set; } = string.Empty;
    
    /// <summary>
    /// 密送地址（JSON数组）
    /// </summary>
    public string Bcc { get; set; } = string.Empty;
    
    /// <summary>
    /// 消息去重键（邮件为 Message-ID 头；微信为 wx_{MsgId} 前缀格式；事件为 wx_event_{OpenId}_{Event}_{CreateTime}）
    /// </summary>
    public string MessageId { get; set; } = string.Empty;
    
    /// <summary>
    /// 原始邮件头（用于排查问题）
    /// </summary>
    public string? Headers { get; set; }
    
    /// <summary>
    /// 是否为内部备注
    /// </summary>
    public bool IsInternal { get; set; }
    
    /// <summary>
    /// 是否有附件
    /// </summary>
    public bool HasAttachments { get; set; }
    
    /// <summary>
    /// 是否为会话的首条线程
    /// </summary>
    public bool IsFirst { get; set; }
    
    /// <summary>
    /// 来源渠道：Customer(客户) 或 User(用户)
    /// </summary>
    public SourceVia SourceVia { get; set; } = SourceVia.Customer;
    
    /// <summary>
    /// 来源类型：Email、Web、API等
    /// </summary>
    public SourceType SourceType { get; set; } = SourceType.Email;
    
    /// <summary>
    /// 创建用户ID（用户发起的消息）
    /// </summary>
    public int? CreatedByUserId { get; set; }
    
    /// <summary>
    /// 创建客户ID（客户发起的消息）
    /// </summary>
    public int? CreatedByCustomerId { get; set; }
    
    /// <summary>
    /// 发送状态：null(无需发送)、Pending(待发送)、Sending(发送中)、Sent(已发送)、Failed(发送失败)
    /// </summary>
    public SendStatus? SendStatus { get; set; }
    
    /// <summary>
    /// 邮件打开时间（追踪）
    /// </summary>
    public DateTime? OpenedAt { get; set; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public int Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // 导航属性
    public Conversation Conversation { get; set; } = null!;
    public User? User { get; set; }
    public Customer? Customer { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<Attachment> Attachments { get; set; } = [];
    public ICollection<SendLog> SendLogs { get; set; } = [];

    /// <summary>
    /// 是否为客户消息
    /// </summary>
    public bool IsCustomerMessage() => Type == ThreadType.Customer;

    /// <summary>
    /// 是否为用户回复
    /// </summary>
    public bool IsUserReply() => Type == ThreadType.Message;

    /// <summary>
    /// 是否为内部备注
    /// </summary>
    public bool IsNote() => Type == ThreadType.Note;

    /// <summary>
    /// 是否为操作记录
    /// </summary>
    public bool IsLineItem() => Type == ThreadType.LineItem;

    /// <summary>
    /// 标记为草稿
    /// </summary>
    public void MarkAsDraft()
    {
        State = ThreadState.Draft;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 标记为已删除
    /// </summary>
    public void MarkAsDeleted()
    {
        State = ThreadState.Deleted;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 恢复已删除的线程
    /// </summary>
    public void Restore()
    {
        State = ThreadState.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 设置发送状态
    /// </summary>
    /// <param name="status">发送状态</param>
    public void SetSendStatus(SendStatus status)
    {
        SendStatus = status;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 记录邮件打开
    /// </summary>
    public void RecordOpen()
    {
        var now = DateTime.UtcNow;
        OpenedAt ??= now;
        UpdatedAt = now;
    }
}

/// <summary>
/// 线程状态
/// </summary>
public enum ThreadState
{
    Active = 1,
    Draft = 2,
    Deleted = 3
}

/// <summary>
/// 线程操作类型（LineItem类型使用）
/// </summary>
public enum ThreadActionType
{
    StatusChanged = 1,
    UserChanged = 2,
    Deleted = 3,
    Restored = 4,
    MovedFromMailbox = 5
}

/// <summary>
/// 发送状态
/// </summary>
public enum SendStatus
{
    Pending = 1,
    Sending = 2,
    Sent = 3,
    Failed = 4,
    DeliveryError = 5
}
