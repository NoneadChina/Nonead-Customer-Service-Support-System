using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;
using NoneadCSS.Infrastructure.Services;
using NoneadCSS.Tests.Shared;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// EmailQueueService 单元测试（使用真实 InMemory EF Core DbContext + Mock IEmailService）。
/// 覆盖 BUG-07 修复：
/// - GetPendingAsync 同时拾起 Accepted 与 SendIntermediateError（孤立中间状态恢复）
/// - AttemptCount &lt; MaxRetryCount 过滤（防止无限重试）
/// - 失败时 AttemptCount &lt; Max → 重置为 Accepted 等待重试
/// - 失败时 AttemptCount &gt;= Max → 固化为 SendError（终态）
/// - 成功路径回归（DeliverySuccess）
/// </summary>
public class EmailQueueServiceTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private IUnitOfWork _unitOfWork = null!;
    private Mock<ILogger<EmailQueueService>> _mockLogger = null!;
    private Mock<IEmailService> _mockEmailService = null!;
    private Mock<IAttachmentService> _mockAttachmentService = null!;
    private EmailQueueService _service = null!;
    private ServiceProvider _serviceProvider = null!;

    public Task InitializeAsync()
    {
        // BUG-07 测试约束：EmailQueueService 在事务内执行状态流转（BeginTransactionAsync + Commit/Rollback）。
        // EF Core InMemory provider 不支持真实事务，会抛出 InMemoryEventId.TransactionIgnoredWarning。
        // 这里通过 ConfigureWarnings(Throw(InMemoryEventId.TransactionIgnoredWarning) → Ignore) 抑制该警告，
        // 使 BeginTransactionAsync 成为 no-op，测试可继续验证状态机逻辑（已多次确认事务行为
        // 在 SQLite/SQL Server 生产环境正常工作，InMemory 仅用于隔离测试业务逻辑）。
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"EmailQueueTestDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new NoneadCSSDbContext(options);
        // Infrastructure 的真实 UnitOfWork（构造函数需要 context + 仓储 ServiceProvider）；
        // 2026-11-21 测试卫生清理后 TestHelpers.cs 已无同名影子类，这里保留完全限定名只为可读性
        _unitOfWork = new NoneadCSS.Infrastructure.UnitOfWork(_context, TestServiceProviderBuilder.Build(_context));

        _mockLogger = new Mock<ILogger<EmailQueueService>>();
        _mockEmailService = new Mock<IEmailService>();
        _mockAttachmentService = new Mock<IAttachmentService>();
        // 注：EmailQueueService 构造函数注入 IAttachmentService，但当前实现未调用其方法
        // （附件查询通过 _unitOfWork.Attachments.GetByIdsAsync 走仓储路径），故此处无需设置 mock

        _serviceProvider = new ServiceCollection().BuildServiceProvider();

        _service = new EmailQueueService(
            _mockLogger.Object,
            _unitOfWork,
            _mockEmailService.Object,
            _mockAttachmentService.Object);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        _serviceProvider?.Dispose();
    }

    /// <summary>
    /// 创建测试用邮箱并保存到 InMemory DB。
    /// </summary>
    private async Task<Mailbox> CreateMailboxAsync()
    {
        var mailbox = new Mailbox
        {
            Name = "测试邮箱",
            Email = "test@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Mailboxes.Add(mailbox);
        await _context.SaveChangesAsync();
        return mailbox;
    }

    /// <summary>
    /// 创建测试用 SendLog 记录并保存到 InMemory DB。
    /// </summary>
    private async Task<SendLog> CreateSendLogAsync(int mailboxId, int status, int attemptCount = 0)
    {
        var log = new SendLog
        {
            MailboxId = mailboxId,
            Email = "recipient@example.com",
            Subject = "测试邮件",
            Body = "<p>测试内容</p>",
            Status = status,
            AttemptCount = attemptCount,
            CreatedAt = DateTime.UtcNow,
            MailType = SendLogMailType.EmailToCustomer,
        };
        _context.SendLogs.Add(log);
        await _context.SaveChangesAsync();
        return log;
    }

    /// <summary>
    /// 重新获取 tracked SendLog 实体（用于断言状态变更）。
    /// </summary>
    private async Task<SendLog> ReloadSendLogAsync(int id)
    {
        var log = await _unitOfWork.SendLogs.GetQueryable(true)
            .FirstOrDefaultAsync(s => s.Id == id);
        log.Should().NotBeNull("测试断言前 SendLog 必须存在");
        return log!;
    }

    #region GetPendingAsync — 队列拾取逻辑

    /// <summary>
    /// BUG-07 修复点 1：GetPendingAsync 应同时拾起 Accepted(1) 与 SendIntermediateError(10)。
    /// 验证 SendIntermediateError 状态的记录（模拟进程崩溃遗留的孤立中间态）能被队列重新拾起。
    /// </summary>
    [Fact]
    public async Task BUG07_GetPendingAsync_PicksUpAcceptedAndIntermediateError()
    {
        // Arrange
        var mailbox = await CreateMailboxAsync();
        await CreateSendLogAsync(mailbox.Id, SendLogStatus.Accepted, attemptCount: 0);
        await CreateSendLogAsync(mailbox.Id, SendLogStatus.SendIntermediateError, attemptCount: 1);

        // Act
        var pending = await _unitOfWork.SendLogs.GetPendingAsync();

        // Assert — 两条记录都应被拾起
        pending.Should().HaveCount(2,
            because: "GetPendingAsync 必须同时拾起 Accepted 与 SendIntermediateError 才能恢复崩溃遗留的中间态");
        pending.Should().Contain(s => s.Status == SendLogStatus.Accepted);
        pending.Should().Contain(s => s.Status == SendLogStatus.SendIntermediateError);
    }

    /// <summary>
    /// BUG-07 修复点 2：AttemptCount >= MaxRetryCount 的记录不应被队列拾起，
    /// 避免无限重试占用资源（状态应已固化为 SendError，由 ProcessQueueItemAsync 在失败时写入）。
    /// </summary>
    [Fact]
    public async Task BUG07_GetPendingAsync_SkipsRecordsReachingMaxRetryCount()
    {
        // Arrange
        var mailbox = await CreateMailboxAsync();
        // Accepted 但 AttemptCount 已达上限 → 不应被拾起
        await CreateSendLogAsync(mailbox.Id, SendLogStatus.Accepted, attemptCount: SendLogStatus.MaxRetryCount);
        // SendIntermediateError 但 AttemptCount 已达上限 → 不应被拾起
        await CreateSendLogAsync(mailbox.Id, SendLogStatus.SendIntermediateError, attemptCount: SendLogStatus.MaxRetryCount);
        // Accepted 且 AttemptCount < 上限 → 应被拾起
        await CreateSendLogAsync(mailbox.Id, SendLogStatus.Accepted, attemptCount: SendLogStatus.MaxRetryCount - 1);

        // Act
        var pending = await _unitOfWork.SendLogs.GetPendingAsync();

        // Assert — 仅 1 条记录被拾起
        pending.Should().HaveCount(1,
            because: "AttemptCount >= MaxRetryCount 的记录应被过滤掉，避免无限重试");
        pending.First().AttemptCount.Should().BeLessThan(SendLogStatus.MaxRetryCount);
    }

    /// <summary>
    /// BUG-07 回归测试：终态记录（SendError / DeliverySuccess）绝不被队列拾起。
    /// </summary>
    [Fact]
    public async Task BUG07_GetPendingAsync_NeverPicksTerminalStates()
    {
        // Arrange
        var mailbox = await CreateMailboxAsync();
        await CreateSendLogAsync(mailbox.Id, SendLogStatus.SendError, attemptCount: 0);
        await CreateSendLogAsync(mailbox.Id, SendLogStatus.DeliverySuccess, attemptCount: 0);
        await CreateSendLogAsync(mailbox.Id, SendLogStatus.DeliveryError, attemptCount: 0);

        // Act
        var pending = await _unitOfWork.SendLogs.GetPendingAsync();

        // Assert
        pending.Should().BeEmpty(
            because: "终态记录（SendError / DeliverySuccess / DeliveryError）不应被队列重新拾起");
    }

    #endregion

    #region ProcessQueueAsync — 重试机制

    /// <summary>
    /// BUG-07 修复点 3：发送失败且 AttemptCount &lt; MaxRetryCount 时，
    /// 状态应重置为 Accepted，等待下一轮队列处理重试。
    /// </summary>
    [Fact]
    public async Task BUG07_ProcessQueueAsync_OnFailure_ResetsToAcceptedWhenBelowMaxRetry()
    {
        // Arrange
        var mailbox = await CreateMailboxAsync();
        var log = await CreateSendLogAsync(mailbox.Id, SendLogStatus.Accepted, attemptCount: 0);

        // Mock IEmailService.SendEmailWithHeadersAsync 抛异常模拟发送失败
        _mockEmailService.Setup(e => e.SendEmailWithHeadersAsync(
                It.IsAny<Mailbox>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<Attachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP 连接失败"));

        // Act
        await _service.ProcessQueueAsync();

        // Assert
        var reloaded = await ReloadSendLogAsync(log.Id);
        reloaded.Status.Should().Be(SendLogStatus.Accepted,
            because: "AttemptCount(1) < MaxRetryCount(3)，失败后应重置为 Accepted 等待重试，而非固化为 SendError");
        reloaded.AttemptCount.Should().Be(1,
            because: "失败后 AttemptCount 应递增（事务内 +1 已被保留）");
        reloaded.ErrorMessage.Should().NotBeNullOrEmpty(
            because: "失败原因应被记录以供排查");
    }

    /// <summary>
    /// BUG-07 修复点 4：发送失败且 AttemptCount &gt;= MaxRetryCount 时，
    /// 状态应固化为 SendError（终态），不再被队列拾起。
    /// </summary>
    [Fact]
    public async Task BUG07_ProcessQueueAsync_OnFailure_TerminalSendErrorWhenMaxRetryReached()
    {
        // Arrange
        var mailbox = await CreateMailboxAsync();
        // AttemptCount = MaxRetryCount - 1，本次失败后 AttemptCount 变为 MaxRetryCount，
        // 触发固化为 SendError
        var log = await CreateSendLogAsync(mailbox.Id, SendLogStatus.Accepted, attemptCount: SendLogStatus.MaxRetryCount - 1);

        _mockEmailService.Setup(e => e.SendEmailWithHeadersAsync(
                It.IsAny<Mailbox>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<Attachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP 连接失败"));

        // Act
        await _service.ProcessQueueAsync();

        // Assert
        var reloaded = await ReloadSendLogAsync(log.Id);
        reloaded.Status.Should().Be(SendLogStatus.SendError,
            because: "AttemptCount 达到 MaxRetryCount(3) 后应固化为终态 SendError，不再被队列拾起");
        reloaded.AttemptCount.Should().Be(SendLogStatus.MaxRetryCount,
            because: "达到上限后 AttemptCount 应等于 MaxRetryCount");
        reloaded.ErrorMessage.Should().NotBeNullOrEmpty();

        // 二次调用队列，该记录不应再被拾起
        var pendingAfter = await _unitOfWork.SendLogs.GetPendingAsync();
        pendingAfter.Should().NotContain(s => s.Id == log.Id,
            because: "终态 SendError 记录不应被队列再次拾起");
    }

    /// <summary>
    /// BUG-07 回归测试：成功路径未受影响，状态应为 DeliverySuccess，AttemptCount 递增。
    /// </summary>
    [Fact]
    public async Task BUG07_ProcessQueueAsync_OnSuccess_SetsDeliverySuccess()
    {
        // Arrange
        var mailbox = await CreateMailboxAsync();
        var log = await CreateSendLogAsync(mailbox.Id, SendLogStatus.Accepted, attemptCount: 0);

        _mockEmailService.Setup(e => e.SendEmailWithHeadersAsync(
                It.IsAny<Mailbox>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<Attachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockEmailService.Setup(e => e.SaveToSentFolderAsync(
                It.IsAny<Mailbox>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<Attachment>?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.ProcessQueueAsync();

        // Assert
        var reloaded = await ReloadSendLogAsync(log.Id);
        reloaded.Status.Should().Be(SendLogStatus.DeliverySuccess,
            because: "发送成功后状态应为 DeliverySuccess");
        reloaded.AttemptCount.Should().Be(1,
            because: "事务内 AttemptCount++ 应被保留");
        reloaded.SentAt.Should().NotBeNull();
        reloaded.MessageId.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// BUG-07 完整重试循环：Accepted(0) → 失败 → Accepted(1) → 失败 → Accepted(2) → 失败 → SendError(3)。
    /// 验证多次 ProcessQueueAsync 调用后状态机完整推进。
    /// </summary>
    [Fact]
    public async Task BUG07_ProcessQueueAsync_RetryLoopProgression()
    {
        // Arrange
        var mailbox = await CreateMailboxAsync();
        var log = await CreateSendLogAsync(mailbox.Id, SendLogStatus.Accepted, attemptCount: 0);

        _mockEmailService.Setup(e => e.SendEmailWithHeadersAsync(
                It.IsAny<Mailbox>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<Attachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP 连接失败"));

        // Act & Assert — 第一轮失败：AttemptCount 0 → 1，状态 Accepted
        await _service.ProcessQueueAsync();
        var after1 = await ReloadSendLogAsync(log.Id);
        after1.Status.Should().Be(SendLogStatus.Accepted);
        after1.AttemptCount.Should().Be(1);

        // 第二轮失败：AttemptCount 1 → 2，状态 Accepted
        await _service.ProcessQueueAsync();
        var after2 = await ReloadSendLogAsync(log.Id);
        after2.Status.Should().Be(SendLogStatus.Accepted);
        after2.AttemptCount.Should().Be(2);

        // 第三轮失败：AttemptCount 2 → 3，达到 MaxRetryCount，固化为 SendError
        await _service.ProcessQueueAsync();
        var after3 = await ReloadSendLogAsync(log.Id);
        after3.Status.Should().Be(SendLogStatus.SendError,
            because: "达到 MaxRetryCount 后应固化为终态 SendError");
        after3.AttemptCount.Should().Be(SendLogStatus.MaxRetryCount);

        // 第四轮调用队列，不应再处理该记录
        var pendingAfter = await _unitOfWork.SendLogs.GetPendingAsync();
        pendingAfter.Should().NotContain(s => s.Id == log.Id);
    }

    #endregion

    #region 孤立中间状态恢复

    /// <summary>
    /// BUG-07 修复点 1 端到端验证：进程崩溃遗留的 SendIntermediateError 记录，
    /// 在重启队列后应能被重新拾起并完成发送（验证"孤立中间状态"恢复路径）。
    /// </summary>
    [Fact]
    public async Task BUG07_ProcessQueueAsync_RecoveryFromOrphanedIntermediateError()
    {
        // Arrange — 模拟进程崩溃后的孤立中间态
        var mailbox = await CreateMailboxAsync();
        var log = await CreateSendLogAsync(
            mailbox.Id,
            SendLogStatus.SendIntermediateError,
            attemptCount: 1);  // 假设上次崩溃在第一次尝试时发生

        // 本轮发送成功（模拟 SMTP 恢复正常）
        _mockEmailService.Setup(e => e.SendEmailWithHeadersAsync(
                It.IsAny<Mailbox>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<Attachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockEmailService.Setup(e => e.SaveToSentFolderAsync(
                It.IsAny<Mailbox>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<Attachment>?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        // Act — 重启队列处理
        await _service.ProcessQueueAsync();

        // Assert — 孤立记录应被成功恢复并标记为已发送
        var reloaded = await ReloadSendLogAsync(log.Id);
        reloaded.Status.Should().Be(SendLogStatus.DeliverySuccess,
            because: "SendIntermediateError 状态的孤立记录应被队列重新拾起并完成发送");
        reloaded.AttemptCount.Should().Be(2,
            because: "上次崩溃时 AttemptCount=1，本轮重试 +1 = 2");
        reloaded.SentAt.Should().NotBeNull();
        reloaded.MessageId.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// BUG-07 孤立中间状态恢复失败路径：恢复时再次失败，
    /// 若仍 < MaxRetryCount 应重置为 Accepted 等待下一次重试。
    /// </summary>
    [Fact]
    public async Task BUG07_ProcessQueueAsync_OrphanedIntermediateErrorFailsAgain_ResetsToAccepted()
    {
        // Arrange
        var mailbox = await CreateMailboxAsync();
        var log = await CreateSendLogAsync(
            mailbox.Id,
            SendLogStatus.SendIntermediateError,
            attemptCount: 1);

        _mockEmailService.Setup(e => e.SendEmailWithHeadersAsync(
                It.IsAny<Mailbox>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<Attachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP 仍然不可用"));

        // Act
        await _service.ProcessQueueAsync();

        // Assert — 恢复失败，AttemptCount(2) < MaxRetryCount(3)，应重置为 Accepted
        var reloaded = await ReloadSendLogAsync(log.Id);
        reloaded.Status.Should().Be(SendLogStatus.Accepted,
            because: "恢复失败但 AttemptCount < MaxRetryCount，应重置为 Accepted 等待下一次重试");
        reloaded.AttemptCount.Should().Be(2);
    }

    #endregion
}
