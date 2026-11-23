using FluentAssertions;
using Moq;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// DashboardService 单元测试
/// 覆盖 GetDashboardStatsAsync 方法的所有路径，包括空数据、完整数据、
/// 邮箱过滤、用户工作量排序、会话趋势排序、SLA 与满意度统计等场景。
/// </summary>
public class DashboardServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConversationRepository> _mockConversations;
    private readonly Mock<IUserRepository> _mockUsers;
    private readonly Mock<IMailboxRepository> _mockMailboxes;
    private readonly Mock<ICustomerRepository> _mockCustomers;
    private readonly Mock<IThreadRepository> _mockThreads;
    private readonly Mock<ISlaViolationRepository> _mockSlaViolations;
    private readonly Mock<ISatisfactionRatingRepository> _mockSatisfactionRatings;
    // 扩展模块仓储（DashboardService 新增的统计指标依赖）
    private readonly Mock<IKnowledgeArticleRepository> _mockKnowledgeArticles;
    private readonly Mock<IKnowledgeCategoryRepository> _mockKnowledgeCategories;
    private readonly Mock<ICalendarEventRepository> _mockCalendarEvents;
    private readonly Mock<IWorkflowRepository> _mockWorkflows;
    private readonly Mock<IRepository<WorkflowExecutionLog>> _mockWorkflowExecutionLogs;
    private readonly Mock<IImMessageLogRepository> _mockImMessageLogs;
    private readonly Mock<ITagRepository> _mockTags;
    private readonly Mock<ICannedResponseRepository> _mockCannedResponses;
    private readonly Mock<IWebhookRepository> _mockWebhooks;
    private readonly DashboardService _service;

    public DashboardServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockConversations = new Mock<IConversationRepository>();
        _mockUsers = new Mock<IUserRepository>();
        _mockMailboxes = new Mock<IMailboxRepository>();
        _mockCustomers = new Mock<ICustomerRepository>();
        _mockThreads = new Mock<IThreadRepository>();
        _mockSlaViolations = new Mock<ISlaViolationRepository>();
        _mockSatisfactionRatings = new Mock<ISatisfactionRatingRepository>();
        _mockKnowledgeArticles = new Mock<IKnowledgeArticleRepository>();
        _mockKnowledgeCategories = new Mock<IKnowledgeCategoryRepository>();
        _mockCalendarEvents = new Mock<ICalendarEventRepository>();
        _mockWorkflows = new Mock<IWorkflowRepository>();
        _mockWorkflowExecutionLogs = new Mock<IRepository<WorkflowExecutionLog>>();
        _mockImMessageLogs = new Mock<IImMessageLogRepository>();
        _mockTags = new Mock<ITagRepository>();
        _mockCannedResponses = new Mock<ICannedResponseRepository>();
        _mockWebhooks = new Mock<IWebhookRepository>();

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversations.Object);
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUsers.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxes.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomers.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreads.Object);
        _mockUnitOfWork.Setup(u => u.SlaViolations).Returns(_mockSlaViolations.Object);
        _mockUnitOfWork.Setup(u => u.SatisfactionRatings).Returns(_mockSatisfactionRatings.Object);
        _mockUnitOfWork.Setup(u => u.KnowledgeArticles).Returns(_mockKnowledgeArticles.Object);
        _mockUnitOfWork.Setup(u => u.KnowledgeCategories).Returns(_mockKnowledgeCategories.Object);
        _mockUnitOfWork.Setup(u => u.CalendarEvents).Returns(_mockCalendarEvents.Object);
        _mockUnitOfWork.Setup(u => u.Workflows).Returns(_mockWorkflows.Object);
        _mockUnitOfWork.Setup(u => u.ImMessageLogs).Returns(_mockImMessageLogs.Object);
        _mockUnitOfWork.Setup(u => u.Tags).Returns(_mockTags.Object);
        _mockUnitOfWork.Setup(u => u.CannedResponses).Returns(_mockCannedResponses.Object);
        _mockUnitOfWork.Setup(u => u.Webhooks).Returns(_mockWebhooks.Object);
        _mockUnitOfWork.Setup(u => u.Repository<WorkflowExecutionLog>()).Returns(_mockWorkflowExecutionLogs.Object);

        _service = new DashboardService(_mockUnitOfWork.Object);

        SetupDefaults();
    }

    /// <summary>
    /// 设置默认返回值，避免因未设置 mock 导致空引用异常。
    /// 各测试可通过再次 Setup 覆盖这些默认值（Moq 中后设置的 Setup 优先匹配）。
    /// </summary>
    private void SetupDefaults()
    {
        _mockConversations.Setup(r => r.CountAsync()).ReturnsAsync(0);
        _mockConversations.Setup(r => r.CountByStatusAsync(It.IsAny<ConversationStatus>())).ReturnsAsync(0);
        _mockConversations.Setup(r => r.CountCreatedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>())).ReturnsAsync(0);
        _mockConversations.Setup(r => r.CountClosedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>())).ReturnsAsync(0);
        _mockConversations.Setup(r => r.CountUnassignedActiveAsync()).ReturnsAsync(0);
        _mockConversations.Setup(r => r.GetActiveCountByUserIdAsync())
            .ReturnsAsync(new Dictionary<int, int>());
        _mockConversations.Setup(r => r.GetClosedCountByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<int, int>());
        _mockConversations.Setup(r => r.GetReplyCountByUserIdAndDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<int, int>());
        _mockConversations.Setup(r => r.GetStatusCountsByMailboxIdAsync())
            .ReturnsAsync(new Dictionary<int, (int Active, int Pending, int Closed)>());
        _mockConversations.Setup(r => r.GetTrendAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()))
            .ReturnsAsync(new Dictionary<DateTime, (int NewCount, int ClosedCount)>());

        _mockUsers.Setup(r => r.CountAsync()).ReturnsAsync(0);
        _mockUsers.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<User>());
        _mockUsers.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<User>()));

        _mockMailboxes.Setup(r => r.CountAsync()).ReturnsAsync(0);
        _mockMailboxes.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Mailbox>());
        _mockMailboxes.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<Mailbox>()));

        _mockCustomers.Setup(r => r.CountAsync()).ReturnsAsync(0);

        _mockThreads.Setup(r => r.CalculateAverageFirstResponseTimeMinutesAsync()).ReturnsAsync(0.0);
        _mockThreads.Setup(r => r.CalculateAverageResolutionTimeMinutesAsync()).ReturnsAsync(0.0);

        _mockSlaViolations.Setup(r => r.GetActiveWarningsAsync()).ReturnsAsync(new List<SlaViolation>());
        _mockSlaViolations.Setup(r => r.GetBreachedAsync()).ReturnsAsync(new List<SlaViolation>());

        _mockSatisfactionRatings.Setup(r => r.GetStatsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new SatisfactionRatingStats());

        // 扩展模块默认返回值（避免 GetDashboardStatsAsync 调用时 NullReferenceException）
        // 知识库
        _mockKnowledgeArticles.Setup(r => r.CountAsync(It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<int[]>()))
            .ReturnsAsync(0);
        _mockKnowledgeArticles.Setup(r => r.SumViewCountAsync(It.IsAny<string?>())).ReturnsAsync(0);
        _mockKnowledgeArticles.Setup(r => r.SumLikeCountAsync(It.IsAny<string?>())).ReturnsAsync(0);
        _mockKnowledgeCategories.Setup(r => r.CountAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool?>(), It.IsAny<string?>()))
            .ReturnsAsync(0);

        // 日历
        _mockCalendarEvents.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<CalendarEvent>()));
        _mockCalendarEvents.Setup(r => r.GetOverviewAggregateAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync((new CalendarOverviewStats(), new List<CalendarEvent>()));

        // 工作流
        _mockWorkflows.Setup(r => r.CountAsync()).ReturnsAsync(0);
        _mockWorkflows.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<Workflow>()));
        _mockWorkflowExecutionLogs.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<WorkflowExecutionLog>()));

        // IM 队列
        _mockImMessageLogs.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<ImMessageLog>()));

        // 其他实体统计
        _mockTags.Setup(r => r.CountAsync()).ReturnsAsync(0);
        _mockCannedResponses.Setup(r => r.CountAsync()).ReturnsAsync(0);
        _mockWebhooks.Setup(r => r.CountAsync()).ReturnsAsync(0);
    }

    #region Happy Path

    [Fact]
    public async Task GetDashboardStatsAsync_WithPopulatedData_ShouldReturnAllStatsCorrectly()
    {
        // Arrange
        _mockConversations.Setup(r => r.CountAsync()).ReturnsAsync(100);
        _mockConversations.Setup(r => r.CountByStatusAsync(ConversationStatus.Active)).ReturnsAsync(40);
        _mockConversations.Setup(r => r.CountByStatusAsync(ConversationStatus.Pending)).ReturnsAsync(10);
        _mockConversations.Setup(r => r.CountByStatusAsync(ConversationStatus.Closed)).ReturnsAsync(50);
        _mockConversations.Setup(r => r.CountUnassignedActiveAsync()).ReturnsAsync(5);
        _mockConversations.Setup(r => r.CountCreatedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>())).ReturnsAsync(12);
        _mockConversations.Setup(r => r.CountClosedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>())).ReturnsAsync(8);

        _mockUsers.Setup(r => r.CountAsync()).ReturnsAsync(15);
        _mockCustomers.Setup(r => r.CountAsync()).ReturnsAsync(200);
        _mockMailboxes.Setup(r => r.CountAsync()).ReturnsAsync(3);

        _mockThreads.Setup(r => r.CalculateAverageFirstResponseTimeMinutesAsync()).ReturnsAsync(15.5);
        _mockThreads.Setup(r => r.CalculateAverageResolutionTimeMinutesAsync()).ReturnsAsync(180.0);

        var slaWarnings = new List<SlaViolation> { new(), new(), new() };
        var slaBreached = new List<SlaViolation> { new(), new() };
        _mockSlaViolations.Setup(r => r.GetActiveWarningsAsync()).ReturnsAsync(slaWarnings);
        _mockSlaViolations.Setup(r => r.GetBreachedAsync()).ReturnsAsync(slaBreached);

        var stats = new SatisfactionRatingStats { TotalCount = 50, AverageScore = 4.5 };
        _mockSatisfactionRatings.Setup(r => r.GetStatsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(stats);

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalConversations.Should().Be(100);
        result.ActiveConversations.Should().Be(40);
        result.PendingConversations.Should().Be(10);
        result.ClosedConversations.Should().Be(50);
        result.UnassignedCount.Should().Be(5);
        result.TotalUsers.Should().Be(15);
        result.TotalCustomers.Should().Be(200);
        result.TotalMailboxes.Should().Be(3);
        result.TodayNewConversations.Should().Be(12);
        result.TodayClosedConversations.Should().Be(8);
        result.AvgFirstResponseTimeMinutes.Should().Be(15.5);
        result.AvgResolutionTimeMinutes.Should().Be(180.0);
        result.SlaWarningCount.Should().Be(3);
        result.SlaBreachedCount.Should().Be(2);
        result.SatisfactionAverageScore.Should().Be(4.5);
        result.SatisfactionTotalCount.Should().Be(50);
    }

    #endregion

    #region Empty Data

    [Fact]
    public async Task GetDashboardStatsAsync_WithEmptyData_ShouldReturnZerosAndEmptyLists()
    {
        // Arrange - 默认 SetupDefaults 已返回空集合与零值

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalConversations.Should().Be(0);
        result.ActiveConversations.Should().Be(0);
        result.PendingConversations.Should().Be(0);
        result.ClosedConversations.Should().Be(0);
        result.UnassignedCount.Should().Be(0);
        result.TotalUsers.Should().Be(0);
        result.TotalCustomers.Should().Be(0);
        result.TotalMailboxes.Should().Be(0);
        result.TodayNewConversations.Should().Be(0);
        result.TodayClosedConversations.Should().Be(0);
        result.AvgFirstResponseTimeMinutes.Should().Be(0);
        result.AvgResolutionTimeMinutes.Should().Be(0);
        result.SlaWarningCount.Should().Be(0);
        result.SlaBreachedCount.Should().Be(0);
        result.SatisfactionAverageScore.Should().Be(0);
        result.SatisfactionTotalCount.Should().Be(0);
        result.UserWorkloads.Should().BeEmpty();
        result.MailboxStats.Should().BeEmpty();
        result.ConversationTrend.Should().BeEmpty();
    }

    #endregion

    #region MailboxId Forwarding

    [Fact]
    public async Task GetDashboardStatsAsync_WithMailboxId_ShouldForwardMailboxIdToRepositories()
    {
        // Arrange
        _mockConversations.Setup(r => r.CountCreatedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 7))
            .ReturnsAsync(20);
        _mockConversations.Setup(r => r.CountClosedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 7))
            .ReturnsAsync(10);
        _mockConversations.Setup(r => r.GetTrendAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 7))
            .ReturnsAsync(new Dictionary<DateTime, (int NewCount, int ClosedCount)>());

        // Act
        var result = await _service.GetDashboardStatsAsync(7);

        // Assert
        result.Should().NotBeNull();
        result.TodayNewConversations.Should().Be(20);
        result.TodayClosedConversations.Should().Be(10);

        _mockConversations.Verify(r => r.CountCreatedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 7), Times.Once);
        _mockConversations.Verify(r => r.CountClosedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 7), Times.Once);
        _mockConversations.Verify(r => r.GetTrendAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 7), Times.Once);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithExplicitNullMailboxId_ShouldForwardNullToRepositories()
    {
        // Arrange & Act
        var result = await _service.GetDashboardStatsAsync(null);

        // Assert
        result.Should().NotBeNull();
        _mockConversations.Verify(r => r.CountCreatedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null), Times.Once);
        _mockConversations.Verify(r => r.CountClosedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null), Times.Once);
        _mockConversations.Verify(r => r.GetTrendAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null), Times.Once);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithoutMailboxId_ShouldDefaultToNull()
    {
        // Arrange & Act - 不传 mailboxId 参数，验证默认值为 null
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.Should().NotBeNull();
        _mockConversations.Verify(r => r.CountCreatedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null), Times.Once);
        _mockConversations.Verify(r => r.CountClosedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null), Times.Once);
        _mockConversations.Verify(r => r.GetTrendAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null), Times.Once);
    }

    #endregion

    #region User Workloads

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldSortUserWorkloadsByActiveCountDescending()
    {
        // Arrange
        var users = new List<User>
        {
            new() { Id = 1, FirstName = "Alice", LastName = "Lee" },
            new() { Id = 2, FirstName = "Bob", LastName = "Wang" },
            new() { Id = 3, FirstName = "Carol", LastName = "Zhang" }
        };
        _mockUsers.Setup(r => r.GetAllAsync()).ReturnsAsync(users);
        _mockUsers.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(users));

        _mockConversations.Setup(r => r.GetActiveCountByUserIdAsync())
            .ReturnsAsync(new Dictionary<int, int>
            {
                { 1, 5 },  // Alice
                { 2, 10 }, // Bob
                { 3, 3 }   // Carol
            });
        _mockConversations.Setup(r => r.GetClosedCountByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<int, int>());
        _mockConversations.Setup(r => r.GetReplyCountByUserIdAndDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<int, int>());

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.UserWorkloads.Should().HaveCount(3);
        result.UserWorkloads[0].UserName.Should().Be("Bob Wang");
        result.UserWorkloads[0].ActiveCount.Should().Be(10);
        result.UserWorkloads[1].UserName.Should().Be("Alice Lee");
        result.UserWorkloads[1].ActiveCount.Should().Be(5);
        result.UserWorkloads[2].UserName.Should().Be("Carol Zhang");
        result.UserWorkloads[2].ActiveCount.Should().Be(3);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithUsersNotInCountDictionaries_ShouldDefaultToZero()
    {
        // Arrange
        var users = new List<User>
        {
            new() { Id = 1, FirstName = "Alice", LastName = "Lee" },
            new() { Id = 2, FirstName = "Bob", LastName = "Wang" }
        };
        _mockUsers.Setup(r => r.GetAllAsync()).ReturnsAsync(users);
        _mockUsers.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(users));

        // 只有 Alice 在字典中，Bob 不在（应默认为 0）
        _mockConversations.Setup(r => r.GetActiveCountByUserIdAsync())
            .ReturnsAsync(new Dictionary<int, int> { { 1, 8 } });
        _mockConversations.Setup(r => r.GetClosedCountByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<int, int> { { 1, 4 } });
        _mockConversations.Setup(r => r.GetReplyCountByUserIdAndDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<int, int> { { 1, 12 } });

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.UserWorkloads.Should().HaveCount(2);

        var bob = result.UserWorkloads.Single(w => w.UserId == 2);
        bob.UserName.Should().Be("Bob Wang");
        bob.ActiveCount.Should().Be(0);
        bob.ClosedToday.Should().Be(0);
        bob.RepliesToday.Should().Be(0);

        var alice = result.UserWorkloads.Single(w => w.UserId == 1);
        alice.ActiveCount.Should().Be(8);
        alice.ClosedToday.Should().Be(4);
        alice.RepliesToday.Should().Be(12);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldMapUserFullNameUsingGetFullNameMethod()
    {
        // Arrange
        var users = new List<User>
        {
            new() { Id = 1, FirstName = "John", LastName = "Doe" }
        };
        _mockUsers.Setup(r => r.GetAllAsync()).ReturnsAsync(users);
        _mockUsers.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(users));
        _mockConversations.Setup(r => r.GetActiveCountByUserIdAsync())
            .ReturnsAsync(new Dictionary<int, int>());
        _mockConversations.Setup(r => r.GetClosedCountByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<int, int>());
        _mockConversations.Setup(r => r.GetReplyCountByUserIdAndDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new Dictionary<int, int>());

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.UserWorkloads.Should().HaveCount(1);
        result.UserWorkloads[0].UserId.Should().Be(1);
        result.UserWorkloads[0].UserName.Should().Be("John Doe");
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithNoUsers_ShouldReturnEmptyUserWorkloads()
    {
        // Arrange - 默认 SetupDefaults 已返回空用户列表

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.UserWorkloads.Should().BeEmpty();
    }

    #endregion

    #region Mailbox Stats

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldMapMailboxStatsFromRepository()
    {
        // Arrange
        var mailboxes = new List<Mailbox>
        {
            new() { Id = 1, Name = "Sales" },
            new() { Id = 2, Name = "Support" }
        };
        _mockMailboxes.Setup(r => r.GetAllAsync()).ReturnsAsync(mailboxes);
        _mockMailboxes.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(mailboxes));

        _mockConversations.Setup(r => r.GetStatusCountsByMailboxIdAsync())
            .ReturnsAsync(new Dictionary<int, (int Active, int Pending, int Closed)>
            {
                { 1, (10, 5, 20) },
                { 2, (15, 3, 25) }
            });

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.MailboxStats.Should().HaveCount(2);

        var sales = result.MailboxStats.Single(m => m.MailboxId == 1);
        sales.MailboxName.Should().Be("Sales");
        sales.ActiveCount.Should().Be(10);
        sales.PendingCount.Should().Be(5);
        sales.ClosedCount.Should().Be(20);

        var support = result.MailboxStats.Single(m => m.MailboxId == 2);
        support.MailboxName.Should().Be("Support");
        support.ActiveCount.Should().Be(15);
        support.PendingCount.Should().Be(3);
        support.ClosedCount.Should().Be(25);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithMailboxNotInStatusCounts_ShouldDefaultToZero()
    {
        // Arrange
        var mailboxes = new List<Mailbox>
        {
            new() { Id = 1, Name = "Sales" },   // 在字典中
            new() { Id = 2, Name = "Support" }  // 不在字典中（应默认为 0）
        };
        _mockMailboxes.Setup(r => r.GetAllAsync()).ReturnsAsync(mailboxes);
        _mockMailboxes.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(mailboxes));

        _mockConversations.Setup(r => r.GetStatusCountsByMailboxIdAsync())
            .ReturnsAsync(new Dictionary<int, (int Active, int Pending, int Closed)>
            {
                { 1, (10, 5, 20) }
            });

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.MailboxStats.Should().HaveCount(2);

        var support = result.MailboxStats.Single(m => m.MailboxId == 2);
        support.MailboxName.Should().Be("Support");
        support.ActiveCount.Should().Be(0);
        support.PendingCount.Should().Be(0);
        support.ClosedCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithNoMailboxes_ShouldReturnEmptyMailboxStats()
    {
        // Arrange - 默认 SetupDefaults 已返回空邮箱列表

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.MailboxStats.Should().BeEmpty();
    }

    #endregion

    #region Conversation Trend

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldSortConversationTrendByDateAscending()
    {
        // Arrange - 字典中的 key 故意乱序，验证返回结果按日期升序排列
        var day1 = new DateTime(2024, 1, 3);
        var day2 = new DateTime(2024, 1, 1);
        var day3 = new DateTime(2024, 1, 2);

        _mockConversations.Setup(r => r.GetTrendAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()))
            .ReturnsAsync(new Dictionary<DateTime, (int NewCount, int ClosedCount)>
            {
                { day1, (3, 1) },
                { day2, (5, 2) },
                { day3, (4, 0) }
            });

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.ConversationTrend.Should().HaveCount(3);
        result.ConversationTrend[0].Date.Should().Be("2024-01-01");
        result.ConversationTrend[0].NewCount.Should().Be(5);
        result.ConversationTrend[0].ClosedCount.Should().Be(2);
        result.ConversationTrend[1].Date.Should().Be("2024-01-02");
        result.ConversationTrend[2].Date.Should().Be("2024-01-03");
    }

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldFormatTrendDateAsYYYY_MM_DD()
    {
        // Arrange
        var date = new DateTime(2024, 12, 25);

        _mockConversations.Setup(r => r.GetTrendAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()))
            .ReturnsAsync(new Dictionary<DateTime, (int NewCount, int ClosedCount)>
            {
                { date, (7, 3) }
            });

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.ConversationTrend.Should().HaveCount(1);
        result.ConversationTrend[0].Date.Should().Be("2024-12-25");
        result.ConversationTrend[0].NewCount.Should().Be(7);
        result.ConversationTrend[0].ClosedCount.Should().Be(3);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithEmptyTrend_ShouldReturnEmptyList()
    {
        // Arrange - 默认 SetupDefaults 已返回空字典

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.ConversationTrend.Should().BeEmpty();
    }

    #endregion

    #region SLA

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldCalculateSlaCountsFromListCount()
    {
        // Arrange
        var warnings = new List<SlaViolation>
        {
            new(), new(), new(), new()
        };
        var breached = new List<SlaViolation>
        {
            new(), new()
        };
        _mockSlaViolations.Setup(r => r.GetActiveWarningsAsync()).ReturnsAsync(warnings);
        _mockSlaViolations.Setup(r => r.GetBreachedAsync()).ReturnsAsync(breached);

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.SlaWarningCount.Should().Be(4);
        result.SlaBreachedCount.Should().Be(2);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithEmptySlaLists_ShouldReturnZeroCounts()
    {
        // Arrange - 默认 SetupDefaults 已返回空列表

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.SlaWarningCount.Should().Be(0);
        result.SlaBreachedCount.Should().Be(0);
    }

    #endregion

    #region Satisfaction Stats

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldMapSatisfactionStatsFromRepository()
    {
        // Arrange
        var stats = new SatisfactionRatingStats
        {
            TotalCount = 100,
            AverageScore = 4.7,
            ExcellentCount = 60,
            GoodCount = 30,
            AverageCount = 5,
            PoorCount = 3,
            BadCount = 2
        };
        _mockSatisfactionRatings.Setup(r => r.GetStatsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(stats);

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.SatisfactionTotalCount.Should().Be(100);
        result.SatisfactionAverageScore.Should().Be(4.7);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithDefaultSatisfactionStats_ShouldReturnZeros()
    {
        // Arrange - GetStatsAsync 返回默认的 SatisfactionRatingStats（所有字段为 0）

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.SatisfactionTotalCount.Should().Be(0);
        result.SatisfactionAverageScore.Should().Be(0);
    }

    #endregion

    #region Response Times

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldMapResponseTimesFromThreadRepository()
    {
        // Arrange
        _mockThreads.Setup(r => r.CalculateAverageFirstResponseTimeMinutesAsync()).ReturnsAsync(25.5);
        _mockThreads.Setup(r => r.CalculateAverageResolutionTimeMinutesAsync()).ReturnsAsync(320.75);

        // Act
        var result = await _service.GetDashboardStatsAsync();

        // Assert
        result.AvgFirstResponseTimeMinutes.Should().Be(25.5);
        result.AvgResolutionTimeMinutes.Should().Be(320.75);
    }

    #endregion

    #region Repository Calls Verification

    [Fact]
    public async Task GetDashboardStatsAsync_ShouldCallAllRequiredRepositoryMethodsExactlyOnce()
    {
        // Arrange & Act
        await _service.GetDashboardStatsAsync();

        // Assert - 验证所有需要的仓储方法都被调用一次
        _mockConversations.Verify(r => r.CountAsync(), Times.Once);
        _mockConversations.Verify(r => r.CountByStatusAsync(ConversationStatus.Active), Times.Once);
        _mockConversations.Verify(r => r.CountByStatusAsync(ConversationStatus.Pending), Times.Once);
        _mockConversations.Verify(r => r.CountByStatusAsync(ConversationStatus.Closed), Times.Once);
        _mockConversations.Verify(r => r.CountCreatedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()), Times.Once);
        _mockConversations.Verify(r => r.CountClosedInDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()), Times.Once);
        _mockConversations.Verify(r => r.CountUnassignedActiveAsync(), Times.Once);
        _mockConversations.Verify(r => r.GetActiveCountByUserIdAsync(), Times.Once);
        _mockConversations.Verify(r => r.GetClosedCountByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Once);
        _mockConversations.Verify(r => r.GetReplyCountByUserIdAndDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Once);
        _mockConversations.Verify(r => r.GetStatusCountsByMailboxIdAsync(), Times.Once);
        _mockConversations.Verify(r => r.GetTrendAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>()), Times.Once);

        _mockUsers.Verify(r => r.CountAsync(), Times.Once);
        _mockUsers.Verify(r => r.GetQueryable(It.IsAny<bool>()), Times.Once);

        _mockMailboxes.Verify(r => r.CountAsync(), Times.Once);
        _mockMailboxes.Verify(r => r.GetQueryable(It.IsAny<bool>()), Times.Once);

        _mockCustomers.Verify(r => r.CountAsync(), Times.Once);

        _mockThreads.Verify(r => r.CalculateAverageFirstResponseTimeMinutesAsync(), Times.Once);
        _mockThreads.Verify(r => r.CalculateAverageResolutionTimeMinutesAsync(), Times.Once);

        _mockSlaViolations.Verify(r => r.GetActiveWarningsAsync(), Times.Once);
        _mockSlaViolations.Verify(r => r.GetBreachedAsync(), Times.Once);

        _mockSatisfactionRatings.Verify(r => r.GetStatsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Once);
    }

    #endregion
}
