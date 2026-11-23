namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 邮件发送日志响应
/// </summary>
public class SendLogDto
{
    public int Id { get; set; }
    public int? ThreadId { get; set; }
    public int? ConversationId { get; set; }
    public string? ConversationSubject { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int MailType { get; set; }
    public string MailTypeName { get; set; } = string.Empty;
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string StatusCategory { get; set; } = string.Empty; // success / failed / pending
    public int? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 发送日志统计响应
/// </summary>
public class SendLogStatsDto
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int PendingCount { get; set; }
    public double SuccessRate { get; set; }
}

/// <summary>
/// 按邮箱的发送统计
/// </summary>
public class MailboxSendStatsDto
{
    public int MailboxId { get; set; }
    public string MailboxName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public double SuccessRate { get; set; }
}

/// <summary>
/// 按日的发送统计
/// </summary>
public class DailySendStatsDto
{
    public DateTime Date { get; set; }
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}

/// <summary>
/// 发送日志总览响应
/// </summary>
public class SendLogOverviewResponse
{
    public SendLogStatsDto Stats { get; set; } = new();
    public List<MailboxSendStatsDto> MailboxStats { get; set; } = [];
    public List<DailySendStatsDto> DailyStats { get; set; } = [];
    public List<SendLogDto> RecentFailures { get; set; } = [];
}
