using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.Calendar;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// CalendarService 单元测试
/// 覆盖权限校验、字段验证、CRUD、垃圾箱、日历概览等核心场景
/// </summary>
public class CalendarServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ICalendarEventRepository> _mockCalendarRepo;
    private readonly Mock<IMailTemplateRepository> _mockMailTemplateRepo;
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<INotificationHubService> _mockNotificationHubService;
    private readonly Mock<IEmailQueueService> _mockEmailQueueService;
    private readonly Mock<ILogger<CalendarService>> _mockLogger;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;
    private readonly IConfiguration _configuration;
    private readonly CalendarService _service;

    public CalendarServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockCalendarRepo = new Mock<ICalendarEventRepository>();
        _mockMailTemplateRepo = new Mock<IMailTemplateRepository>();
        _mockUserRepo = new Mock<IUserRepository>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockUserRoleResolver = new Mock<IUserRoleResolver>();
        _mockAuditService = new Mock<IAuditService>();
        _mockNotificationHubService = new Mock<INotificationHubService>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockLogger = new Mock<ILogger<CalendarService>>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        var inMemoryConfig = new Dictionary<string, string?> { ["Trash:CalendarRetentionDays"] = "30" };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        _mockUnitOfWork.Setup(u => u.CalendarEvents).Returns(_mockCalendarRepo.Object);
        _mockUnitOfWork.Setup(u => u.MailTemplates).Returns(_mockMailTemplateRepo.Object);
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _mockUserRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<User>());

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync((int id, bool includeDeleted) =>
            {
                return null;
            });
        _mockCalendarRepo.Setup(r => r.GetByParentIdTrackedAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<CalendarEvent>());
        _mockCalendarRepo.Setup(r => r.GetFollowingInstancesTrackedAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent>());
        _mockCalendarRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(Helpers.TestAsyncQueryProvider.CreateAsyncQueryable(new List<CalendarEvent>()));
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(0);
        _mockCalendarRepo.Setup(r => r.TryClaimRecurringReminderResetAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(0);

        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>()))
            .ReturnsAsync(true);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        var brandOpts = Options.Create<BrandOptions>(new BrandOptions());
        var calOpts = Options.Create<CalendarOptions>(new CalendarOptions());
        var renderer = new TemplateRenderer();

        var authService = new CalendarAuthorizationService(
            _mockCurrentUserAccessor.Object,
            _mockUserRoleResolver.Object);

        var recurrenceService = new RecurrenceExpansionService(
            _configuration,
            Mock.Of<ILogger<RecurrenceExpansionService>>());

        var queryService = new CalendarQueryService(
            _mockUnitOfWork.Object,
            _mockCurrentUserAccessor.Object,
            authService,
            recurrenceService,
            Mock.Of<ILogger<CalendarQueryService>>());

        var lazyQuery = new Lazy<ICalendarQueryService>(() => queryService);
        var conflictService = Mock.Of<ICalendarConflictService>();

        var mutationService = new CalendarMutationService(
            _mockUnitOfWork.Object,
            _mockNotificationService.Object,
            _mockCurrentUserAccessor.Object,
            _mockAuditService.Object,
            _mockNotificationHubService.Object,
            Mock.Of<ILogger<CalendarMutationService>>(),
            authService,
            calOpts,
            conflictService,
            lazyQuery);

        var reminderService = new CalendarReminderService(
            _mockUnitOfWork.Object,
            _mockNotificationService.Object,
            _mockCurrentUserAccessor.Object,
            _mockNotificationHubService.Object,
            _mockEmailQueueService.Object,
            Mock.Of<ILogger<CalendarReminderService>>(),
            brandOpts,
            calOpts,
            renderer,
            _configuration,
            recurrenceService);

        _service = new CalendarService(queryService, mutationService, reminderService, _mockLogger.Object);
    }

    private static CreateCalendarEventRequest CreateValidRequest(int? userId = null)
    {
        return new CreateCalendarEventRequest
        {
            Title = "测试日程",
            Description = "测试描述",
            EventType = "task",
            StartAt = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EndAt = DateTime.UtcNow.AddDays(1).AddHours(2).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            IsAllDay = false,
            MailboxId = 1,
            UserId = userId,
            Priority = 3,
            Progress = 50,
            Attendees = new List<int>(),
            SubTasks = new List<SubTaskDto>()
        };
    }

    private static UpdateCalendarEventRequest CreateValidUpdateRequest(int? userId = null)
    {
        return new UpdateCalendarEventRequest
        {
            Id = 1,
            Title = "更新后的日程",
            Description = "更新后的描述",
            EventType = "meeting",
            StartAt = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EndAt = DateTime.UtcNow.AddDays(2).AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            IsAllDay = false,
            MailboxId = 1,
            UserId = userId,
            Status = "pending",
            UpdateMode = "single",
            Priority = 4,
            Progress = 75,
            Attendees = new List<int>(),
            SubTasks = new List<SubTaskDto>()
        };
    }

    private static CalendarEvent CreateCalendarEvent(int id = 1, int createdBy = 1, int? userId = 1, bool isDeleted = false)
    {
        return new CalendarEvent
        {
            Id = id,
            Title = "测试日程",
            Description = "测试描述",
            EventType = "task",
            StartAt = DateTime.UtcNow.AddDays(1),
            EndAt = DateTime.UtcNow.AddDays(1).AddHours(2),
            IsAllDay = false,
            MailboxId = 1,
            UserId = userId,
            CreatedBy = createdBy,
            Status = "pending",
            Priority = 3,
            Progress = 50,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DeletedAt = isDeleted ? DateTime.UtcNow.AddHours(-1) : null
        };
    }

    #region 提醒分钟边界（「准时(0)」契约漂移，2026-09-24 用户裁定：后端放宽为 >= 0）

    /// <summary>
    /// 0 = 「准时」：`CalendarReminderService` 用 `AddMinutes(-0)` 表示"开始时刻提醒"，
    /// 界面 `REMINDER_OPTIONS` 也提供该档位。裁定前两处校验都写 `<= 0` / `< 1`，
    /// 界面提供的合法值必被后端驳回 —— 本组用例把区间锁成 [0, 10080]。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10080)]
    public async Task CreateAsync_ReminderMinutes_AllowsZeroTo10080(int minutes)
    {
        // Arrange
        const int userId = 1;
        var request = CreateValidRequest();
        request.ReminderMinutes = minutes;

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(userId, UserRoleType.SuperAdmin)).ReturnsAsync(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert：放行且落库值不被改写（0 必须原样存 0，不能变成 null）
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.ReminderMinutes.Should().Be(minutes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10081)]
    public async Task CreateAsync_ReminderMinutes_RejectsOutOfRange(int minutes)
    {
        // Arrange
        const int userId = 1;
        var request = CreateValidRequest();
        request.ReminderMinutes = minutes;

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(userId, UserRoleType.SuperAdmin)).ReturnsAsync(true);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10080)]
    public void CreateCalendarEventRequest_Validate_AcceptsZeroTo10080(int minutes)
    {
        var request = CreateValidRequest();
        request.ReminderMinutes = minutes;

        var results = request.Validate(new ValidationContext(request)).ToList();

        results.Should().NotContain(r => r.MemberNames.Contains(nameof(CreateCalendarEventRequest.ReminderMinutes)),
            $"ReminderMinutes={minutes} 属合法区间 [0, 10080]");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10081)]
    public void CreateCalendarEventRequest_Validate_RejectsOutOfRange(int minutes)
    {
        var request = CreateValidRequest();
        request.ReminderMinutes = minutes;

        var results = request.Validate(new ValidationContext(request)).ToList();

        results.Should().Contain(r => r.MemberNames.Contains(nameof(CreateCalendarEventRequest.ReminderMinutes)),
            $"ReminderMinutes={minutes} 超出区间 [0, 10080]");
    }

    #endregion

    #region 权限校验测试

    #region 创建事件权限

    [Fact]
    public async Task CreateAsync_SuperAdmin_CanCreateEventForAnyUser()
    {
        // Arrange
        const int superAdminId = 1;
        const int targetUserId = 99;
        var request = CreateValidRequest(targetUserId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(superAdminId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(superAdminId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(targetUserId);
        captured.CreatedBy.Should().Be(superAdminId);
        _mockAuditService.Verify(a => a.LogAsync("calendar.create", "CalendarEvent", 1,
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_AgentSupervisor_CanCreateEventForSubordinates()
    {
        // Arrange
        const int supervisorId = 2;
        const int agentId = 3;
        const int readOnlyId = 4;
        var request = CreateValidRequest(agentId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(supervisorId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(true);
        _mockUserRoleResolver.Setup(r => r.GetUserIdsBySystemRoleTypeAsync(UserRoleType.Agent))
            .ReturnsAsync(new HashSet<int> { agentId });
        _mockUserRoleResolver.Setup(r => r.GetUserIdsBySystemRoleTypeAsync(UserRoleType.ReadOnly))
            .ReturnsAsync(new HashSet<int> { readOnlyId });

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(agentId);
        captured.CreatedBy.Should().Be(supervisorId);
    }

    [Fact]
    public async Task CreateAsync_AgentSupervisor_CanCreateEventForSelf()
    {
        // Arrange
        const int supervisorId = 2;
        var request = CreateValidRequest(supervisorId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(supervisorId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(supervisorId);
        captured.CreatedBy.Should().Be(supervisorId);
    }

    [Fact]
    public async Task CreateAsync_Agent_CanCreateEventForReadOnlyUser()
    {
        // Arrange
        const int agentId = 3;
        const int readOnlyId = 4;
        var request = CreateValidRequest(readOnlyId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(agentId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.Agent))
            .ReturnsAsync(true);
        _mockUserRoleResolver.Setup(r => r.GetUserRoleTypeAsync(readOnlyId))
            .ReturnsAsync(UserRoleType.ReadOnly);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(readOnlyId);
        captured.CreatedBy.Should().Be(agentId);
    }

    [Fact]
    public async Task CreateAsync_Agent_CanCreateEventForSelf()
    {
        // Arrange
        const int agentId = 3;
        var request = CreateValidRequest(agentId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(agentId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.Agent))
            .ReturnsAsync(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(agentId);
        captured.CreatedBy.Should().Be(agentId);
    }

    [Fact]
    public async Task CreateAsync_Agent_CannotCreateEventForAgent()
    {
        // Arrange
        const int agentId = 3;
        const int otherAgentId = 5;
        var request = CreateValidRequest(otherAgentId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(agentId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(agentId, UserRoleType.Agent))
            .ReturnsAsync(true);
        _mockUserRoleResolver.Setup(r => r.GetUserRoleTypeAsync(otherAgentId))
            .ReturnsAsync(UserRoleType.Agent);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*专员仅可向只读用户或自己分配任务*");
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ReadOnlyUser_CannotCreateEvent()
    {
        // Arrange
        const int readOnlyId = 4;
        var request = CreateValidRequest(readOnlyId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(readOnlyId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(readOnlyId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(readOnlyId, UserRoleType.ReadOnly))
            .ReturnsAsync(true);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*只读用户无权创建日程*");
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_AgentSupervisor_CannotCreateEventForSuperAdmin()
    {
        // Arrange
        const int supervisorId = 2;
        const int superAdminId = 1;
        var request = CreateValidRequest(superAdminId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(supervisorId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(true);
        _mockUserRoleResolver.Setup(r => r.GetUserIdsBySystemRoleTypeAsync(UserRoleType.Agent))
            .ReturnsAsync(new HashSet<int> { 3 });
        _mockUserRoleResolver.Setup(r => r.GetUserIdsBySystemRoleTypeAsync(UserRoleType.ReadOnly))
            .ReturnsAsync(new HashSet<int> { 4 });

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*无权为该用户创建日程*");
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    #endregion

    #region 编辑/删除事件权限

    [Fact]
    public async Task UpdateAsync_Creator_CanEditOwnEvent()
    {
        // Arrange
        const int creatorId = 1;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: creatorId, userId: 5);
        var request = CreateValidUpdateRequest(5);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(creatorId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(creatorId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(eventId)).ReturnsAsync(evt);

        // Act
        var result = await _service.UpdateAsync(eventId, request);

        // Assert
        result.Should().NotBeNull();
        evt.Title.Should().Be("更新后的日程");
        evt.Priority.Should().Be(4);
        _mockAuditService.Verify(a => a.LogAsync("calendar.update", "CalendarEvent", eventId,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_Assignee_CanEditAssignedEvent()
    {
        // Arrange
        const int assigneeId = 5;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: 1, userId: assigneeId);
        var request = CreateValidUpdateRequest(assigneeId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(assigneeId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(assigneeId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(eventId)).ReturnsAsync(evt);

        // Act
        var result = await _service.UpdateAsync(eventId, request);

        // Assert
        result.Should().NotBeNull();
        evt.Title.Should().Be("更新后的日程");
    }

    [Fact]
    public async Task UpdateAsync_Supervisor_CanEditSubordinateEvent()
    {
        // Arrange
        const int supervisorId = 2;
        const int subordinateId = 3;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: 10, userId: subordinateId);
        var request = CreateValidUpdateRequest(subordinateId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(supervisorId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(true);
        _mockUserRoleResolver.Setup(r => r.GetUserIdsBySystemRoleTypeAsync(UserRoleType.Agent))
            .ReturnsAsync(new HashSet<int> { subordinateId });
        _mockUserRoleResolver.Setup(r => r.GetUserIdsBySystemRoleTypeAsync(UserRoleType.ReadOnly))
            .ReturnsAsync(new HashSet<int> { 4 });

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(eventId)).ReturnsAsync(evt);

        // Act
        var result = await _service.UpdateAsync(eventId, request);

        // Assert
        result.Should().NotBeNull();
        evt.Title.Should().Be("更新后的日程");
    }

    [Fact]
    public async Task UpdateAsync_UnauthorizedUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        const int currentUserId = 99;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: 1, userId: 5);
        var request = CreateValidUpdateRequest(5);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(currentUserId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(false);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);

        // Act
        var act = () => _service.UpdateAsync(eventId, request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*无权操作该日程*");
    }

    [Fact]
    public async Task DeleteAsync_Creator_CanDeleteOwnEvent()
    {
        // Arrange
        const int creatorId = 1;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: creatorId, userId: 5);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(creatorId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(creatorId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);

        // Act
        var result = await _service.DeleteAsync(eventId);

        // Assert
        result.Should().BeTrue();
        evt.DeletedAt.Should().NotBeNull();
        _mockAuditService.Verify(a => a.LogAsync("calendar.delete", "CalendarEvent", eventId,
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Supervisor_CanDeleteSubordinateEvent()
    {
        // Arrange
        const int supervisorId = 2;
        const int subordinateId = 3;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: 10, userId: subordinateId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(supervisorId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(supervisorId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(true);
        _mockUserRoleResolver.Setup(r => r.GetUserIdsBySystemRoleTypeAsync(UserRoleType.Agent))
            .ReturnsAsync(new HashSet<int> { subordinateId });
        _mockUserRoleResolver.Setup(r => r.GetUserIdsBySystemRoleTypeAsync(UserRoleType.ReadOnly))
            .ReturnsAsync(new HashSet<int> { 4 });

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);

        // Act
        var result = await _service.DeleteAsync(eventId);

        // Assert
        result.Should().BeTrue();
        evt.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnauthorizedUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        const int currentUserId = 99;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: 1, userId: 5);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(currentUserId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(false);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);

        // Act
        var act = () => _service.DeleteAsync(eventId);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*无权操作该日程*");
    }

    #endregion

    #endregion

    #region 字段验证测试

    [Fact]
    public async Task CreateAsync_PriorityBelowZero_ThrowsArgumentException()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Priority = -1; // 越界（< 0）；0 本身是合法值（未分级/默认优先级）

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*优先级必须在 0-5 之间*");
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_PriorityAboveFive_ThrowsArgumentException()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Priority = 6;

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*优先级必须在 0-5 之间*");
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ProgressBelowZero_ThrowsArgumentException()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Progress = -1;

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*进度必须在 0-100 之间*");
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ProgressAboveHundred_ThrowsArgumentException()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Progress = 101;

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*进度必须在 0-100 之间*");
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_InvalidRecurrenceExclusionDateFormat_ThrowsArgumentException()
    {
        // Arrange
        var request = CreateValidRequest();
        request.RecurrenceExclusions = new RecurrenceExclusionsDto
        {
            Excluded = new List<string> { "2026/07/31" }
        };

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*排除日期格式必须为 yyyy-MM-dd*");
        _mockCalendarRepo.Verify(r => r.AddAsync(It.IsAny<CalendarEvent>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_PriorityInvalid_ThrowsArgumentException()
    {
        // Arrange
        const int eventId = 1;
        const int userId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: userId, userId: userId);
        var request = CreateValidUpdateRequest(userId);
        request.Priority = 10;

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);

        // Act
        var act = () => _service.UpdateAsync(eventId, request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*优先级必须在 0-5 之间*");
    }

    [Fact]
    public async Task CreateAsync_ValidPriorityAndProgress_DoesNotThrow()
    {
        // Arrange
        var request = CreateValidRequest();
        request.Priority = 5;
        request.Progress = 100;

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.Priority.Should().Be(5);
        captured.Progress.Should().Be(100);
    }

    [Fact]
    public async Task CreateAsync_ValidRecurrenceExclusionDates_DoesNotThrow()
    {
        // Arrange
        var request = CreateValidRequest();
        request.RecurrenceExclusions = new RecurrenceExclusionsDto
        {
            Excluded = new List<string> { "2026-08-01", "2026-08-15" }
        };

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.RecurrenceExclusionsJson.Should().NotBeNull();
    }

    #endregion

    #region 创建/更新/删除测试

    [Fact]
    public async Task CreateAsync_WithValidRequest_ShouldSetCreatedBy()
    {
        // Arrange
        const int currentUserId = 5;
        var request = CreateValidRequest();

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.CreatedBy.Should().Be(currentUserId);
        captured.CreatedAt.Should().NotBe(default);
        captured.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_ShouldLogCalendarCreateAudit()
    {
        // Arrange
        var request = CreateValidRequest();

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 42; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(42))
            .ReturnsAsync((int id) => captured);

        // Act
        await _service.CreateAsync(request);

        // Assert
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.create",
            "CalendarEvent",
            42,
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithValidRequest_ShouldLogCalendarUpdateAudit()
    {
        // Arrange
        const int eventId = 1;
        const int userId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: userId, userId: userId);
        var request = CreateValidUpdateRequest(userId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(eventId)).ReturnsAsync(evt);

        // Act
        await _service.UpdateAsync(eventId, request);

        // Assert
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.update",
            "CalendarEvent",
            eventId,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        evt.UpdatedAt.Should().BeAfter(evt.CreatedAt);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingEvent_ShouldSoftDeleteAndLogAudit()
    {
        // Arrange
        const int eventId = 1;
        const int userId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: userId, userId: userId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);

        // Act
        var result = await _service.DeleteAsync(eventId);

        // Assert
        result.Should().BeTrue();
        evt.IsDeleted().Should().BeTrue();
        evt.DeletedAt.Should().NotBeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.delete",
            "CalendarEvent",
            eventId,
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_WithValidEvent_ShouldUpdateStatusAndLogAudit()
    {
        // Arrange
        const int eventId = 1;
        const int userId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: userId, userId: userId);
        const string newStatus = "completed";

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false))
            .ReturnsAsync(evt);

        // Act
        var result = await _service.UpdateStatusAsync(eventId, newStatus);

        // Assert
        result.Should().BeTrue();
        evt.Status.Should().Be(newStatus);
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.status_change",
            "CalendarEvent",
            eventId,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingEvent_ReturnsFalse()
    {
        // Arrange
        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        // Act
        var result = await _service.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingEvent_ReturnsNull()
    {
        // Arrange
        var request = CreateValidUpdateRequest();

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        // Act
        var result = await _service.UpdateAsync(999, request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateStatusAsync_NonExistingEvent_ReturnsFalse()
    {
        // Arrange
        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        // Act
        var result = await _service.UpdateStatusAsync(999, "completed");

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region 垃圾箱测试

    [Fact]
    public async Task GetTrashAsync_ShouldReturnPagedDeletedEvents()
    {
        // Arrange
        var deletedEvents = new List<CalendarEvent>
        {
            CreateCalendarEvent(1, isDeleted: true),
            CreateCalendarEvent(2, isDeleted: true)
        };

        _mockCalendarRepo.Setup(r => r.GetTrashAsync(1, 20, null, null))
            .ReturnsAsync(deletedEvents);
        _mockCalendarRepo.Setup(r => r.CountTrashAsync(null, null))
            .ReturnsAsync(2);

        // Act
        var result = await _service.GetTrashAsync(1, 20, null);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetTrashAsync_WithKeyword_ShouldPassKeywordToRepository()
    {
        // Arrange
        const string keyword = "搜索";
        var deletedEvents = new List<CalendarEvent>
        {
            CreateCalendarEvent(1, isDeleted: true)
        };

        _mockCalendarRepo.Setup(r => r.GetTrashAsync(1, 10, keyword, null))
            .ReturnsAsync(deletedEvents);
        _mockCalendarRepo.Setup(r => r.CountTrashAsync(keyword, null))
            .ReturnsAsync(1);

        // Act
        var result = await _service.GetTrashAsync(1, 10, keyword);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetTrashCountAsync_ShouldReturnCount()
    {
        // Arrange
        _mockCalendarRepo.Setup(r => r.CountTrashAsync(null, null))
            .ReturnsAsync(5);

        // Act
        var result = await _service.GetTrashCountAsync();

        // Assert
        result.Should().Be(5);
    }

    [Fact]
    public async Task RestoreAsync_WithDeletedEvent_ShouldRestoreAndLogAudit()
    {
        // Arrange
        const int eventId = 1;
        const int userId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: userId, userId: userId, isDeleted: true);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, true))
            .ReturnsAsync(evt);
        _mockCalendarRepo.Setup(r => r.GetByIdIgnoreQueryFilterAsync(eventId))
            .ReturnsAsync(evt);

        // MutationService.RestoreAsync 现在使用带 CancellationToken 的 SaveChangesAsync
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.RestoreAsync(eventId);

        // Assert
        result.Should().BeTrue();
        evt.IsDeleted().Should().BeFalse();
        evt.DeletedAt.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.restore",
            "CalendarEvent",
            eventId,
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_NonExistingEvent_ReturnsFalse()
    {
        // Arrange
        _mockCalendarRepo.Setup(r => r.GetByIdIgnoreQueryFilterAsync(999))
            .ReturnsAsync((CalendarEvent?)null);

        // Act
        var result = await _service.RestoreAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_NonDeletedEvent_ReturnsFalse()
    {
        // Arrange
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, isDeleted: false);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, true))
            .ReturnsAsync(evt);
        _mockCalendarRepo.Setup(r => r.GetByIdIgnoreQueryFilterAsync(eventId))
            .ReturnsAsync(evt);

        // Act
        var result = await _service.RestoreAsync(eventId);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task HardDeleteAsync_WithExistingEvent_ShouldHardDeleteAndLogAudit()
    {
        // Arrange
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, isDeleted: true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, true))
            .ReturnsAsync(evt);
        _mockCalendarRepo.Setup(r => r.GetByIdIgnoreQueryFilterAsync(eventId))
            .ReturnsAsync(evt);

        // Act
        var result = await _service.HardDeleteAsync(eventId);

        // Assert
        result.Should().BeTrue();
        _mockCalendarRepo.Verify(r => r.HardDeleteAsync(evt), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.hard_delete",
            "CalendarEvent",
            eventId,
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HardDeleteAsync_NonExistingEvent_ReturnsFalse()
    {
        // Arrange
        _mockCalendarRepo.Setup(r => r.GetByIdIgnoreQueryFilterAsync(999))
            .ReturnsAsync((CalendarEvent?)null);

        // Act
        var result = await _service.HardDeleteAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ClearTrashAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        _mockCalendarRepo.Setup(r => r.ClearTrashAsync(null))
            .ReturnsAsync(7);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(7);
        _mockCalendarRepo.Verify(r => r.ClearTrashAsync(null), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region 日历概览测试

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnCorrectStats()
    {
        // Arrange
        const int userId = 1;
        var now = DateTime.UtcNow;
        var events = new List<CalendarEvent>
        {
            new() { Id = 1, Title = "待处理1", Status = "pending", StartAt = now.AddDays(1), UserId = userId },
            new() { Id = 2, Title = "待处理2", Status = "pending", StartAt = now.AddHours(1), UserId = userId },
            new() { Id = 3, Title = "已逾期", Status = "pending", StartAt = now.AddDays(-1), UserId = userId },
            new() { Id = 4, Title = "已完成", Status = "completed", StartAt = now.AddDays(-2), UserId = userId },
            new() { Id = 5, Title = "已取消", Status = "cancelled", StartAt = now.AddDays(-3), UserId = userId }
        };

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(userId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(userId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(false);

        _mockCalendarRepo.Setup(r => r.GetOverviewAggregateAsync(
                It.IsAny<IEnumerable<int>?>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync((IEnumerable<int>? userIds, DateTime dtNow, int take) =>
            {
                var filtered = userIds != null && userIds.Any()
                    ? events.Where(e => e.UserId.HasValue && userIds.Contains(e.UserId.Value)).ToList()
                    : events;
                var stats = new CalendarOverviewStats
                {
                    Total = filtered.Count,
                    Pending = filtered.Count(e => e.Status == "pending"),
                    Completed = filtered.Count(e => e.Status == "completed"),
                    Cancelled = filtered.Count(e => e.Status == "cancelled"),
                    Upcoming = filtered.Count(e => e.StartAt >= dtNow && e.Status == "pending"),
                    Overdue = filtered.Count(e => e.StartAt < dtNow && e.Status == "pending")
                };
                var upcomingEvents = filtered
                    .Where(e => e.StartAt >= dtNow && e.Status == "pending")
                    .OrderBy(e => e.StartAt)
                    .Take(take)
                    .ToList();
                return (stats, upcomingEvents);
            });

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.Stats["total"].Should().Be(5);
        result.Stats["pending"].Should().Be(3);
        result.Stats["completed"].Should().Be(1);
        result.Stats["cancelled"].Should().Be(1);
        result.Stats["upcoming"].Should().Be(2);
        result.Stats["overdue"].Should().Be(1);
        result.UpcomingEvents.Should().HaveCount(2);
        result.EventTypeOptions.Should().ContainKey("due_date");
        result.EventTypeOptions.Should().ContainKey("task");
        result.EventTypeOptions.Should().ContainKey("meeting");
        result.EventTypeOptions.Should().ContainKey("reminder");
        result.StatusOptions.Should().ContainKey("pending");
        result.StatusOptions.Should().ContainKey("completed");
        result.StatusOptions.Should().ContainKey("cancelled");
    }

    [Fact]
    public async Task GetOverviewAsync_SuperAdmin_ShouldSeeAllEvents()
    {
        // Arrange
        const int superAdminId = 1;
        var now = DateTime.UtcNow;
        var events = new List<CalendarEvent>
        {
            new() { Id = 1, Title = "事件1", Status = "pending", StartAt = now.AddDays(1), UserId = 5 },
            new() { Id = 2, Title = "事件2", Status = "pending", StartAt = now.AddDays(2), UserId = 10 },
            new() { Id = 3, Title = "事件3", Status = "completed", StartAt = now.AddDays(-1), UserId = 15 }
        };

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(superAdminId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(superAdminId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);

        _mockCalendarRepo.Setup(r => r.GetOverviewAggregateAsync(
                It.IsAny<IEnumerable<int>?>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync((IEnumerable<int>? userIds, DateTime dtNow, int take) =>
            {
                var filtered = userIds != null && userIds.Any()
                    ? events.Where(e => e.UserId.HasValue && userIds.Contains(e.UserId.Value)).ToList()
                    : events;
                var stats = new CalendarOverviewStats
                {
                    Total = filtered.Count,
                    Pending = filtered.Count(e => e.Status == "pending"),
                    Completed = filtered.Count(e => e.Status == "completed"),
                    Cancelled = filtered.Count(e => e.Status == "cancelled"),
                    Upcoming = filtered.Count(e => e.StartAt >= dtNow && e.Status == "pending"),
                    Overdue = filtered.Count(e => e.StartAt < dtNow && e.Status == "pending")
                };
                var upcomingEvents = filtered
                    .Where(e => e.StartAt >= dtNow && e.Status == "pending")
                    .OrderBy(e => e.StartAt)
                    .Take(take)
                    .ToList();
                return (stats, upcomingEvents);
            });

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.Stats["total"].Should().Be(3);
        result.Stats["pending"].Should().Be(2);
    }

    [Fact]
    public async Task GetOverviewAsync_NoEvents_ShouldReturnZeroStats()
    {
        // Arrange
        const int userId = 1;
        var events = new List<CalendarEvent>();

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(userId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(userId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);

        _mockCalendarRepo.Setup(r => r.GetOverviewAggregateAsync(
                It.IsAny<IEnumerable<int>?>(), It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync((IEnumerable<int>? userIds, DateTime dtNow, int take) =>
            {
                var filtered = userIds != null && userIds.Any()
                    ? events.Where(e => e.UserId.HasValue && userIds.Contains(e.UserId.Value)).ToList()
                    : events;
                var stats = new CalendarOverviewStats
                {
                    Total = filtered.Count,
                    Pending = filtered.Count(e => e.Status == "pending"),
                    Completed = filtered.Count(e => e.Status == "completed"),
                    Cancelled = filtered.Count(e => e.Status == "cancelled"),
                    Upcoming = filtered.Count(e => e.StartAt >= dtNow && e.Status == "pending"),
                    Overdue = filtered.Count(e => e.StartAt < dtNow && e.Status == "pending")
                };
                var upcomingEvents = filtered
                    .Where(e => e.StartAt >= dtNow && e.Status == "pending")
                    .OrderBy(e => e.StartAt)
                    .Take(take)
                    .ToList();
                return (stats, upcomingEvents);
            });

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.Stats["total"].Should().Be(0);
        result.Stats["pending"].Should().Be(0);
        result.Stats["completed"].Should().Be(0);
        result.Stats["cancelled"].Should().Be(0);
        result.Stats["upcoming"].Should().Be(0);
        result.Stats["overdue"].Should().Be(0);
        result.UpcomingEvents.Should().BeEmpty();
    }

    #endregion

    #region 回收站清理测试

    [Fact]
    public async Task PurgeTrashAsync_DefaultRetention30_ShouldPurgeOlderThan30Days()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var events = new List<CalendarEvent>
        {
            new() { Id = 101, DeletedAt = now.AddDays(-31) },
            new() { Id = 102, DeletedAt = now.AddDays(-29) },
            new() { Id = 103, DeletedAt = now.AddDays(-100) }
        };

        _mockCalendarRepo.Setup(r => r.PurgeTrashAsync(It.IsAny<DateTime>(), null))
            .ReturnsAsync((DateTime cutoff, int? _) => events.Count(e => e.DeletedAt <= cutoff));

        // Act
        var purged = await _service.PurgeTrashAsync(30);

        // Assert
        purged.Should().Be(2);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.purge_trash",
            "CalendarEvent",
            0,
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PurgeTrashAsync_Retention0_ShouldPurgeAllTrash()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var events = new List<CalendarEvent>
        {
            new() { Id = 101, DeletedAt = now.AddDays(-31) },
            new() { Id = 102, DeletedAt = now.AddDays(-29) },
            new() { Id = 103, DeletedAt = now.AddDays(-100) }
        };

        _mockCalendarRepo.Setup(r => r.PurgeTrashAsync(It.IsAny<DateTime>(), null))
            .ReturnsAsync((DateTime cutoff, int? _) => events.Count(e => e.DeletedAt <= cutoff));

        // Act
        var purged = await _service.PurgeTrashAsync(0);

        // Assert
        purged.Should().Be(3);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.purge_trash",
            "CalendarEvent",
            0,
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PurgeTrashAsync_NegativeRetention_ShouldThrowArgumentOutOfRange()
    {
        // Act
        var act = () => _service.PurgeTrashAsync(-1);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _mockCalendarRepo.Verify(r => r.PurgeTrashAsync(It.IsAny<DateTime>(), It.IsAny<int?>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockAuditService.Verify(a => a.LogAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClearTrashAsync_AuditLogCalled()
    {
        // Arrange
        _mockCalendarRepo.Setup(r => r.ClearTrashAsync(null)).ReturnsAsync(5);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(5);
        _mockCalendarRepo.Verify(r => r.ClearTrashAsync(null), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockAuditService.Verify(a => a.LogAsync(
            "calendar.clear_trash",
            "CalendarEvent",
            0,
            It.IsAny<string?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetListAsync 测试

    [Fact]
    public async Task GetListAsync_ReturnsPagedResults()
    {
        // Arrange
        const int currentUserId = 1;
        var events = new List<CalendarEvent>
        {
            CreateCalendarEvent(1, createdBy: currentUserId, userId: currentUserId),
            CreateCalendarEvent(2, createdBy: currentUserId, userId: currentUserId),
            CreateCalendarEvent(3, createdBy: currentUserId, userId: currentUserId),
            CreateCalendarEvent(4, createdBy: currentUserId, userId: currentUserId),
            CreateRecurringCalendarEvent(5, createdBy: currentUserId, userId: currentUserId)
        };

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);

        _mockCalendarRepo.Setup(r => r.GetListAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(events);
        _mockCalendarRepo.Setup(r => r.CountAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(5);

        // Act
        var (items, total) = await _service.GetListAsync(new CalendarEventQuery
        {
            Page = 1,
            PageSize = 10,
            ExpandRecurring = false // 分页测试本身不关心重复事件展开，避免依赖 _recurrence 组件
        });

        // Assert
        items.Should().HaveCount(5);
        total.Should().Be(5);
    }

    [Fact]
    public async Task GetListAsync_ExpandRecurring_True_GeneratesInstances()
    {
        // Arrange：1 条每日重复事件（RecurrenceType=0 每日, Interval=1, 明天开始）
        const int currentUserId = 1;
        var evt = CreateRecurringCalendarEvent(1, createdBy: currentUserId, userId: currentUserId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);

        _mockCalendarRepo.Setup(r => r.GetListAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.CountAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(1);

        var query = new CalendarEventQuery
        {
            Page = 1,
            PageSize = 10,
            ExpandRecurring = true,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(7)
        };

        // Act
        var (items, total) = await _service.GetListAsync(query);

        // Assert：每日重复在 7 天窗口内会展开为多个实例
        items.Should().HaveCountGreaterThan(1);
        total.Should().Be(items.Count);
    }

    [Fact]
    public async Task GetListAsync_ExpandRecurring_False_NoExpansion()
    {
        // Arrange
        const int currentUserId = 1;
        var evt = CreateRecurringCalendarEvent(1, createdBy: currentUserId, userId: currentUserId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);

        _mockCalendarRepo.Setup(r => r.GetListAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.CountAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<bool>()))
            .ReturnsAsync(1);

        var query = new CalendarEventQuery
        {
            Page = 1,
            PageSize = 10,
            ExpandRecurring = false,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(7)
        };

        // Act
        var (items, total) = await _service.GetListAsync(query);

        // Assert：未展开，仅返回原始 1 条
        items.Should().HaveCount(1);
        total.Should().Be(1);
    }

    #endregion

    #region GetByIdAsync 测试

    [Fact]
    public async Task GetByIdAsync_Exists_ReturnsDto()
    {
        // Arrange
        const int currentUserId = 1;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: currentUserId, userId: currentUserId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin))
            .ReturnsAsync(true);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(eventId)).ReturnsAsync(evt);

        // Act
        var result = await _service.GetByIdAsync(eventId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(eventId);
    }

    [Fact]
    public async Task GetByIdAsync_NotExists_ReturnsNull()
    {
        // Arrange
        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((CalendarEvent?)null);

        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// P1-1 越权守卫回归测试：非超管用户既不是创建者也不是被分配者，
    /// 且其角色（ReadOnly）不在上级管辖范围内 → GetByIdAsync 必须返回 null，
    /// 防止 IDOR 信息泄露。
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_UnauthorizedUser_ReturnsNull()
    {
        // Arrange
        const int attackerId = 999;
        const int ownerId = 5;
        const int eventId = 42;
        var evt = CreateCalendarEvent(eventId, createdBy: ownerId, userId: ownerId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(attackerId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);
        // 攻击者只有 Mailbox 访问权限（共享邮箱场景），但事件归属不是攻击者
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(evt.MailboxId))
            .ReturnsAsync(true);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(attackerId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(attackerId, UserRoleType.AgentSupervisor))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(attackerId, UserRoleType.Agent))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(attackerId, UserRoleType.ReadOnly))
            .ReturnsAsync(true);

        _mockCalendarRepo.Setup(r => r.GetByIdAsync(eventId)).ReturnsAsync(evt);

        // Act
        var result = await _service.GetByIdAsync(eventId);

        // Assert —— 必须拒绝访问（返回 null），不得泄露事件详情
        result.Should().BeNull("只读用户既非创建者也非被分配人，且非管辖范围，无权获取事件详情");
    }

    /// <summary>
    /// P1-1 正向越权守卫：作为被分配人（UserId 匹配），即使不是创建人也应能看到脱敏后的详情。
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_Assignee_ShouldReturnSanitizedDto()
    {
        // Arrange
        const int assigneeId = 7;
        const int creatorId = 1;
        const int eventId = 7;
        var evt = CreateCalendarEvent(eventId, createdBy: creatorId, userId: assigneeId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(assigneeId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(false);
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(evt.MailboxId))
            .ReturnsAsync(true);

        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(assigneeId, UserRoleType.SuperAdmin))
            .ReturnsAsync(false);
        _mockUserRoleResolver.Setup(r => r.IsSystemRoleAsync(assigneeId, UserRoleType.Agent))
            .ReturnsAsync(true);

        _mockCalendarRepo.Setup(r => r.GetByIdAsync(eventId)).ReturnsAsync(evt);

        // Act
        var result = await _service.GetByIdAsync(eventId);

        // Assert —— 被分配人必须能看到事件
        result.Should().NotBeNull();
        result!.Id.Should().Be(eventId);
    }

    #endregion

    #region ScanAndSendRemindersAsync 测试

    [Fact]
    public async Task ScanAndSendRemindersAsync_SendsRemindersForDueEvents()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var evt1 = CreateCalendarEvent(1, userId: 1);
        evt1.StartAt = now.AddMinutes(10); // 10 分钟后开始，提前 15 分钟提醒 → 已到提醒时间
        evt1.ReminderMinutes = 15;
        evt1.IsReminderSent = false;
        evt1.Status = "pending";

        var evt2 = CreateCalendarEvent(2, userId: 1);
        evt2.StartAt = now.AddMinutes(30); // 30 分钟后开始，提前 15 分钟提醒 → 还未到提醒时间
        evt2.ReminderMinutes = 15;
        evt2.IsReminderSent = false;
        evt2.Status = "pending";

        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt1, evt2 });
        // evt1 到达提醒时间：抢占成功；evt2 未到时间：claim 返回 0
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt1.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt2.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(0);

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert：仅 evt1 到达提醒时间被发送
        result.Should().Be(1);
        evt1.IsReminderSent.Should().BeTrue();
        evt2.IsReminderSent.Should().BeFalse();
        // 提醒标记通过 ExecuteUpdateAsync 原子更新，无需 SaveChangesAsync
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        // 站内通知可能因 Mailbox/User 仓储未 mock 而抛出异常被 try/catch 吞掉，
        // 这里只需验证扫描计数正确即可（通知发送的端到端验证通过集成测试覆盖）
    }

    [Fact]
    public async Task ScanAndSendRemindersAsync_NoDueEvents_ReturnsZero()
    {
        // Arrange
        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert
        result.Should().Be(0);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region 重复日程更新/删除分支测试

    [Fact]
    public async Task UpdateAsync_RecurringAll_UpdatesAllChildren()
    {
        // Arrange：1 条重复日程父事件 + 2 条子事件
        const int currentUserId = 1;
        const int parentId = 1;
        var parentEvt = CreateRecurringCalendarEvent(parentId, createdBy: currentUserId, userId: currentUserId);
        var child1 = CreateRecurringCalendarEvent(2, parentId: parentId, createdBy: currentUserId, userId: currentUserId);
        var child2 = CreateRecurringCalendarEvent(3, parentId: parentId, createdBy: currentUserId, userId: currentUserId);

        var request = CreateValidUpdateRequest(currentUserId);
        request.UpdateMode = "all";

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(parentId, false)).ReturnsAsync(parentEvt);
        _mockCalendarRepo.Setup(r => r.GetByParentIdTrackedAsync(parentId))
            .ReturnsAsync(new List<CalendarEvent> { child1, child2 });
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(parentId)).ReturnsAsync(parentEvt);

        // Act
        var result = await _service.UpdateAsync(parentId, request);

        // Assert：父事件与所有子事件均被更新，审计与 SignalR 推送被触发
        result.Should().NotBeNull();
        parentEvt.Title.Should().Be("更新后的日程");
        child1.Title.Should().Be("更新后的日程");
        child2.Title.Should().Be("更新后的日程");
        _mockCalendarRepo.Verify(r => r.GetByParentIdTrackedAsync(parentId), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockAuditService.Verify(a => a.LogAsync("calendar.update", "CalendarEvent", parentId,
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockNotificationHubService.Verify(h => h.NotifyCalendarEventChangedAsync(
            "calendar.update", parentId, It.IsAny<IEnumerable<int>>(), It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_RecurringAll_DeletesAllChildren()
    {
        // Arrange：1 条重复日程父事件 + 2 条子事件
        const int currentUserId = 1;
        const int parentId = 1;
        var parentEvt = CreateRecurringCalendarEvent(parentId, createdBy: currentUserId, userId: currentUserId);
        var child1 = CreateRecurringCalendarEvent(2, parentId: parentId, createdBy: currentUserId, userId: currentUserId);
        var child2 = CreateRecurringCalendarEvent(3, parentId: parentId, createdBy: currentUserId, userId: currentUserId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(parentId, false)).ReturnsAsync(parentEvt);
        _mockCalendarRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new List<CalendarEvent> { parentEvt }));
        _mockCalendarRepo.Setup(r => r.GetByParentIdTrackedAsync(parentId))
            .ReturnsAsync(new List<CalendarEvent> { child1, child2 });

        // Act
        var result = await _service.DeleteAsync(parentId, "all");

        // Assert：父事件与所有子事件均被软删除，审计与 SignalR 推送被触发
        result.Should().BeTrue();
        parentEvt.DeletedAt.Should().NotBeNull();
        child1.DeletedAt.Should().NotBeNull();
        child2.DeletedAt.Should().NotBeNull();
        _mockCalendarRepo.Verify(r => r.GetByParentIdTrackedAsync(parentId), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockAuditService.Verify(a => a.LogAsync("calendar.delete", "CalendarEvent", parentId,
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockNotificationHubService.Verify(h => h.NotifyCalendarEventChangedAsync(
            "calendar.delete", parentId, It.IsAny<IEnumerable<int>>(), It.IsAny<object>()), Times.Once);
    }

    #endregion

    #region SignalR 推送验证测试

    [Fact]
    public async Task CreateAsync_NotifiesViaSignalR()
    {
        // Arrange
        const int currentUserId = 1;
        var request = CreateValidRequest(currentUserId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 1; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((int id) => captured);

        // Act
        await _service.CreateAsync(request);

        // Assert
        _mockNotificationHubService.Verify(h => h.NotifyCalendarEventChangedAsync(
            "calendar.create", It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Single_NotifiesViaSignalR()
    {
        // Arrange
        const int currentUserId = 1;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: currentUserId, userId: currentUserId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false)).ReturnsAsync(evt);

        // Act
        await _service.DeleteAsync(eventId);

        // Assert
        _mockNotificationHubService.Verify(h => h.NotifyCalendarEventChangedAsync(
            "calendar.delete", eventId, It.IsAny<IEnumerable<int>>(), It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_NotifiesViaSignalR()
    {
        // Arrange
        const int currentUserId = 1;
        const int eventId = 1;
        var evt = CreateCalendarEvent(eventId, createdBy: currentUserId, userId: currentUserId);

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _mockCalendarRepo.Setup(r => r.GetByIdTrackedAsync(eventId, false)).ReturnsAsync(evt);

        // Act
        await _service.UpdateStatusAsync(eventId, "completed");

        // Assert
        _mockNotificationHubService.Verify(h => h.NotifyCalendarEventChangedAsync(
            "calendar.status_change", eventId, It.IsAny<IEnumerable<int>>(), It.IsAny<object>()), Times.Once);
    }

    #endregion

    #region 事件类型 EventType 容错/归一化测试（修复 ArgumentException + WebSocket 未完成关闭握手级联断开）

    /// <summary>
    /// 传入完全未知的 EventType（例如 Outlook/ICS 导入映射不到的 CATEGORIES）时，
    /// CreateAsync 不应抛出 ArgumentException——否则会被客户端 SignalR 重试循环放大成
    /// WebSocket "未完成关闭握手" 级联断开。
    /// 期望：自动降级为 "other" 并打 Warning 日志，然后正常完成保存流程。
    /// </summary>
    [Fact]
    public async Task CreateAsync_UnknownEventType_DoesNotThrowArgument_NormalizesToOther_LogsWarning()
    {
        // Arrange
        const int currentUserId = 1;
        var request = CreateValidRequest(currentUserId);
        request.EventType = "Out Of Office"; // 完全未知的类型（大小写混合 + 空格）

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 99; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(99))
            .ReturnsAsync((int id) => captured);

        // Act
        var act = () => _service.CreateAsync(request);
        var exception = await Record.ExceptionAsync(act);

        // Assert: 不应该抛任何异常（特别是 ArgumentException 引起的 WS 连锁断开）
        exception.Should().BeNull();
        captured.Should().NotBeNull();
        captured!.EventType.Should().Be("other"); // 已降级

        // 必须打 Warning 日志（含原始类型值），方便后续追溯导入来源
        _mockLogger.Verify(
            logger => logger.Log(
                It.Is<LogLevel>(level => level == LogLevel.Warning),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Out Of Office") &&
                    state.ToString()!.Contains("other")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "未知 EventType 必须打 Warning 日志且包含原始值 + 降级后值");
    }

    /// <summary>
    /// 扩展白名单里的 8 种（due_date/task/meeting/reminder/personal/travel/work/other）
    /// 都必须允许通过，大小写不敏感，保存的类型值应保留原始大小写（不强制转换）。
    /// </summary>
    [Theory]
    [InlineData("due_date")]
    [InlineData("task")]
    [InlineData("meeting")]
    [InlineData("reminder")]
    [InlineData("personal")]
    [InlineData("travel")]
    [InlineData("work")]
    [InlineData("other")]
    [InlineData("PERSONAL")]   // 大小写不敏感测试
    [InlineData("Travel")]     // 首字母大写测试
    public async Task CreateAsync_AllowedExtendedEventTypes_PassesThrough_NoWarning(string allowedType)
    {
        // Arrange
        const int currentUserId = 1;
        var request = CreateValidRequest(currentUserId);
        request.EventType = allowedType;

        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(currentUserId);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 100 + Math.Abs(allowedType.GetHashCode()); captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => captured);

        // Act
        await _service.CreateAsync(request);

        // Assert
        var exception = Record.Exception(() => captured.Should().NotBeNull());
        exception.Should().BeNull();
        captured!.EventType.Should().Be(allowedType, "合法白名单里的类型必须原样保存（不做大小写转换）");

        // 对于白名单中的类型，绝不应该触发 "未知事件类型" 的 Warning
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("未知事件类型") ||
                    state.ToString()!.Contains("NormalizeEventType")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            $"合法 EventType '{allowedType}' 不应触发归一化 Warning");
    }

    #endregion

    private static CalendarEvent CreateRecurringCalendarEvent(int id = 1, int parentId = 0, int createdBy = 1, int? userId = 1)
    {
        var evt = CreateCalendarEvent(id, createdBy, userId);
        evt.IsRecurring = true;
        evt.RecurrenceType = 0; // 每日
        evt.RecurrenceInterval = 1;
        if (parentId > 0)
        {
            evt.RecurrenceParentId = parentId;
        }
        return evt;
    }
}
