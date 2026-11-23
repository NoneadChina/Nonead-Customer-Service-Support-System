using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 会话状态与分配两条路径的领域事件接线测试。
/// 二者此前只有「审计 + SignalR 实时推送」两条副作用已在服务层内联，缺的是站内通知：
/// ConversationStatusChangedEvent → ConversationStatusChangedListener（给会话所属用户发「会话状态已变更」）；
/// ConversationUserChangedEvent → ConversationUserChangedListener（给被分配坐席发「您有新的会话」）。
/// 事件必须在事务提交、实时推送之后发布：监听器用自己的 Scope 与 DbContext，读不到未提交数据。
/// </summary>
public class ConversationStatusAndAssignmentDomainEventTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<IFolderService> _mockFolderService = new();
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher = new();
    private readonly Mock<IEmailQueueService> _mockEmailQueueService = new();
    private readonly Mock<IWorkflowService> _mockWorkflowService = new();
    private readonly Mock<IConfiguration> _mockConfiguration = new();
    private readonly Mock<ISatisfactionTokenGenerator> _mockSatisfactionTokenGenerator = new();
    private readonly Mock<IFrontendUrlResolver> _mockFrontendUrlResolver = new();
    private readonly Mock<ILogger<ConversationStatusService>> _mockStatusLogger = new();
    private readonly Mock<ILogger<ConversationAssignmentService>> _mockAssignLogger = new();
    private readonly Mock<IConversationRepository> _mockConversationRepo = new();
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction = new();

    /// <summary>被接线进两个服务的强类型领域事件分发器（本测试的核心断言对象）。</summary>
    private readonly Mock<IDomainEventDispatcher> _mockDispatcher = new();

    private readonly ConversationStatusService _statusService;
    private readonly ConversationAssignmentService _assignmentService;

    public ConversationStatusAndAssignmentDomainEventTests()
    {
        _mockFrontendUrlResolver.Setup(r => r.GetFrontendUrl()).Returns("https://app.example.com");

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockTransaction.Setup(t => t.CommitAsync()).Returns(Task.CompletedTask);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .Returns(Task.CompletedTask);
        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);
        _mockNotificationPublisher.Setup(n => n.NotifyMailboxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);
        _mockNotificationPublisher.Setup(n => n.NotifyUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);

        _statusService = new ConversationStatusService(
            _mockUnitOfWork.Object,
            _mockFolderService.Object,
            _mockNotificationPublisher.Object,
            _mockEmailQueueService.Object,
            _mockWorkflowService.Object,
            _mockConfiguration.Object,
            _mockSatisfactionTokenGenerator.Object,
            _mockFrontendUrlResolver.Object,
            logger: _mockStatusLogger.Object,
            renderer: new NoneadCSS.Application.Templates.TemplateRenderer(),
            domainEventDispatcher: _mockDispatcher.Object);

        _assignmentService = new ConversationAssignmentService(
            _mockUnitOfWork.Object,
            _mockFolderService.Object,
            _mockNotificationPublisher.Object,
            _mockEmailQueueService.Object,
            _mockWorkflowService.Object,
            _mockConfiguration.Object,
            _mockAssignLogger.Object,
            domainEventDispatcher: _mockDispatcher.Object);
    }

    private static Conversation BuildConversation(int id = 1) => new()
    {
        Id = id,
        Number = id,
        Subject = "Test",
        MailboxId = 1,
        UserId = null,
        Status = ConversationStatus.Active,
        CustomerEmail = "test@example.com"
    };

    [Fact]
    public async Task UpdateStatusAsync_Should_DispatchConversationStatusChangedEvent_AfterRealtimePush()
    {
        // Arrange
        var conversation = BuildConversation();
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(conversation.Id)).ReturnsAsync(conversation);

        ConversationStatusChangedEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<ConversationStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ConversationStatusChangedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act：改为 Pending，避开关闭分支的满意度令牌与邮件逻辑
        var result = await _statusService.UpdateStatusAsync(conversation.Id, (int)ConversationStatus.Pending, userId: 3);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.Conversation.Should().BeSameAs(conversation);
        captured.OldStatus.Should().Be(ConversationStatus.Active.ToString());
        captured.NewStatus.Should().Be(ConversationStatus.Pending.ToString());
        captured.ChangedByUserId.Should().Be(3);
        // 事件只在实时推送之后发布（重复推送与重复审计已由服务层/过滤器覆盖）
        _mockNotificationPublisher.Verify(n => n.NotifyMailboxAsync(It.IsAny<string>(), "conversation_status_changed", It.IsAny<object>()), Times.Once);
        // 持久化用的是无参重载（ConversationStatusService.cs:95 → NoneadCSS.Domain/IUnitOfWork.cs:69）
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateStatusAsync_Should_NotDispatchConversationStatusChangedEvent_When_ConversationMissing()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(999)).ReturnsAsync((Conversation?)null);

        // Act
        var result = await _statusService.UpdateStatusAsync(999, (int)ConversationStatus.Pending);

        // Assert
        result.Should().BeNull();
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<ConversationStatusChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AssignAsync_Should_DispatchConversationUserChangedEvent_WithOldAndNewAssignee()
    {
        // Arrange
        var conversation = BuildConversation(2);
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(2)).ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(conversation);

        ConversationUserChangedEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<ConversationUserChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ConversationUserChangedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _assignmentService.AssignAsync(2, 11);

        // Assert
        result.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.Conversation.Should().BeSameAs(conversation);
        captured.OldUserId.Should().BeNull();
        captured.NewUserId.Should().Be(11);
        // 站内通知（给被分配坐席）由监听器补上，服务层此前只做 SignalR 定向推送
        _mockNotificationPublisher.Verify(n => n.NotifyUserAsync("11", "conversation_assigned", It.IsAny<object>()), Times.Once);
        // 持久化用的是无参重载（ConversationAssignmentService.cs:72 → NoneadCSS.Domain/IUnitOfWork.cs:69）
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AssignAsync_Should_NotDispatchConversationUserChangedEvent_When_ConversationMissing()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999)).ReturnsAsync((Conversation?)null);

        // Act
        var result = await _assignmentService.AssignAsync(999, 11);

        // Assert
        result.Should().BeNull();
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<ConversationUserChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
