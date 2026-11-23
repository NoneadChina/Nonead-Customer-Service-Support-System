using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _unitOfWork;

    public DashboardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DashboardResponse> GetDashboardStatsAsync(int? mailboxId = null)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var sevenDaysAgo = today.AddDays(-6);

        var totalConversations = await _unitOfWork.Conversations.CountAsync();
        var activeConversations = await _unitOfWork.Conversations.CountByStatusAsync(ConversationStatus.Active);
        var pendingConversations = await _unitOfWork.Conversations.CountByStatusAsync(ConversationStatus.Pending);
        var closedConversations = await _unitOfWork.Conversations.CountByStatusAsync(ConversationStatus.Closed);

        var totalUsers = await _unitOfWork.Users.CountAsync();
        var totalCustomers = await _unitOfWork.Customers.CountAsync();
        var totalMailboxes = await _unitOfWork.Mailboxes.CountAsync();

        var todayNew = await _unitOfWork.Conversations.CountCreatedInDateRangeAsync(today, tomorrow, mailboxId);
        var todayClosed = await _unitOfWork.Conversations.CountClosedInDateRangeAsync(today, tomorrow, mailboxId);

        var userWorkloads = await GetUserWorkloadsAsync(today, tomorrow);
        var mailboxStats = await GetMailboxStatsAsync();
        var trend = await GetConversationTrendAsync(sevenDaysAgo, tomorrow, mailboxId);

        var unassignedCount = await _unitOfWork.Conversations.CountUnassignedActiveAsync();

        // 平均首次响应时间和解决时间
        var (avgFirstResponse, avgResolution) = await CalculateResponseTimesAsync();

        // SLA 预警和逾期
        var slaWarnings = await _unitOfWork.SlaViolations.GetActiveWarningsAsync();
        var slaBreached = await _unitOfWork.SlaViolations.GetBreachedAsync();

        // 满意度统计
        var satisfactionStats = await _unitOfWork.SatisfactionRatings.GetStatsAsync();

        // ===== 扩展模块统计 =====
        var knowledgeStats = await GetKnowledgeStatsAsync();
        var calendarStats = await GetCalendarStatsAsync(today, tomorrow);
        var workflowStats = await GetWorkflowStatsAsync(today, tomorrow);
        var imQueueStats = await GetImQueueStatsAsync(today, tomorrow);

        // 其他实体统计
        var totalTags = await _unitOfWork.Tags.CountAsync();
        var totalWorkflows = await _unitOfWork.Workflows.CountAsync();
        var totalCannedResponses = await _unitOfWork.CannedResponses.CountAsync();
        var totalWebhooks = await _unitOfWork.Webhooks.CountAsync();

        return new DashboardResponse
        {
            // 会话核心指标
            TotalConversations = totalConversations,
            ActiveConversations = activeConversations,
            PendingConversations = pendingConversations,
            ClosedConversations = closedConversations,
            UnassignedCount = unassignedCount,
            TotalUsers = totalUsers,
            TotalCustomers = totalCustomers,
            TotalMailboxes = totalMailboxes,
            TodayNewConversations = todayNew,
            TodayClosedConversations = todayClosed,
            AvgFirstResponseTimeMinutes = avgFirstResponse,
            AvgResolutionTimeMinutes = avgResolution,
            SlaWarningCount = slaWarnings.Count,
            SlaBreachedCount = slaBreached.Count,
            SatisfactionAverageScore = satisfactionStats.AverageScore,
            SatisfactionTotalCount = satisfactionStats.TotalCount,
            UserWorkloads = userWorkloads,
            MailboxStats = mailboxStats,
            ConversationTrend = trend,

            // 知识库
            KnowledgeTotalArticles = knowledgeStats.TotalArticles,
            KnowledgePublishedArticles = knowledgeStats.PublishedArticles,
            KnowledgeDraftArticles = knowledgeStats.DraftArticles,
            KnowledgeTotalCategories = knowledgeStats.TotalCategories,
            KnowledgeTotalViews = knowledgeStats.TotalViews,
            KnowledgeTotalLikes = knowledgeStats.TotalLikes,

            // 日历
            CalendarTotalEvents = calendarStats.TotalEvents,
            CalendarTodayEvents = calendarStats.TodayEvents,
            CalendarUpcomingEvents = calendarStats.UpcomingEvents,
            CalendarPendingEvents = calendarStats.PendingEvents,
            CalendarOverdueEvents = calendarStats.OverdueEvents,

            // 工作流
            WorkflowActiveCount = workflowStats.ActiveCount,
            WorkflowTodayExecutions = workflowStats.TodayExecutions,
            WorkflowTodaySuccess = workflowStats.TodaySuccess,
            WorkflowTodayFailed = workflowStats.TodayFailed,

            // IM 队列
            ImQueuePending = imQueueStats.Pending,
            ImQueueSending = imQueueStats.Sending,
            ImQueueSentToday = imQueueStats.SentToday,
            ImQueueFailedToday = imQueueStats.FailedToday,

            // 其他实体
            TotalTags = totalTags,
            TotalWorkflows = totalWorkflows,
            TotalCannedResponses = totalCannedResponses,
            TotalWebhooks = totalWebhooks
        };
    }

    private async Task<(double AvgFirstResponse, double AvgResolution)> CalculateResponseTimesAsync()
    {
        var avgFirstResponse = await _unitOfWork.Threads.CalculateAverageFirstResponseTimeMinutesAsync();
        var avgResolution = await _unitOfWork.Threads.CalculateAverageResolutionTimeMinutesAsync();
        return (avgFirstResponse, avgResolution);
    }

    private async Task<List<UserWorkloadDto>> GetUserWorkloadsAsync(DateTime startDate, DateTime endDate)
    {
        var users = await _unitOfWork.Users.GetQueryable()
            .AsNoTracking()
            .Where(u => u.Status != UserStatus.Deleted)
            .OrderBy(u => u.Id)
            .Take(100)
            .ToListAsync();
        var activeCounts = await _unitOfWork.Conversations.GetActiveCountByUserIdAsync();
        var closedCounts = await _unitOfWork.Conversations.GetClosedCountByDateRangeAsync(startDate, endDate);
        var replyCounts = await _unitOfWork.Conversations.GetReplyCountByUserIdAndDateRangeAsync(startDate, endDate);

        var workloads = users.Select(user => new UserWorkloadDto
        {
            UserId = user.Id,
            UserName = user.GetFullName(),
            ActiveCount = activeCounts.TryGetValue(user.Id, out var active) ? active : 0,
            ClosedToday = closedCounts.TryGetValue(user.Id, out var closed) ? closed : 0,
            RepliesToday = replyCounts.TryGetValue(user.Id, out var replies) ? replies : 0
        }).ToList();

        return workloads.OrderByDescending(w => w.ActiveCount).ToList();
    }

    private async Task<List<MailboxStatsDto>> GetMailboxStatsAsync()
    {
        var mailboxes = await _unitOfWork.Mailboxes.GetQueryable()
            .Where(m => m.IsActive)
            .AsNoTracking()
            .OrderBy(m => m.Id)
            .Take(100)
            .ToListAsync();
        var statusCounts = await _unitOfWork.Conversations.GetStatusCountsByMailboxIdAsync();

        var stats = mailboxes.Select(mailbox =>
        {
            statusCounts.TryGetValue(mailbox.Id, out var counts);
            return new MailboxStatsDto
            {
                MailboxId = mailbox.Id,
                MailboxName = mailbox.Name,
                ActiveCount = counts.Active,
                PendingCount = counts.Pending,
                ClosedCount = counts.Closed
            };
        }).ToList();

        return stats;
    }

    private async Task<List<ConversationTrendDto>> GetConversationTrendAsync(DateTime startDate, DateTime endDate, int? mailboxId)
    {
        var trendDict = await _unitOfWork.Conversations.GetTrendAsync(startDate, endDate, mailboxId);

        return trendDict
            .OrderBy(kv => kv.Key)
            .Select(kv => new ConversationTrendDto
            {
                Date = kv.Key.ToString("yyyy-MM-dd"),
                NewCount = kv.Value.NewCount,
                ClosedCount = kv.Value.ClosedCount
            })
            .ToList();
    }

    // ===== 知识库统计 =====
    private async Task<KnowledgeStats> GetKnowledgeStatsAsync()
    {
        var totalArticles = await _unitOfWork.KnowledgeArticles.CountAsync();
        var publishedArticles = await _unitOfWork.KnowledgeArticles.CountAsync(status: "published");
        var draftArticles = await _unitOfWork.KnowledgeArticles.CountAsync(status: "draft");
        var totalCategories = await _unitOfWork.KnowledgeCategories.CountAsync();
        var totalViews = await _unitOfWork.KnowledgeArticles.SumViewCountAsync();
        var totalLikes = await _unitOfWork.KnowledgeArticles.SumLikeCountAsync();

        return new KnowledgeStats
        {
            TotalArticles = totalArticles,
            PublishedArticles = publishedArticles,
            DraftArticles = draftArticles,
            TotalCategories = totalCategories,
            TotalViews = totalViews,
            TotalLikes = totalLikes
        };
    }

    private record KnowledgeStats
    {
        public int TotalArticles { get; init; }
        public int PublishedArticles { get; init; }
        public int DraftArticles { get; init; }
        public int TotalCategories { get; init; }
        public int TotalViews { get; init; }
        public int TotalLikes { get; init; }
    }

    // ===== 日历统计 =====
    private async Task<CalendarStats> GetCalendarStatsAsync(DateTime today, DateTime tomorrow)
    {
        var utcNow = DateTime.UtcNow;
        var (stats, _) = await _unitOfWork.CalendarEvents.GetOverviewAggregateAsync(
            userIds: null, now: utcNow, upcomingTake: 0);

        // 今日事件数：起始时间在 [today, tomorrow) 区间内的事件
        var todayEvents = await _unitOfWork.CalendarEvents.GetQueryable()
            .AsNoTracking()
            .Where(e => e.DeletedAt == null && e.StartAt >= today && e.StartAt < tomorrow)
            .CountAsync();

        return new CalendarStats
        {
            TotalEvents = stats.Total,
            TodayEvents = todayEvents,
            UpcomingEvents = stats.Upcoming,
            PendingEvents = stats.Pending,
            OverdueEvents = stats.Overdue
        };
    }

    private record CalendarStats
    {
        public int TotalEvents { get; init; }
        public int TodayEvents { get; init; }
        public int UpcomingEvents { get; init; }
        public int PendingEvents { get; init; }
        public int OverdueEvents { get; init; }
    }

    // ===== 工作流统计 =====
    private async Task<WorkflowStats> GetWorkflowStatsAsync(DateTime today, DateTime tomorrow)
    {
        var activeCount = await _unitOfWork.Workflows.GetQueryable()
            .AsNoTracking()
            .Where(w => w.DeletedAt == null && w.IsActive)
            .CountAsync();

        var executionRepo = _unitOfWork.Repository<WorkflowExecutionLog>();
        var logsQueryable = executionRepo.GetQueryable(true);

        var todayExecutions = await logsQueryable
            .Where(l => l.ExecutedAt >= today && l.ExecutedAt < tomorrow)
            .CountAsync();

        var todaySuccess = await logsQueryable
            .Where(l => l.ExecutedAt >= today && l.ExecutedAt < tomorrow && l.Status == "success")
            .CountAsync();

        var todayFailed = await logsQueryable
            .Where(l => l.ExecutedAt >= today && l.ExecutedAt < tomorrow && (l.Status == "failed" || l.Status == "partial"))
            .CountAsync();

        return new WorkflowStats
        {
            ActiveCount = activeCount,
            TodayExecutions = todayExecutions,
            TodaySuccess = todaySuccess,
            TodayFailed = todayFailed
        };
    }

    private record WorkflowStats
    {
        public int ActiveCount { get; init; }
        public int TodayExecutions { get; init; }
        public int TodaySuccess { get; init; }
        public int TodayFailed { get; init; }
    }

    // ===== IM 队列统计 =====
    private async Task<ImQueueStats> GetImQueueStatsAsync(DateTime today, DateTime tomorrow)
    {
        var imRepo = _unitOfWork.ImMessageLogs;
        var queryable = imRepo.GetQueryable(true);

        var pending = await queryable
            .Where(x => x.Status == ImMessageLogStatus.Pending)
            .CountAsync();

        var sending = await queryable
            .Where(x => x.Status == ImMessageLogStatus.Sending)
            .CountAsync();

        var sentToday = await queryable
            .Where(x => x.Status == ImMessageLogStatus.Sent && x.SentAt != null && x.SentAt >= today && x.SentAt < tomorrow)
            .CountAsync();

        var failedToday = await queryable
            .Where(x => x.Status == ImMessageLogStatus.Failed && x.UpdatedAt != null && x.UpdatedAt >= today && x.UpdatedAt < tomorrow)
            .CountAsync();

        return new ImQueueStats
        {
            Pending = pending,
            Sending = sending,
            SentToday = sentToday,
            FailedToday = failedToday
        };
    }

    private record ImQueueStats
    {
        public int Pending { get; init; }
        public int Sending { get; init; }
        public int SentToday { get; init; }
        public int FailedToday { get; init; }
    }
}
