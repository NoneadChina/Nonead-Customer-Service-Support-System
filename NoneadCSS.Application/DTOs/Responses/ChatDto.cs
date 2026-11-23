using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 聊天会话 DTO
/// </summary>
public class ChatSessionDto
{
    public int Id { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    /// <summary>
    /// 客户手机号（规范化后，如 13812345678 或 +8613812345678）
    /// </summary>
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerIp { get; set; }
    public int? CustomerId { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public int? ConversationId { get; set; }
    public string Status { get; set; } = "waiting";
    public string StatusLabel { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public DateTime? CustomerLastSeenAt { get; set; }
    public DateTime? AgentLastSeenAt { get; set; }
    public int CustomerUnreadCount { get; set; }
    public int AgentUnreadCount { get; set; }
    public int? Rating { get; set; }
    public string? RatingComment { get; set; }

    /// <summary>
    /// 客服主动发起关闭、并向客户推送满意度调查请求的时间（UTC，null = 未发起关闭请求）。
    /// 非空时 Status 一般为 "awaiting_rating"。
    /// </summary>
    public DateTime? RatingRequestedAt { get; set; }

    /// <summary>
    /// 等待客户评分的超时时间（UTC），通常 = RatingRequestedAt + 3 分钟。
    /// 后台定时任务每分钟扫描此时间 <= UTC now 且 Status=awaiting_rating → 自动转为 closed。
    /// </summary>
    public DateTime? RatingTimeoutAt { get; set; }

    public int MessageCount { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? ClosedAt { get; set; }

    /// <summary>
    /// 乐观锁版本标记（RowVersion base64 字符串）。
    /// 编辑 / 关闭 / 评分 / 分派会话时，前端应在请求体回传 IfMatch = rowVersion；
    /// 多端并发写场景下若版本不匹配，后端会返回 409 并在 data.latest 中附带最新快照。
    /// </summary>
    [JsonPropertyName("rowVersion")]
    public string? RowVersion { get; set; }
}

public class ChatMessageDto
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string SenderType { get; set; } = "customer";
    public int? SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string MessageType { get; set; } = "text";
    public string Content { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public string? AttachmentName { get; set; }
    public bool IsRead { get; set; }
    public string? ReadAt { get; set; }
    public bool IsRecalled { get; set; }
    public string? RecalledAt { get; set; }
    public string? EditedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    /// 乐观锁版本标记（RowVersion base64 字符串）。
    /// 编辑 / 撤回 / 标记已读 / 重发消息等写操作回传 ifMatch = rowVersion。
    /// </summary>
    [JsonPropertyName("rowVersion")]
    public string? RowVersion { get; set; }
}

/// <summary>
/// 编辑消息请求
/// </summary>
public class EditChatMessageRequest
{
    [Required]
    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>乐观锁 If-Match（前端从 ChatMessageDto.rowVersion 回传）。</summary>
    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}

/// <summary>
/// 输入状态通知请求
/// </summary>
public class TypingRequest
{
    /// <summary>
    /// 谁正在输入：customer / agent
    /// </summary>
    [Required]
    public string SenderType { get; set; } = "agent";
}

/// <summary>
/// 客户发起聊天请求
/// </summary>
public class InitiateChatRequest
{
    [Required]
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string CustomerEmail { get; set; } = string.Empty;
    /// <summary>
    /// 客户手机号（必填，支持中国大陆 11 位手机号或带国家码格式，如 +8613812345678）
    /// 后端将规范化后再存储到 ChatSession.CustomerPhone 和 Customer.Phones
    /// </summary>
    [Required(ErrorMessage = "请输入您的手机号")]
    [MaxLength(30)]
    [DataType(DataType.PhoneNumber)]
    [RegularExpression(@"^(\+?\d{1,4}[\s-]?)?1[3-9]\d{9}$", ErrorMessage = "手机号格式不正确")]
    public string CustomerPhone { get; set; } = string.Empty;
    public int? MailboxId { get; set; }
    public string? Subject { get; set; }
    [MaxLength(2000)]
    public string? InitialMessage { get; set; }
}

/// <summary>
/// 客服发送消息请求
/// </summary>
public class SendChatMessageRequest
{
    [Required]
    [MaxLength(5000)]
    public string Content { get; set; } = string.Empty;
    public string MessageType { get; set; } = "text";
    public string? AttachmentUrl { get; set; }
    public string? AttachmentName { get; set; }
}

/// <summary>
/// 转工单请求
/// </summary>
public class TransferToTicketRequest
{
    public int? MailboxId { get; set; }
    public int? UserId { get; set; }
    public string? Subject { get; set; }
}

/// <summary>
/// 客户评价请求
/// </summary>
public class RateChatRequest
{
    [Range(1, 5)]
    public int Rating { get; set; }
    [MaxLength(1000)]
    public string? Comment { get; set; }

    /// <summary>乐观锁 If-Match（客户提交评分也可能与客服关闭会话同时发生）</summary>
    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}

/// <summary>
/// 客服关闭会话请求（同时触发满意度调查 or 直接关闭）。
/// </summary>
public class CloseChatSessionRequest
{
    /// <summary>关闭前是否先向客户推送评分请求（true 时 Status 变为 awaiting_rating，false 时直接 closed）</summary>
    public bool RequestRating { get; set; } = true;

    /// <summary>乐观锁 If-Match</summary>
    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}

/// <summary>
/// 撤回聊天消息请求
/// </summary>
public class RecallChatMessageRequest
{
    /// <summary>乐观锁 If-Match</summary>
    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}

/// <summary>
/// 聊天会话查询参数
/// </summary>
public class ChatSessionQuery
{
    public string? Status { get; set; }
    public int? MailboxId { get; set; }
    public int? UserId { get; set; }
    public int? CustomerId { get; set; }
    public string? Keyword { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// 聊天概览响应
/// </summary>
public class ChatOverviewResponse
{
    public Dictionary<string, int> Stats { get; set; } = new();
    public List<ChatSessionDto> ActiveSessions { get; set; } = new();
    public List<ChatSessionDto> WaitingSessions { get; set; } = new();
    public Dictionary<string, string> StatusOptions { get; set; } = new();
}

/// <summary>
/// 发起聊天响应
/// </summary>
public class InitiateChatResponse
{
    public ChatSessionDto Session { get; set; } = new();
    public ChatMessageDto? WelcomeMessage { get; set; }
}
