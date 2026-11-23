using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ConversationStatusService 单元测试 - 使用 Moq 完全模拟依赖
/// </summary>
public class ConversationStatusServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IFolderService> _mockFolderService;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<IEmailQueueService> _mockEmailQueueService;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ISatisfactionTokenGenerator> _mockSatisfactionTokenGenerator;
    private readonly Mock<IFrontendUrlResolver> _mockFrontendUrlResolver;
    private readonly Mock<ILogger<ConversationStatusService>> _mockLogger;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    private readonly ConversationStatusService _service;

    public ConversationStatusServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockFolderService = new Mock<IFolderService>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockSatisfactionTokenGenerator = new Mock<ISatisfactionTokenGenerator>();
        _mockFrontendUrlResolver = new Mock<IFrontendUrlResolver>();
        _mockFrontendUrlResolver.Setup(r => r.GetFrontendUrl()).Returns("https://app.example.com");
        _mockLogger = new Mock<ILogger<ConversationStatusService>>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .Returns(Task.CompletedTask);

        _service = new ConversationStatusService(
            _mockUnitOfWork.Object,
            _mockFolderService.Object,
            _mockNotificationPublisher.Object,
            _mockEmailQueueService.Object,
            _mockWorkflowService.Object,
            _mockConfiguration.Object,
            _mockSatisfactionTokenGenerator.Object,
            _mockFrontendUrlResolver.Object,
            _mockLogger.Object,
            new NoneadCSS.Application.Templates.TemplateRenderer()
        );
    }

    #region UpdateStatusAsync Tests

    [Fact]
    public async Task UpdateStatusAsync_WithNonExistingConversation_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.UpdateStatusAsync(999, (int)ConversationStatus.Closed);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateStatusAsync_WithValidConversation_ShouldUpdateStatusAndReturnResponse()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerStatusChanged))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Pending);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Status.Should().Be((int)ConversationStatus.Pending);
        conversation.Status.Should().Be(ConversationStatus.Pending);

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(conversation), Times.Once);
        _mockNotificationPublisher.Verify(n => n.NotifyMailboxAsync(It.IsAny<string>(), "conversation_status_changed", It.IsAny<object>()), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerStatusChanged), Times.Once);
        // Active -> Pending 不是关闭或重新打开，不应触发额外工作流
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationClosed), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationReopened), Times.Never);
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenClosing_ShouldTriggerClosedWorkflow()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Closed);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Closed);
        conversation.Status.Should().Be(ConversationStatus.Closed);

        // 应触发状态变更 + 会话关闭两个工作流
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerStatusChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationClosed), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationReopened), Times.Never);
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenReopening_ShouldTriggerReopenedWorkflow()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Closed,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Active);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Active);
        conversation.Status.Should().Be(ConversationStatus.Active);

        // 应触发状态变更 + 会话重新打开两个工作流
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerStatusChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationReopened), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationClosed), Times.Never);
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenAlreadyClosedAndClosingAgain_ShouldNotTriggerClosedWorkflowTwice()
    {
        // Arrange - 会话已经是关闭状态，再次关闭不应触发 TriggerConversationClosed
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Closed,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Closed);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Closed);

        // wasClosed 为 true，新状态也是 Closed，不满足 !wasClosed && Closed 条件
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerStatusChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationClosed), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationReopened), Times.Never);
    }

    [Fact]
    public async Task UpdateStatusAsync_WithUserIdAndNoAssignee_ShouldAssignUser()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            UserId = null,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Pending, userId: 5);

        // Assert
        result.Should().NotBeNull();
        conversation.UserId.Should().Be(5);
    }

    [Fact]
    public async Task UpdateStatusAsync_WithUserIdAndExistingAssignee_ShouldNotReassign()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            UserId = 10,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Pending, userId: 5);

        // Assert
        result.Should().NotBeNull();
        conversation.UserId.Should().Be(10);
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenWorkflowFails_ShouldStillReturnResponse()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Workflow failed"));

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Pending);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Pending);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_FromActiveToSpam_ShouldSucceed()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Spam);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Spam);
        conversation.Status.Should().Be(ConversationStatus.Spam);

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(conversation), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_FromSpamToActive_ShouldSucceed()
    {
        // Arrange - 模拟T000054场景：会话已标记为垃圾邮件，尝试改回活跃
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Spam,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Active);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Active);
        conversation.Status.Should().Be(ConversationStatus.Active);

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(conversation), Times.Once);
        // 状态变更的对外可观察行为 = 发送实时通知（Eventy 事件层已移除）
        _mockNotificationPublisher.Verify(n => n.NotifyMailboxAsync(It.IsAny<string>(), "conversation_status_changed", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_FromSpamToClosed_ShouldSucceed()
    {
        // Arrange - 从垃圾邮件状态改为已关闭
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Spam,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Closed);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Closed);
        conversation.Status.Should().Be(ConversationStatus.Closed);
        conversation.ClosedAt.Should().NotBeNull();

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        // Spam -> Closed：应触发状态变更 + 会话关闭工作流
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerStatusChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationClosed), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_FromSpamToPending_ShouldSucceed()
    {
        // Arrange - 从垃圾邮件状态改为待处理
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Spam,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Pending);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Pending);
        conversation.Status.Should().Be(ConversationStatus.Pending);

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    #endregion

    #region BulkUpdateStatusAsync Tests

    [Fact]
    public async Task BulkUpdateStatusAsync_WithValidConversations_ShouldUpdateSuccessfully()
    {
        // Arrange
        var request = new BulkStatusRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            Status = (int)ConversationStatus.Closed
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, Status = ConversationStatus.Active, CustomerEmail = "test1@example.com" },
            new Conversation { Id = 2, Subject = "Test 2", MailboxId = 1, Status = ConversationStatus.Active, CustomerEmail = "test2@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.BulkUpdateStatusAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.TotalCount.Should().Be(2);

        conversations.All(c => c.Status == ConversationStatus.Closed).Should().BeTrue();

        // 每个会话触发状态变更 + 会话关闭
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), WorkflowConst.TriggerStatusChanged), Times.Exactly(2));
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), WorkflowConst.TriggerConversationClosed), Times.Exactly(2));
    }

    [Fact]
    public async Task BulkUpdateStatusAsync_WithSomeNonExistingConversations_ShouldReportFailures()
    {
        // Arrange
        var request = new BulkStatusRequest
        {
            ConversationIds = new List<int> { 1, 999 },
            Status = (int)ConversationStatus.Pending
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, Status = ConversationStatus.Active, CustomerEmail = "test1@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.BulkUpdateStatusAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.TotalCount.Should().Be(2);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("999");
    }

    [Fact]
    public async Task BulkUpdateStatusAsync_WhenReopeningFromClosed_ShouldTriggerReopenedWorkflow()
    {
        // Arrange
        var request = new BulkStatusRequest
        {
            ConversationIds = new List<int> { 1 },
            Status = (int)ConversationStatus.Active
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, Status = ConversationStatus.Closed, CustomerEmail = "test1@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.BulkUpdateStatusAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(1);

        // 从关闭状态重新打开，应触发状态变更 + 重新打开
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerStatusChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationReopened), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerConversationClosed), Times.Never);
    }

    [Fact]
    public async Task BulkUpdateStatusAsync_WhenWorkflowFails_ShouldNotAffectSuccessCount()
    {
        // Arrange
        var request = new BulkStatusRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            Status = (int)ConversationStatus.Pending
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, Status = ConversationStatus.Active, CustomerEmail = "test1@example.com" },
            new Conversation { Id = 2, Subject = "Test 2", MailboxId = 1, Status = ConversationStatus.Active, CustomerEmail = "test2@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // 工作流失败，但不应影响批量操作的 SuccessCount
        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Workflow failed"));

        // Act
        var result = await _service.BulkUpdateStatusAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
    }

    #endregion
}
