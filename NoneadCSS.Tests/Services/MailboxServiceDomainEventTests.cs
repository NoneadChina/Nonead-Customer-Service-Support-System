using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// MailboxService 领域事件接线测试。
/// 验证邮箱配置更新保存后会分发 MailboxUpdatedEvent：其监听器 UpdateMailboxCountersListener
/// 通过 Quartz 排程 UpdateFolderCountersJob 重算该邮箱的文件夹计数，此前完全没有等价实现。
/// 事件必须在 SaveChangesAsync 之后发布：监听器用自己的 Scope 与 DbContext，读不到未提交数据。
/// </summary>
public class MailboxServiceDomainEventTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<IEmailService> _mockEmailService = new();
    private readonly Mock<ISystemOptionService> _mockSystemOptionService = new();
    private readonly Mock<IMailboxRepository> _mockMailboxRepository = new();
    private readonly Mock<IServiceProvider> _mockServiceProvider = new();
    private readonly IMemoryCache _cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));

    /// <summary>被接线进 MailboxService 的强类型领域事件分发器（本测试的核心断言对象）。</summary>
    private readonly Mock<IDomainEventDispatcher> _mockDispatcher = new();

    private readonly MailboxService _service;

    public MailboxServiceDomainEventTests()
    {
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepository.Object);
        // MailboxService 走 IUnitOfWork 的无参重载（NoneadCSS.Domain/IUnitOfWork.cs:69，见 MailboxService.cs:301）；
        // 带 CancellationToken 的是另一个重载（:75），Setup/Verify 打在服务真正调用的重载上，否则 Times.Never 也是空断言。
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        _service = new MailboxService(
            _mockUnitOfWork.Object,
            _mockEmailService.Object,
            _cache,
            _mockSystemOptionService.Object,
            Options.Create(new CacheOptions()),
            _mockServiceProvider.Object,
            _mockDispatcher.Object);
    }

    private static Mailbox BuildMailbox(int id = 3) => new()
    {
        Id = id,
        Name = "Support",
        Email = "support@example.com",
        InHost = "imap.example.com",
        InPort = 993,
        InUsername = "support@example.com",
        InPassword = "old-password",
        OutHost = "smtp.example.com",
        OutPort = 465,
        OutUsername = "support@example.com",
        OutPassword = "old-password",
        IsActive = true,
        FetchFrequency = 5,
        MailboxUsers = new List<MailboxUser>()
    };

    private static CreateMailboxRequest BuildRequest() => new()
    {
        Name = "Support New",
        Email = "support@example.com",
        InHost = "imap.example.com",
        InPort = 993,
        InUsername = "support@example.com",
        OutHost = "smtp.example.com",
        OutPort = 465,
        OutUsername = "support@example.com",
        IsActive = true,
        FetchFrequency = 10
    };

    [Fact]
    public async Task UpdateMailboxAsync_Should_DispatchMailboxUpdatedEvent_AfterSaving()
    {
        // Arrange
        var mailbox = BuildMailbox();
        _mockMailboxRepository.Setup(r => r.GetByIdTrackedAsync(mailbox.Id)).ReturnsAsync(mailbox);
        _mockMailboxRepository.Setup(r => r.GetByEmailAsync(mailbox.Email)).ReturnsAsync(mailbox);

        MailboxUpdatedEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<MailboxUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MailboxUpdatedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateMailboxAsync(mailbox.Id, BuildRequest());

        // Assert：更新已提交，事件带 MailboxId 供监听器排程计数任务
        result.Should().NotBeNull();
        mailbox.FetchFrequency.Should().Be(10);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        captured.Should().NotBeNull();
        captured!.MailboxId.Should().Be(mailbox.Id);
    }

    [Fact]
    public async Task UpdateMailboxAsync_Should_NotDispatchMailboxUpdatedEvent_When_MailboxNotFound()
    {
        // Arrange
        _mockMailboxRepository.Setup(r => r.GetByIdTrackedAsync(999)).ReturnsAsync((Mailbox?)null);

        // Act
        var result = await _service.UpdateMailboxAsync(999, BuildRequest());

        // Assert：未保存就不该排程计数任务
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<MailboxUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateMailboxAsync_Should_NotDispatchMailboxUpdatedEvent_When_EmailConflicts()
    {
        // Arrange：邮箱地址已被另一个邮箱占用 → 直接返回 null
        var mailbox = BuildMailbox();
        _mockMailboxRepository.Setup(r => r.GetByIdTrackedAsync(mailbox.Id)).ReturnsAsync(mailbox);
        _mockMailboxRepository.Setup(r => r.GetByEmailAsync(mailbox.Email)).ReturnsAsync(new Mailbox { Id = 999, Email = mailbox.Email });

        // Act
        var result = await _service.UpdateMailboxAsync(mailbox.Id, BuildRequest());

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<MailboxUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
