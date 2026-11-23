namespace NoneadCSS.Application.DTOs.Responses;

public class DashboardResponse
{
    public int TotalConversations { get; set; }
    public int ActiveConversations { get; set; }
    public int PendingConversations { get; set; }
    public int ClosedConversations { get; set; }
    public int UnassignedCount { get; set; }
    public int TotalUsers { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalMailboxes { get; set; }
    public int TodayNewConversations { get; set; }
    public int TodayClosedConversations { get; set; }
    public double AvgFirstResponseTimeMinutes { get; set; }
    public double AvgResolutionTimeMinutes { get; set; }
    public int SlaWarningCount { get; set; }
    public int SlaBreachedCount { get; set; }
    public double SatisfactionAverageScore { get; set; }
    public int SatisfactionTotalCount { get; set; }
    public List<UserWorkloadDto> UserWorkloads { get; set; } = [];
    public List<MailboxStatsDto> MailboxStats { get; set; } = [];
    public List<ConversationTrendDto> ConversationTrend { get; set; } = [];

    // ===== 知识库模块统计 =====
    public int KnowledgeTotalArticles { get; set; }
    public int KnowledgePublishedArticles { get; set; }
    public int KnowledgeDraftArticles { get; set; }
    public int KnowledgeTotalCategories { get; set; }
    public int KnowledgeTotalViews { get; set; }
    public int KnowledgeTotalLikes { get; set; }

    // ===== 日历模块统计 =====
    public int CalendarTotalEvents { get; set; }
    public int CalendarTodayEvents { get; set; }
    public int CalendarUpcomingEvents { get; set; }
    public int CalendarPendingEvents { get; set; }
    public int CalendarOverdueEvents { get; set; }

    // ===== 工作流模块统计 =====
    public int WorkflowActiveCount { get; set; }
    public int WorkflowTodayExecutions { get; set; }
    public int WorkflowTodaySuccess { get; set; }
    public int WorkflowTodayFailed { get; set; }

    // ===== IM 队列统计 =====
    public int ImQueuePending { get; set; }
    public int ImQueueSending { get; set; }
    public int ImQueueSentToday { get; set; }
    public int ImQueueFailedToday { get; set; }

    // ===== 其他实体统计 =====
    public int TotalTags { get; set; }
    public int TotalWorkflows { get; set; }
    public int TotalCannedResponses { get; set; }
    public int TotalWebhooks { get; set; }
}

public class UserWorkloadDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
    public int ClosedToday { get; set; }
    public int RepliesToday { get; set; }
}

public class MailboxStatsDto
{
    public int MailboxId { get; set; }
    public string MailboxName { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
    public int PendingCount { get; set; }
    public int ClosedCount { get; set; }
}

public class ConversationTrendDto
{
    public string Date { get; set; } = string.Empty;
    public int NewCount { get; set; }
    public int ClosedCount { get; set; }
}
