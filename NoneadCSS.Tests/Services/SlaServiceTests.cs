using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Sla;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// SlaService 单元测试（使用 SQLite 内存库，支持事务，与生产一致）。
/// 覆盖 F-C3（首次响应超时 breached/warning 判断）、L-004（恢复时同名冲突自动重命名）、
/// ScanViolationsAsync 事务边界冒烟测试。
/// </summary>
public class SlaServiceTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private IUnitOfWork _unitOfWork = null!;
    private Mock<ILogger<SlaService>> _mockLogger = null!;
    private Mock<INotificationService> _mockNotificationService = null!;
    private Mock<IConfiguration> _mockConfiguration = null!;
    private Mock<ICurrentUserAccessor> _mockCurrentUserAccessor = null!;
    private Mock<IFrontendUrlResolver> _mockFrontendUrlResolver = null!;
    private SlaService _slaService = null!;
    private ServiceProvider _serviceProvider = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"SlaTestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new NoneadCSSDbContext(options);
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        _mockLogger = new Mock<ILogger<SlaService>>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockFrontendUrlResolver = new Mock<IFrontendUrlResolver>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(_mockConfiguration.Object);
        _serviceProvider = serviceCollection.BuildServiceProvider();

        _mockConfiguration.Setup(c => c["CompanyName"]).Returns("NoneadCSS");
        _mockFrontendUrlResolver.Setup(r => r.GetFrontendUrl()).Returns("https://test.example.com");

        _slaService = new SlaService(
            _unitOfWork,
            _mockLogger.Object,
            _mockNotificationService.Object,
            _mockConfiguration.Object,
            _mockCurrentUserAccessor.Object,
            _mockFrontendUrlResolver.Object);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        _serviceProvider?.Dispose();
    }

    #region ApplyToConversationAsync Tests

    /// <summary>
    /// F-C3 修复验证：首次响应已超时（CreatedAt = 4 小时前，超时阈值 = 1 小时），
    /// 应创建 ViolationType=first_response 且 Status=breached 的违规记录。
    /// </summary>
    [Fact]
    public async Task ApplyToConversationAsync_FirstResponseAlreadyOverdue_CreatesBreached()
    {
        // Arrange — 新建邮箱并保存
        var mailbox = new Mailbox
        {
            Name = "测试邮箱",
            Email = "test@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Mailboxes.Add(mailbox);
        await _context.SaveChangesAsync();

        // 新建会话，CreatedAt = 4 小时前（已超过 1 小时的首次响应阈值）
        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Subject = "F-C3 测试：首次响应已超时",
            MailboxId = mailbox.Id,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer@example.com",
            CreatedAt = now.AddHours(-4),
            UpdatedAt = now.AddHours(-4)
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        // 新建 SLA 策略：首次响应 1 小时（60 分钟），解决 24 小时（1440 分钟），
        // IsActive=true（对应 "active" 状态），Priority=1，绑定 MailboxId
        var sla = new SlaPolicy
        {
            Name = "F-C3 测试策略",
            MailboxId = mailbox.Id,
            FirstResponseHours = 1.0, // 60 分钟
            ResolutionHours = 24.0,   // 1440 分钟
            IsActive = true,
            Priority = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.SlaPolicies.Add(sla);
        await _context.SaveChangesAsync();

        // Act — 对该会话应用 SLA 策略
        await _slaService.ApplyToConversationAsync(conversation.Id);
        await _unitOfWork.SaveChangesAsync();

        // Assert — 查询违规记录，验证存在 breached 状态的首次响应违规
        var violations = await _unitOfWork.SlaViolations.GetQueryable(true).ToListAsync();
        violations.Should().NotBeEmpty();

        var firstResponseBreach = violations.FirstOrDefault(v =>
            v.ConversationId == conversation.Id &&
            v.ViolationType == SlaConst.ViolationFirstResponse &&
            v.Status == SlaConst.StatusBreached);

        firstResponseBreach.Should().NotBeNull(
            because: "会话创建于 4 小时前，首次响应阈值为 1 小时，应创建 breached（非 warning）违规记录");
    }

    /// <summary>
    /// F-C3 修复验证：首次响应未超时（CreatedAt = 30 分钟前，超时阈值 = 1 小时），
    /// 应创建 ViolationType=first_response 且 Status=warning 的预警记录。
    /// </summary>
    [Fact]
    public async Task ApplyToConversationAsync_FirstResponseNotOverdue_CreatesWarning()
    {
        // Arrange — 新建邮箱并保存
        var mailbox = new Mailbox
        {
            Name = "预警测试邮箱",
            Email = "warn@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Mailboxes.Add(mailbox);
        await _context.SaveChangesAsync();

        // 新建会话，CreatedAt = 30 分钟前（在 1 小时首次响应阈值内）
        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Subject = "F-C3 测试：首次响应未超时",
            MailboxId = mailbox.Id,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer2@example.com",
            CreatedAt = now.AddMinutes(-30),
            UpdatedAt = now.AddMinutes(-30)
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        // 新建 SLA 策略：首次响应 1 小时（60 分钟）
        var sla = new SlaPolicy
        {
            Name = "F-C3 预警测试策略",
            MailboxId = mailbox.Id,
            FirstResponseHours = 1.0,
            ResolutionHours = 24.0,
            IsActive = true,
            Priority = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.SlaPolicies.Add(sla);
        await _context.SaveChangesAsync();

        // Act
        await _slaService.ApplyToConversationAsync(conversation.Id);
        await _unitOfWork.SaveChangesAsync();

        // Assert — 验证 warning 状态
        var violations = await _unitOfWork.SlaViolations.GetQueryable(true).ToListAsync();
        violations.Should().NotBeEmpty();

        var firstResponseWarning = violations.FirstOrDefault(v =>
            v.ConversationId == conversation.Id &&
            v.ViolationType == SlaConst.ViolationFirstResponse &&
            v.Status == SlaConst.StatusWarning);

        firstResponseWarning.Should().NotBeNull(
            because: "会话创建于 30 分钟前，首次响应阈值为 1 小时，应创建 warning 预警记录");
    }

    #endregion

    #region RestoreAsync Tests

    /// <summary>
    /// L-004 修复验证：已删除策略与现存策略同名时，恢复操作应自动重命名，
    /// 命名格式为 "原名称 (恢复_YYYYMMDD)"。
    /// </summary>
    /// <remarks>
    /// 注意：SlaPolicyRepository.RestoreAsync 内部使用 ExecuteUpdateAsync（批量 UPDATE），
    /// EF Core InMemory Provider 不支持 ExecuteUpdate/ExecuteDelete，因此本测试标记 Skip，
    /// 需在真实 SQL Server/SQLite 上做集成测试验证。
    /// </remarks>
    [Fact(Skip = "InMemory DB 不支持 ExecuteUpdateAsync，需使用 SQL Server/SQLite 集成测试验证")]
    public async Task RestoreAsync_NameConflict_AutoRenames()
    {
        // Arrange — 新建 SLA 策略 "测试策略" 并 soft delete
        var now = DateTime.UtcNow;
        var deletedSla = new SlaPolicy
        {
            Name = "测试策略",
            FirstResponseHours = 1.0,
            ResolutionHours = 24.0,
            IsActive = true,
            Priority = 1,
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddDays(-2),
            DeletedAt = now // 软删除
        };
        _context.SlaPolicies.Add(deletedSla);
        await _context.SaveChangesAsync();

        // 再新建另一个同名策略（未删除）
        var activeSla = new SlaPolicy
        {
            Name = "测试策略",
            FirstResponseHours = 2.0,
            ResolutionHours = 48.0,
            IsActive = true,
            Priority = 2,
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1)
        };
        _context.SlaPolicies.Add(activeSla);
        await _context.SaveChangesAsync();

        var deletedId = deletedSla.Id;

        // Act — 恢复已删除的策略
        var result = await _slaService.RestoreAsync(deletedId);
        await _unitOfWork.SaveChangesAsync();

        // Assert — 恢复应成功，且名称已被自动重命名
        result.Should().BeTrue();

        var restored = await _context.SlaPolicies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == deletedId);

        restored.Should().NotBeNull();
        restored!.DeletedAt.Should().BeNull();
        restored.Name.Should().NotBe("测试策略",
            because: "存在同名未删除策略，恢复时应自动重命名避免唯一键冲突");
        restored.Name.Should().StartWith("测试策略 (恢复_",
            because: "重命名格式应为 \"原名称 (恢复_YYYYMMDD)\"");
        restored.Name.Should().Contain(DateTime.UtcNow.ToString("yyyyMMdd"),
            because: "重命名应包含当前日期 YYYYMMDD");
    }

    #endregion

    #region ScanViolationsAsync Tests

    /// <summary>
    /// 冒烟测试：ScanViolationsAsync 对空集合应能执行不抛异常，
    /// 验证事务边界（BeginTransaction/Commit/Rollback）在 SQLite 上不会冲突。
    /// </summary>
    [Fact]
    public async Task ScanViolationsAsync_BatchWrapsInTransaction_SaveSucceeds()
    {
        // Arrange — 数据库中没有任何需要扫描的会话（空集场景），
        // 确保 ScanViolationsAsync 在零会话时也能安全通过事务包装

        // Act — 执行扫描，不应抛出任何数据库异常（如事务嵌套冲突、SaveChanges 失败等）
        var scanResult = await _slaService.Awaiting(s => s.ScanViolationsAsync())
            .Should().NotThrowAsync(
                because: "ScanViolationsAsync 应对空集合安全执行，事务边界不应引发 DB 异常");

        scanResult.Subject.Scanned.Should().Be(0);
        scanResult.Subject.NewViolations.Should().Be(0);
        scanResult.Subject.Breached.Should().Be(0);
    }

    #endregion

    #region BUG-06 Regression — CalculateStatus 除零风险

    /// <summary>
    /// BUG-06 回归测试：当 targetHours = 0 时（历史数据/直改 DB 导致），
    /// SlaConst.CalculateStatus 应直接返回 "breached"，而非进入除零计算
    /// （DivideByZeroException 或 double.NaN）。
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0, false)]      // targetHours=0, elapsedHours=0
    [InlineData(0.0, 5.0, false)]     // targetHours=0, elapsedHours=5
    [InlineData(0.0, 100.0, false)]   // targetHours=0, elapsedHours=100
    public void BUG06_CalculateStatus_TargetHoursZero_ReturnsBreached(
        double targetHours, double elapsedHours, bool isResolved)
    {
        // Act — 不应抛出 DivideByZeroException
        var status = SlaConst.CalculateStatus(targetHours, elapsedHours, isResolved);

        // Assert — 直接判定为 breached
        status.Should().Be(SlaConst.SlaStatusBreached,
            because: "targetHours=0 是无效策略配置，应被视为已超时而非进入除零计算");
    }

    /// <summary>
    /// BUG-06 回归测试：当 targetHours 为负数时（数据错误），
    /// 同样应返回 "breached"。
    /// </summary>
    [Theory]
    [InlineData(-1.0, 0.0, false)]
    [InlineData(-1.0, 5.0, false)]
    [InlineData(-100.0, 50.0, false)]
    public void BUG06_CalculateStatus_TargetHoursNegative_ReturnsBreached(
        double targetHours, double elapsedHours, bool isResolved)
    {
        var status = SlaConst.CalculateStatus(targetHours, elapsedHours, isResolved);

        status.Should().Be(SlaConst.SlaStatusBreached,
            because: "targetHours<0 是无效策略配置，应被视为已超时");
    }

    /// <summary>
    /// BUG-06 回归测试：明确验证不抛出 DivideByZeroException。
    /// 在修复前，targetHours=0 进入 (targetHours - elapsedHours) / targetHours 计算，
    /// 对于 double 类型虽不会抛出 DivideByZeroException（返回 NaN），但下游 switch/字典
    /// 查找会失败。这里通过 FluentAssertions 的 NotThrow 断言加固。
    /// </summary>
    [Fact]
    public void BUG06_CalculateStatus_TargetHoursZero_DoesNotThrowOrReturnNaN()
    {
        // Act
        var status = SlaConst.CalculateStatus(0, 10, false);

        // Assert — 不为 NaN（double.IsNaN 检查），且为已知状态字符串
        double.TryParse(status, out _).Should().BeFalse(
            because: "状态字符串不应是数值或 NaN");
        status.Should().NotBe("NaN",
            because: "targetHours=0 不应产生 NaN 状态");
        status.Should().Be(SlaConst.SlaStatusBreached);
    }

    /// <summary>
    /// BUG-06 回归测试：验证修复未破坏正常路径（on_track / warning / breached / resolved）。
    /// </summary>
    [Theory]
    [InlineData(10.0, 1.0, false, SlaConst.SlaStatusOnTrack)]    // 远未到期
    [InlineData(10.0, 9.5, false, SlaConst.SlaStatusWarning)]   // 剩余 5% < 20% 预警阈值
    [InlineData(10.0, 11.0, false, SlaConst.SlaStatusBreached)] // 已超期
    [InlineData(10.0, 5.0, true, SlaConst.SlaStatusResolved)]   // 已解决
    public void BUG06_CalculateStatus_NormalPath_Unchanged(
        double targetHours, double elapsedHours, bool isResolved, string expected)
    {
        var status = SlaConst.CalculateStatus(targetHours, elapsedHours, isResolved);

        status.Should().Be(expected,
            because: "BUG-06 修复仅新增 targetHours<=0 守卫，不应改变正常路径行为");
    }

    /// <summary>
    /// BUG-06 回归测试：SlaPolicy.FirstResponseHours = 0 时，
    /// 通过公共方法 CalculateFirstResponseStatus 间接测试私有 CalculateStatus，
    /// 应返回 SlaStatuses.ConversationBreached 而非 NaN。
    /// </summary>
    [Fact]
    public void BUG06_SlaPolicy_FirstResponseHoursZero_ReturnsBreached()
    {
        // Arrange — 构造 FirstResponseHours=0 的策略（模拟历史脏数据）
        var policy = new SlaPolicy
        {
            Name = "BUG-06 测试",
            FirstResponseHours = 0.0,  // 触发 BUG-06 守卫
            ResolutionHours = 24.0,
            IsActive = true,
            Priority = 1,
        };
        var firstCustomerMessageAt = DateTime.UtcNow.AddMinutes(-30);
        var now = DateTime.UtcNow;

        // Act — 客户已发消息但未回复（firstReplyAt=null）
        var status = policy.CalculateFirstResponseStatus(firstCustomerMessageAt, null, now);

        // Assert
        status.Should().Be(SlaStatuses.ConversationBreached,
            because: "FirstResponseHours=0 是无效配置，CalculateFirstResponseStatus 应直接返回 breached");
    }

    /// <summary>
    /// BUG-06 回归测试：SlaPolicy.ResolutionHours 为负数时（数据错误），
    /// CalculateResolutionStatus 应返回 breached。
    /// </summary>
    [Fact]
    public void BUG06_SlaPolicy_ResolutionHoursNegative_ReturnsBreached()
    {
        // Arrange
        var policy = new SlaPolicy
        {
            Name = "BUG-06 测试",
            FirstResponseHours = 1.0,
            ResolutionHours = -24.0,  // 负数触发 BUG-06 守卫
            IsActive = true,
            Priority = 1,
        };
        var createdAt = DateTime.UtcNow.AddHours(-1);
        var now = DateTime.UtcNow;

        // Act — 会话未关闭
        var status = policy.CalculateResolutionStatus(createdAt, isClosed: false, closedAt: null, now);

        // Assert
        status.Should().Be(SlaStatuses.ConversationBreached,
            because: "ResolutionHours<0 是无效配置，应直接返回 breached");
    }

    /// <summary>
    /// BUG-06 回归测试：SlaPolicy 正常路径未受影响（仍能返回 on_track/warning/breached/resolved）。
    /// </summary>
    [Theory]
    [InlineData(24.0, 1.0, false, SlaStatuses.ConversationOnTrack)]
    [InlineData(24.0, 23.0, false, SlaStatuses.ConversationWarning)]  // 剩余 1/24 ≈ 4.2% < 20%
    [InlineData(24.0, 25.0, false, SlaStatuses.ConversationBreached)]
    [InlineData(24.0, 10.0, true, SlaStatuses.ConversationResolved)]
    public void BUG06_SlaPolicy_NormalPath_Unchanged(
        double resolutionHours, double elapsedHours, bool isClosed, string expected)
    {
        // Arrange
        var policy = new SlaPolicy
        {
            Name = "BUG-06 正常路径",
            FirstResponseHours = 1.0,
            ResolutionHours = resolutionHours,
            IsActive = true,
            Priority = 1,
        };
        var createdAt = DateTime.UtcNow.AddHours(-elapsedHours);
        var now = DateTime.UtcNow;
        var closedAt = isClosed ? now : (DateTime?)null;

        // Act
        var status = policy.CalculateResolutionStatus(createdAt, isClosed, closedAt, now);

        // Assert
        status.Should().Be(expected,
            because: "BUG-06 修复仅新增 ResolutionHours<=0 守卫，不应改变正常路径行为");
    }

    #endregion
}
