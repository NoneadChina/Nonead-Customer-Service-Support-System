namespace NoneadCSS.Application.DTOs.Responses;

using System.ComponentModel.DataAnnotations;

public class SatisfactionRatingDto
{
    public int Id { get; set; }
    /// <summary>
    /// 数据来源：satisfaction_link = 通过满意度评价链接（邮件/IM 渠道）写入；chat_session = 在线聊天会话评分
    /// </summary>
    public string Source { get; set; } = "satisfaction_link";
    /// <summary>
    /// 当 Source = chat_session 时保存 ChatSession.Id，其他情况为 null
    /// </summary>
    public int? ChatSessionId { get; set; }
    public int ConversationId { get; set; }
    public string? ConversationSubject { get; set; }
    public int? ThreadId { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public int Rating { get; set; }
    public string RatingLabel { get; set; } = string.Empty;
    public string RatingCategory { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SatisfactionRatingStatsDto
{
    public int TotalCount { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AverageCount { get; set; }
    public int PoorCount { get; set; }
    public int BadCount { get; set; }
    public double AverageScore { get; set; }
    public double GoodRate { get; set; }
}

public class DailySatisfactionStatsDto
{
    public string Date { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AverageCount { get; set; }
    public int PoorCount { get; set; }
    public int BadCount { get; set; }
    public double AverageScore { get; set; }
}

public class MailboxSatisfactionStatsDto
{
    public int MailboxId { get; set; }
    public string MailboxName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AverageCount { get; set; }
    public int PoorCount { get; set; }
    public int BadCount { get; set; }
    public double AverageScore { get; set; }
    public double GoodRate { get; set; }
}

public class UserSatisfactionStatsDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ExcellentCount { get; set; }
    public int GoodCount { get; set; }
    public int AverageCount { get; set; }
    public int PoorCount { get; set; }
    public int BadCount { get; set; }
    public double AverageScore { get; set; }
    public double GoodRate { get; set; }
}

public class SatisfactionOverviewResponse
{
    public SatisfactionRatingStatsDto Stats { get; set; } = new();
    public List<DailySatisfactionStatsDto> DailyStats { get; set; } = [];
    public List<MailboxSatisfactionStatsDto> MailboxStats { get; set; } = [];
    public List<UserSatisfactionStatsDto> UserStats { get; set; } = [];
    public List<SatisfactionRatingDto> RecentRatings { get; set; } = [];
}

public class CreateSatisfactionRatingRequest
{
    [Required(ErrorMessage = "会话ID不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "会话ID必须大于0")]
    public int ConversationId { get; set; }

    [Required(ErrorMessage = "评分不能为空")]
    [Range(1, 5, ErrorMessage = "评分必须在 1-5 之间")]
    public int Rating { get; set; }

    [MaxLength(1000, ErrorMessage = "评论内容不能超过1000字")]
    public string? Comment { get; set; }

    [MaxLength(100, ErrorMessage = "客户姓名不能超过100字")]
    public string? CustomerName { get; set; }

    // 说明：不使用 [EmailAddress] 属性校验。
    // 因为 IM(钉钉/飞书/WhatsApp) 渠道 enrich 写入 Conversation.CustomerEmail 的值可能含有
    // 不可见字符、全角空格、显示名格式（例如 "张三" <zhangsan@example.com>），
    // 在 SatisfactionRatingService 内部会调用 NormalizeEmail() 统一规范化后再写入，
    // 避免合法用户因为"邮箱格式不正确"无法提交满意度评价。
    [MaxLength(255, ErrorMessage = "邮箱地址不能超过255字")]
    public string? CustomerEmail { get; set; }

    [Required(ErrorMessage = "评价令牌不能为空")]
    public string? Token { get; set; }
}
