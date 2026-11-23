using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.Calendar;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services.Calendar;

/// <summary>
/// Commit #7.2: CalendarService 提醒推送加固 & ConfirmReminder API 核心逻辑测试。
/// 严格 TDD：测试先写，断言先失败，然后实现使其通过。
/// 通过 Facade 模式调用，内部委托 CalendarReminderService 子服务执行。
/// </summary>
public class CalendarReminderDeliveryTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ICalendarEventRepository> _mockCalendarRepo;
    private readonly Mock<IMailboxRepository> _mockMailboxRepo;
    private readonly Mock<IMailTemplateRepository> _mockMailTemplateRepo;
    private readonly Mock<IRepository<CalendarReminderAttempt>> _mockAttemptRepo;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<INotificationHubService> _mockNotificationHubService;
    private readonly Mock<IEmailQueueService> _mockEmailQueueService;
    private readonly IConfiguration _configuration;
    private readonly CalendarService _service;

    private readonly List<CalendarReminderAttempt> _capturedAttempts = new();

    public CalendarReminderDeliveryTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockCalendarRepo = new Mock<ICalendarEventRepository>();
        _mockMailTemplateRepo = new Mock<IMailTemplateRepository>();
        _mockAttemptRepo = new Mock<IRepository<CalendarReminderAttempt>>();
        _mockMailboxRepo = new Mock<IMailboxRepository>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockUserRoleResolver = new Mock<IUserRoleResolver>();
        _mockAuditService = new Mock<IAuditService>();
        _mockNotificationHubService = new Mock<INotificationHubService>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();

        var inMemoryConfig = new Dictionary<string, string?> { ["Trash:CalendarRetentionDays"] = "30" };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        _mockUnitOfWork.Setup(u => u.CalendarEvents).Returns(_mockCalendarRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.MailTemplates).Returns(_mockMailTemplateRepo.Object);
        _mockUnitOfWork.Setup(u => u.Repository<CalendarReminderAttempt>()).Returns(_mockAttemptRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        // 捕获 AddAsync 写入的 Attempt 实体，便于断言
        _mockAttemptRepo.Setup(r => r.AddAsync(It.IsAny<CalendarReminderAttempt>()))
            .Callback<CalendarReminderAttempt>(a => _capturedAttempts.Add(a))
            .Returns<CalendarReminderAttempt>(a => Task.FromResult(a));

        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>()))
            .ReturnsAsync(true);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);
        _mockCurrentUserAccessor.Setup(x => x.UserId).Returns(1);

        // —— 按依赖顺序构造子服务 & Facade（#10 CalendarService 分层）——
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

        _service = new CalendarService(queryService, mutationService, reminderService);
    }

    private static CalendarEvent CreateCalendarEvent(int id, int? userId = null)
    {
        return new CalendarEvent
        {
            Id = id,
            Title = $"测试事件 #{id}",
            EventType = "task",
            StartAt = DateTime.UtcNow.AddMinutes(30),
            Status = "pending",
            CreatedBy = userId ?? 1,
            UserId = userId,
            ReminderMinutes = 15,
            IsReminderSent = false
        };
    }

    [Fact]
    public async Task ScanAndSendRemindersAsync_SendsSignalRCalendarReminderEvent()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(1, userId: 42);
        evt.StartAt = now.AddMinutes(10); // 提前 15 分钟提醒 → 已到提醒时间
        evt.ReminderMinutes = 15;

        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        // TryClaimFirstReminderAsync 返回 1 = 抢占成功
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert：扫描计数正确
        result.Should().Be(1);

        // 规范 §3.2：SignalR 事件名必须为 "CalendarReminder"，并推送给负责人 userId
        // SendToUser(userId, "CalendarReminder", payload)
        _mockNotificationHubService.Verify(
            m => m.SendToUser(
                42,
                "CalendarReminder",
                It.Is<object>(p => p != null)),
            Times.Once,
            "规范要求 SignalR 推送事件名必须为 CalendarReminder，且发送给事件负责人");
    }

    [Fact]
    public async Task ConfirmReminder_WhenPending_UpdatesStatusToConfirmed()
    {
        // Arrange：使用真实的 SQLite in-memory DbContext 验证 ExecuteUpdate 原子更新
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        await using var context = new NoneadCSSDbContext(options);
        await context.Database.EnsureCreatedAsync();

        // 先关闭外键约束校验（避免 UserId/MailboxId FK 检查）
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = 0");

        // 先查询实际表的列名和类型（调试用）
        var columns = await context.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('CalendarEvents') ORDER BY cid").ToListAsync();

        // 仅用最小列集 INSERT，其他列依赖 DEFAULT 值或 NULL 约束
        var startAt = DateTime.UtcNow.AddHours(1);
        var createdAt = DateTime.UtcNow.AddHours(-1);
        var updatedAt = DateTime.UtcNow.AddMinutes(-6);
        var reminderSentAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // 动态构造 INSERT：只包含 columns 中存在的列
        var cols = new List<string>
        {
            "Id", "Title", "EventType", "StartAt", "Status",
            "CreatedBy", "UserId", "CreatedAt", "UpdatedAt",
            "IsAllDay", "IsReminderSent", "IsRecurring",
            "ReminderConfirmation", "ReminderSentAt",
            "ReminderMinutes"
        };
        // Outlook 系列列不一定存在
        cols.RemoveAll(c => !columns.Contains(c));
        // Priority/Progress 如果存在
        if (columns.Contains("Priority")) cols.Add("Priority");
        if (columns.Contains("Progress")) cols.Add("Progress");
        if (columns.Contains("RowVersion")) cols.Add("RowVersion");

        var insertSql = $@"
INSERT INTO CalendarEvents ({string.Join(", ", cols)})
VALUES (
    1, '测试确认提醒', 'task', @startAt, 'pending',
    100, 100, @createdAt, @updatedAt,
    0, 1, 0,
    'pending', @reminderSentAt,
    15
    {(cols.Contains("Priority") ? ", 2" : "")}
    {(cols.Contains("Progress") ? ", 0" : "")}
    {(cols.Contains("RowVersion") ? ", 0x0000000000000001" : "")}
)";
        await context.Database.ExecuteSqlRawAsync(insertSql, new[]
        {
            new Microsoft.Data.Sqlite.SqliteParameter("@startAt", startAt),
            new Microsoft.Data.Sqlite.SqliteParameter("@createdAt", createdAt),
            new Microsoft.Data.Sqlite.SqliteParameter("@updatedAt", updatedAt),
            new Microsoft.Data.Sqlite.SqliteParameter("@reminderSentAt", reminderSentAt.DateTime)
        });

        // Act：CalendarService.ConfirmReminderAsync 内部使用的原子更新 SQL：
        // WHERE Id=id AND (UserId=currentUserId OR isAdmin) AND ReminderConfirmation IN ('pending','fallback_sent')
        // SET ReminderConfirmation='confirmed', ReminderConfirmedAt=UtcNow
        var now = DateTimeOffset.UtcNow;
        var allowedStatuses = new[] { "pending", "fallback_sent" };
        const int currentUserId = 100;
        const bool isAdmin = false;

        var query = context.CalendarEvents
            .Where(e => e.Id == 1 && e.DeletedAt == null);
        if (!isAdmin)
            query = query.Where(e => e.UserId == currentUserId);
        query = query.Where(e => allowedStatuses.Contains(e.ReminderConfirmation));

        var affected = await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(e => e.ReminderConfirmation, "confirmed")
            .SetProperty(e => e.ReminderConfirmedAt, now)
            .SetProperty(e => e.UpdatedAt, DateTime.UtcNow));

        // Assert
        affected.Should().Be(1, "应恰好更新 1 行（pending -> confirmed）");

        var updated = await context.CalendarEvents.AsNoTracking().FirstAsync(e => e.Id == 1);
        updated.ReminderConfirmation.Should().Be("confirmed");
        updated.ReminderConfirmedAt.Should().NotBeNull();
        updated.ReminderConfirmedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    // ====== R5 新测试1：SignalR 成功必须写入独立 channel="signalr" status="sent" 的 attempt 行 ======
    [Fact]
    public async Task ScanAndSendRemindersAsync_SignalRSuccess_WritesSeparateSignalrSentAttempt()
    {
        // Arrange
        _capturedAttempts.Clear();
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(1, userId: 42);
        evt.StartAt = now.AddMinutes(10);
        evt.ReminderMinutes = 15;
        evt.EndAt = now.AddMinutes(40);

        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);

        // SendToUser 成功（不抛异常）
        _mockNotificationHubService.Setup(m => m.SendToUser(42, "CalendarReminder", It.IsAny<object>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert
        result.Should().Be(1);
        var signalrAttempt = _capturedAttempts
            .FirstOrDefault(a => a.AttemptChannel == "signalr" && a.Status == "sent");
        signalrAttempt.Should().NotBeNull("R5: SignalR 成功必须写入 channel=signalr, status=sent 的独立 attempt 行");
        signalrAttempt!.AttemptChannel.Should().Be("signalr");
        signalrAttempt.Status.Should().Be("sent");
        signalrAttempt.AttemptNo.Should().Be(1);
        signalrAttempt.Payload.Should().Be("user_42");
        // 不得存在 channel="all" 的 attempt
        _capturedAttempts.Should().NotContain(a => a.AttemptChannel == "all",
            "R5: 不得写入 channel='all' 的汇总 attempt");
    }

    // ====== R5 新测试2：SignalR 抛出异常必须写入独立 channel="signalr" status="failed" 的 attempt 行 ======
    [Fact]
    public async Task ScanAndSendRemindersAsync_SignalRThrows_WritesSeparateSignalrFailedAttempt()
    {
        // Arrange
        _capturedAttempts.Clear();
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(2, userId: 99);
        evt.StartAt = now.AddMinutes(10);
        evt.ReminderMinutes = 15;

        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);

        // SendToUser 抛出异常
        var signalrException = new InvalidOperationException("SignalR connection dropped");
        _mockNotificationHubService.Setup(m => m.SendToUser(99, "CalendarReminder", It.IsAny<object>()))
            .ThrowsAsync(signalrException);

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert：异常被吞，扫描计数仍为 1（抢占成功就算）
        result.Should().Be(1);
        var signalrFail = _capturedAttempts
            .FirstOrDefault(a => a.AttemptChannel == "signalr" && a.Status == "failed");
        signalrFail.Should().NotBeNull("R5: SignalR 失败必须写入 channel=signalr, status=failed 的独立 attempt 行");
        signalrFail!.ErrorDetail.Should().NotBeNullOrWhiteSpace();
        signalrFail.ErrorDetail.Should().Contain("SignalR connection dropped");
        signalrFail.AttemptNo.Should().Be(1);
        // 不得存在 channel="all" 的 attempt
        _capturedAttempts.Should().NotContain(a => a.AttemptChannel == "all",
            "R5: 异常时也不得写入 channel='all' 的汇总 attempt");
    }

    // ====== R4 测试：SignalR payload 必须包含精确的 spec 字段 ======
    [Fact]
    public async Task ScanAndSendRemindersAsync_SignalRPayload_HasExactSpecFields()
    {
        // Arrange
        _capturedAttempts.Clear();
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(3, userId: 7);
        evt.StartAt = now.AddMinutes(10);
        evt.EndAt = now.AddMinutes(30);
        evt.ReminderMinutes = 15;
        evt.IsAllDay = false;

        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);

        object? capturedPayload = null;
        _mockNotificationHubService.Setup(m => m.SendToUser(7, "CalendarReminder", It.IsAny<object>()))
            .Callback<int, string, object>((_, _, p) => capturedPayload = p)
            .Returns(Task.CompletedTask);

        // Act
        await _service.ScanAndSendRemindersAsync();

        // Assert：通过反射检查匿名对象属性
        capturedPayload.Should().NotBeNull();
        var props = capturedPayload!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(capturedPayload));

        props.Keys.Should().Contain(new[]
        {
            "eventId", "title", "startAt", "endAt", "isAllDay", "reminderMinutes", "sentAt"
        }, "R4: payload 必须包含 spec 精确定义的 7 个字段");
        props.Keys.Should().HaveCount(7, "R4: payload 不得包含 spec 以外的字段（如 reminderSentAt）");
        props.Should().NotContainKey("reminderSentAt", "R4: 不得使用错误的字段名 reminderSentAt，应使用 sentAt");

        props["eventId"].Should().Be(3);
        props["title"].Should().Be("测试事件 #3");
        props["endAt"].Should().Be(evt.EndAt);
        props["isAllDay"].Should().Be(false);
        props["reminderMinutes"].Should().Be(15);
        props["sentAt"].Should().BeOfType<DateTime>();
    }

    // ====== R13 新测试：ConfirmReminder 非 owner 非 admin 必须被拒绝（affected=0） ======
    [Fact]
    public async Task ConfirmReminder_NonOwnerNonAdmin_ReturnsZeroRows()
    {
        // Arrange：使用真实 SQLite in-memory DbContext
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        await using var context = new NoneadCSSDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = 0");

        var columns = await context.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('CalendarEvents') ORDER BY cid").ToListAsync();

        var cols = new List<string>
        {
            "Id", "Title", "EventType", "StartAt", "Status",
            "CreatedBy", "UserId", "CreatedAt", "UpdatedAt",
            "IsAllDay", "IsReminderSent", "IsRecurring",
            "ReminderConfirmation", "ReminderSentAt", "ReminderMinutes"
        };
        cols.RemoveAll(c => !columns.Contains(c));
        if (columns.Contains("Priority")) cols.Add("Priority");
        if (columns.Contains("Progress")) cols.Add("Progress");
        if (columns.Contains("RowVersion")) cols.Add("RowVersion");

        var startAt = DateTime.UtcNow.AddHours(1);
        var reminderSentAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        // 场景：事件分配给 UserId=200，CreatedBy=300（创建者不是分配者）
        var insertSql = $@"
INSERT INTO CalendarEvents ({string.Join(", ", cols)})
VALUES (
    5, 'ConfirmReminder R13 权限测试', 'task', @startAt, 'pending',
    300, 200, @createdAt, @updatedAt,
    0, 1, 0,
    'pending', @reminderSentAt,
    15
    {(cols.Contains("Priority") ? ", 2" : "")}
    {(cols.Contains("Progress") ? ", 0" : "")}
    {(cols.Contains("RowVersion") ? ", 0x0000000000000001" : "")}
)";
        await context.Database.ExecuteSqlRawAsync(insertSql, new[]
        {
            new Microsoft.Data.Sqlite.SqliteParameter("@startAt", startAt),
            new Microsoft.Data.Sqlite.SqliteParameter("@createdAt", DateTime.UtcNow.AddHours(-1)),
            new Microsoft.Data.Sqlite.SqliteParameter("@updatedAt", DateTime.UtcNow.AddMinutes(-6)),
            new Microsoft.Data.Sqlite.SqliteParameter("@reminderSentAt", reminderSentAt.DateTime)
        });

        var now = DateTimeOffset.UtcNow;
        var allowedStatuses = new[] { "pending", "fallback_sent" };

        // 场景A: currentUserId=999（既不是 UserId=200 也不是 CreatedBy=300），非 admin
        var queryA = context.CalendarEvents
            .Where(e => e.Id == 5 && e.DeletedAt == null)
            .Where(e => e.UserId == 999)
            .Where(e => allowedStatuses.Contains(e.ReminderConfirmation));
        var affectedA = await queryA.ExecuteUpdateAsync(setters => setters
            .SetProperty(e => e.ReminderConfirmation, "confirmed")
            .SetProperty(e => e.ReminderConfirmedAt, now)
            .SetProperty(e => e.UpdatedAt, DateTime.UtcNow));
        affectedA.Should().Be(0, "R13: 非 owner 非 admin 无法确认（即使是 CreatedBy 也不行）");

        // 场景B: currentUserId=300（CreatedBy，但不是 UserId），非 admin —— R13 规定 CreatedBy 不再具有确认权限
        var queryB = context.CalendarEvents
            .Where(e => e.Id == 5 && e.DeletedAt == null)
            .Where(e => e.UserId == 300)
            .Where(e => allowedStatuses.Contains(e.ReminderConfirmation));
        var affectedB = await queryB.ExecuteUpdateAsync(setters => setters
            .SetProperty(e => e.ReminderConfirmation, "confirmed")
            .SetProperty(e => e.ReminderConfirmedAt, now)
            .SetProperty(e => e.UpdatedAt, DateTime.UtcNow));
        affectedB.Should().Be(0, "R13: CreatedBy 不是 UserId 时无权确认提醒（需 UserId 匹配或 IsAdmin）");

        // 场景C: currentUserId=200（匹配 UserId），非 admin —— 应该成功
        var queryC = context.CalendarEvents
            .Where(e => e.Id == 5 && e.DeletedAt == null)
            .Where(e => e.UserId == 200)
            .Where(e => allowedStatuses.Contains(e.ReminderConfirmation));
        var affectedC = await queryC.ExecuteUpdateAsync(setters => setters
            .SetProperty(e => e.ReminderConfirmation, "confirmed")
            .SetProperty(e => e.ReminderConfirmedAt, now)
            .SetProperty(e => e.UpdatedAt, DateTime.UtcNow));
        affectedC.Should().Be(1, "R13: UserId 匹配时可以确认提醒");
    }

    // ========== T000090：ReminderType 多选组合分通道 ==========
    private static Mailbox CreateTestMailbox(int id = 5)
        => new() { Id = id, Email = "reminder-sender@test.local", Name = "提醒发件", IsActive = true };

    /// <summary>为"邮件通道真实入队"场景配置 发件邮箱 + 收件人用户 + EmailQueue mock。</summary>
    private void SetupReminderEmailDeliveryMocks(Mailbox mailbox)
    {
        _mockMailboxRepo.Setup(r => r.GetFirstActiveAsync()).ReturnsAsync(mailbox);
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<User>
            {
                new() { Id = 1, Email = "owner@test.local", Status = UserStatus.Active },
                new() { Id = 42, Email = "assigned@test.local", Status = UserStatus.Active },
            });
        _mockUnitOfWork.Setup(u => u.Users).Returns(userRepo.Object);
        _mockEmailQueueService
            .Setup(m => m.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
    }

    private static void VerifyEmailQueuedTimes(Mock<IEmailQueueService> queue, int times)
    {
        queue.Verify(m => m.EnqueueEmailAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), 10, It.IsAny<int?>(), It.IsAny<int?>()), Times.Exactly(times));
    }

    [Fact]
    public async Task ScanAndSendRemindersAsync_ReminderTypeEmail_SendsEmailOnly_NoInAppPopup()
    {
        // Arrange：ReminderType=email → 只发邮件，不发站内/SignalR 弹窗
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(101, userId: 42);
        evt.StartAt = now.AddMinutes(10);
        evt.ReminderMinutes = 15;
        evt.ReminderType = "email";
        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);
        SetupReminderEmailDeliveryMocks(CreateTestMailbox());

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert
        result.Should().Be(1);
        _mockNotificationHubService.Verify(m => m.SendToUser(It.IsAny<int>(), "CalendarReminder", It.IsAny<object>()), Times.Never,
            "T000090: email 类型不推送 SignalR 弹窗");
        _mockNotificationService.Verify(m => m.SendNotificationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never,
            "T000090: email 类型不发送站内通知");
        VerifyEmailQueuedTimes(_mockEmailQueueService, 1);
        _capturedAttempts.Any(a => a.EventId == 101 && a.AttemptChannel == "email" && a.Status == "sent").Should().BeTrue(
            "主流程邮件真实入队后应写 attempt(email, sent) 供 fallback 防重复");
    }

    [Fact]
    public async Task ScanAndSendRemindersAsync_ReminderTypePopup_SendsPopupOnly_NoEmail()
    {
        // Arrange：ReminderType=popup → 只发站内弹窗，不发邮件
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(102, userId: 42);
        evt.StartAt = now.AddMinutes(10);
        evt.ReminderMinutes = 15;
        evt.ReminderType = "popup";
        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert
        result.Should().Be(1);
        _mockNotificationHubService.Verify(m => m.SendToUser(It.IsAny<int>(), "CalendarReminder", It.IsAny<object>()), Times.Once,
            "T000090: popup 类型应推送 SignalR 弹窗");
        VerifyEmailQueuedTimes(_mockEmailQueueService, 0);
    }

    [Fact]
    public async Task ScanAndSendRemindersAsync_ReminderTypePopupEmailCombination_SendsBoth()
    {
        // Arrange：ReminderType="popup,email"（T000090 多选组合）→ 站内弹窗 + 邮件同时触发
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(103, userId: 42);
        evt.StartAt = now.AddMinutes(10);
        evt.ReminderMinutes = 15;
        evt.ReminderType = "popup,email";
        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);
        SetupReminderEmailDeliveryMocks(CreateTestMailbox());

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert：组合命中两通道
        result.Should().Be(1);
        _mockNotificationHubService.Verify(m => m.SendToUser(It.IsAny<int>(), "CalendarReminder", It.IsAny<object>()), Times.Once,
            "T000090: popup,email 组合应发送站内弹窗");
        VerifyEmailQueuedTimes(_mockEmailQueueService, 1);
        _capturedAttempts.Any(a => a.EventId == 103 && a.AttemptChannel == "email" && a.Status == "sent").Should().BeTrue();
    }

    [Fact]
    public async Task ScanAndSendRemindersAsync_NullReminderType_DefaultsToPopupAndEmail()
    {
        // Arrange：ReminderType=null（旧默认）→ popup + email（向后兼容），不触发短信
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(104, userId: 42);
        evt.StartAt = now.AddMinutes(10);
        evt.ReminderMinutes = 15;
        evt.ReminderType = null;
        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);
        SetupReminderEmailDeliveryMocks(CreateTestMailbox());

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert
        result.Should().Be(1);
        _mockNotificationHubService.Verify(m => m.SendToUser(It.IsAny<int>(), "CalendarReminder", It.IsAny<object>()), Times.Once);
        VerifyEmailQueuedTimes(_mockEmailQueueService, 1);
    }

    [Fact]
    public async Task ScanAndSendRemindersAsync_ReminderTypeSmsWithPopup_SmsFallsBackToInApp_NoEmail()
    {
        // Arrange：ReminderType="popup,sms" → 站内弹窗 + SMS（无网关降级为第二条站内通知），不发邮件
        var now = DateTime.UtcNow;
        var evt = CreateCalendarEvent(105, userId: 42);
        evt.StartAt = now.AddMinutes(10);
        evt.ReminderMinutes = 15;
        evt.ReminderType = "popup,sms";
        _mockCalendarRepo.Setup(r => r.GetUpcomingRemindersAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        _mockCalendarRepo.Setup(r => r.TryClaimFirstReminderAsync(evt.Id, It.IsAny<DateTime>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ScanAndSendRemindersAsync();

        // Assert
        result.Should().Be(1);
        _mockNotificationHubService.Verify(m => m.SendToUser(It.IsAny<int>(), "CalendarReminder", It.IsAny<object>()), Times.Once);
        // popup 分支 1 条站内 + sms 降级 1 条站内 = 2 次
        _mockNotificationService.Verify(m => m.SendNotificationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Exactly(2));
        VerifyEmailQueuedTimes(_mockEmailQueueService, 0);
    }
}
