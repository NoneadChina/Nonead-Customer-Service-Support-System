using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 在线聊天会话 - 表示客户与客服之间的实时聊天会话
/// </summary>
public class ChatSession
{
    public int Id { get; set; }

    /// <summary>
    /// 会话唯一标识符（用于客户端标识）
    /// </summary>
    [MaxLength(64)]
    public string SessionToken { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 客户名称
    /// </summary>
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// 客户邮箱
    /// </summary>
    [MaxLength(200)]
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>
    /// 客户手机号（规范化后存储，例如 13812345678 或 +8613812345678）
    /// </summary>
    [MaxLength(50)]
    public string CustomerPhone { get; set; } = string.Empty;

    /// <summary>
    /// 客户 IP 地址
    /// </summary>
    [MaxLength(50)]
    public string? CustomerIp { get; set; }

    /// <summary>
    /// 客户 User-Agent
    /// </summary>
    [MaxLength(500)]
    public string? CustomerUserAgent { get; set; }

    /// <summary>
    /// 关联客户 ID（可选，登录后绑定）
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 关联邮箱 ID
    /// </summary>
    public int? MailboxId { get; set; }

    /// <summary>
    /// 客服用户 ID（接待者）
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// 关联会话 ID（聊天转工单时生成）
    /// </summary>
    public int? ConversationId { get; set; }

    /// <summary>
    /// 状态：waiting (等待接待) / active (进行中) / awaiting_rating (客服主动关闭发起后，等待客户满意度评分) / closed (已关闭) / transferred (已转工单)
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "waiting";

    /// <summary>
    /// 主题
    /// </summary>
    [MaxLength(200)]
    public string? Subject { get; set; }

    /// <summary>
    /// 客户最后活动时间
    /// </summary>
    public DateTime? CustomerLastSeenAt { get; set; }

    /// <summary>
    /// 客服最后活动时间
    /// </summary>
    public DateTime? AgentLastSeenAt { get; set; }

    /// <summary>
    /// 未读消息数（客户侧）
    /// </summary>
    public int CustomerUnreadCount { get; set; }

    /// <summary>
    /// 未读消息数（客服侧）
    /// </summary>
    public int AgentUnreadCount { get; set; }

    /// <summary>
    /// 客户满意度评分（1-5，会话结束后客户评价）
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>
    /// 客户评价备注
    /// </summary>
    [MaxLength(500)]
    public string? RatingComment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// 客服主动发起关闭、并向客户推送满意度调查请求的时间（UTC，null 表示尚未发起关闭请求）。
    /// 非空时：Status 应为 "awaiting_rating"，客户完成评分或超时后会变成 closed。
    /// </summary>
    public DateTime? RatingRequestedAt { get; set; }

    /// <summary>
    /// 等待客户评分的超时时间（UTC），通常 = RatingRequestedAt + 3 分钟。
    /// 后台定时任务每分钟扫描：Status=awaiting_rating && RatingTimeoutAt <= UTC now → 自动转为 closed。
    /// RatingRequestedAt 非空时此值必然非空；RatingRequestedAt 为 null 时此值也应为 null。
    /// </summary>
    public DateTime? RatingTimeoutAt { get; set; }

    /// <summary>软删除时间（null 表示未删除）</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁 RowVersion（EF Core SQLite 下自动递增，用于防止 lost update）。
    /// 并发场景：客户与客服同时更新未读数、状态、最后活动时间；登录限流/评分/关闭都可能同时写。
    /// </summary>
    public byte[]? RowVersion { get; set; }

    // 导航属性
    public Mailbox? Mailbox { get; set; }
    public User? User { get; set; }
    public Customer? Customer { get; set; }
    public Conversation? Conversation { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

/// <summary>
/// 聊天消息
/// </summary>
public class ChatMessage
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    /// <summary>
    /// 发送者类型：customer (客户) / agent (客服) / system (系统)
    /// </summary>
    [MaxLength(20)]
    public string SenderType { get; set; } = "customer";

    /// <summary>
    /// 发送者 ID（客服为 UserId，客户为空）
    /// </summary>
    public int? SenderId { get; set; }

    /// <summary>
    /// 发送者名称
    /// </summary>
    [MaxLength(100)]
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// 消息类型：text (文本) / image (图片) / file (文件) / system (系统消息)
    /// </summary>
    [MaxLength(20)]
    public string MessageType { get; set; } = "text";

    /// <summary>
    /// 消息内容
    /// </summary>
    [StringLength(int.MaxValue)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 附件 URL（图片/文件类型时使用）
    /// </summary>
    [MaxLength(500)]
    public string? AttachmentUrl { get; set; }

    /// <summary>
    /// 附件名称
    /// </summary>
    [MaxLength(200)]
    public string? AttachmentName { get; set; }

    /// <summary>
    /// 是否已读
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// 已读时间
    /// </summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>
    /// 是否已撤回
    /// </summary>
    public bool IsRecalled { get; set; }

    /// <summary>
    /// 撤回时间
    /// </summary>
    public DateTime? RecalledAt { get; set; }

    /// <summary>
    /// 最后编辑时间（null 表示未编辑过）
    /// </summary>
    public DateTime? EditedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 乐观锁 RowVersion（撤回/已读/编辑消息可能出现并发）。
    /// SQLite 下 EF Core 自动在每次 SaveChanges 时自增 8 字节。
    /// </summary>
    public byte[]? RowVersion { get; set; }

    // 导航属性
    public ChatSession? Session { get; set; }
}
