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
/// ConversationAssignmentService 单元测试
/// 覆盖会话分配（AssignAsync）与批量分配（BulkAssignAsync）的全部公开行为，
/// 包括正常路径、边界条件（取消分配、空批次）、异常路径（保存失败/文件夹更新失败/工作流失败）
/// 以及批量操作的混合成功/失败场景。
/// </summary>
public class ConversationAssignmentServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IFolderService> _mockFolderService;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<IEmailQueueService> _mockEmailQueueService;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<ConversationAssignmentService>> _mockLogger;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;
    private readonly Mock<IDomainEventDispatcher> _mockDomainEventDispatcher;

    private readonly ConversationAssignmentService _service;

    public ConversationAssignmentServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockFolderService = new Mock<IFolderService>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ConversationAssignmentService>>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockDomainEventDispatcher = new Mock<IDomainEventDispatcher>();

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .Returns(Task.CompletedTask);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        _service = new ConversationAssignmentService(
            _mockUnitOfWork.Object,
            _mockFolderService.Object,
            _mockNotificationPublisher.Object,
            _mockEmailQueueService.Object,
            _mockWorkflowService.Object,
            _mockConfiguration.Object,
            _mockLogger.Object,
            _mockDomainEventDispatcher.Object
        );
    }

    #region AssignAsync

    [Fact]
    public async Task AssignAsync_WithNonExistingConversation_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.AssignAsync(999, 5);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Never);
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()), Times.Never);
        _mockNotificationPublisher.Verify(n => n.NotifyUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AssignAsync_WithValidConversation_ShouldAssignAndReturnResponse()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        var result = await _service.AssignAsync(1, 5);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.UserId.Should().Be(5);
        conversation.UserId.Should().Be(5);
        conversation.UpdatedAt.Should().NotBe(default);

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(conversation), Times.Once);
        _mockNotificationPublisher.Verify(n => n.NotifyUserAsync("5", "conversation_assigned", It.IsAny<object>()), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerAssigneeChanged), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_ShouldCaptureOldAssigneeIdBeforeAssignment()
    {
        // Arrange - 会话原本分配给用户 10，重新分配给用户 5
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = 10,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Eventy 层已移除：旧受理人只在强类型领域事件里可见，故改抓 ConversationUserChangedEvent
        ConversationUserChangedEvent? capturedEvent = null;
        _mockDomainEventDispatcher.Setup(d => d.DispatchAsync(It.IsAny<ConversationUserChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ConversationUserChangedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        // Act
        await _service.AssignAsync(1, 5);

        // Assert - 领域事件应携带重新分配前的 OldUserId（AssignTo 已把 UserId 覆盖为 5）
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Conversation.Should().BeSameAs(conversation);
        capturedEvent.OldUserId.Should().Be(10);
        capturedEvent.NewUserId.Should().Be(5);
        capturedEvent.ChangedByUserId.Should().BeNull();
        conversation.UserId.Should().Be(5);
    }

    [Fact]
    public async Task AssignAsync_WithUserIdZero_ShouldUnassignConversation()
    {
        // Arrange - userId <= 0 表示取消分配（AssignTo 将 UserId 置为 null）
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = 10,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        var result = await _service.AssignAsync(1, 0);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().BeNull();
        conversation.UserId.Should().BeNull();
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerAssigneeChanged), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_WithNegativeUserId_ShouldUnassignConversation()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = 10,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        var result = await _service.AssignAsync(1, -1);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().BeNull();
        conversation.UserId.Should().BeNull();
    }

    [Fact]
    public async Task AssignAsync_WhenSaveChangesFails_ShouldRollbackAndThrow()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = 10,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act
        var act = () => _service.AssignAsync(1, 5);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DB error");

        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
        _mockNotificationPublisher.Verify(n => n.NotifyUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AssignAsync_WhenFolderUpdateFails_ShouldRollbackAndThrow()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = 10,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .ThrowsAsync(new InvalidOperationException("Folder error"));

        // Act
        var act = () => _service.AssignAsync(1, 5);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Folder error");

        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task AssignAsync_WhenGetByIdReturnsNullAfterUpdate_ShouldUseOriginalConversation()
    {
        // Arrange - 提交后重新查询返回 null，应回退使用内存中的 conversation 构建响应
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.AssignAsync(1, 7);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.UserId.Should().Be(7);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_WhenWorkflowFails_ShouldStillReturnResponse()
    {
        // Arrange - 工作流异常被捕获并记录，不应影响分配结果
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerAssigneeChanged))
            .ThrowsAsync(new Exception("Workflow failed"));

        // Act
        var result = await _service.AssignAsync(1, 5);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(5);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockNotificationPublisher.Verify(n => n.NotifyUserAsync("5", "conversation_assigned", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_ShouldNotifyTargetUserId()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        await _service.AssignAsync(1, 42);

        // Assert - 通知接收方为新分配的用户 ID（字符串形式）
        _mockNotificationPublisher.Verify(n => n.NotifyUserAsync("42", "conversation_assigned", It.IsAny<ConversationResponse>()), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_ShouldReassignFromOneUserToAnother()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            UserId = 10,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        var result = await _service.AssignAsync(1, 20);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(20);
        conversation.UserId.Should().Be(20);
    }

    #endregion

    #region BulkAssignAsync

    [Fact]
    public async Task BulkAssignAsync_WithValidConversations_ShouldAssignAllSuccessfully()
    {
        // Arrange
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            UserId = 5
        };

        var conversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "Test 1", MailboxId = 1, UserId = null, Status = ConversationStatus.Active, CustomerEmail = "t1@example.com" },
            new() { Id = 2, Subject = "Test 2", MailboxId = 1, UserId = null, Status = ConversationStatus.Active, CustomerEmail = "t2@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();

        conversations.Should().AllSatisfy(c => c.UserId.Should().Be(5));

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Exactly(2));
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Exactly(2));
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()), Times.Exactly(2));
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerAssigneeChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(2, WorkflowConst.TriggerAssigneeChanged), Times.Once);
    }

    [Fact]
    public async Task BulkAssignAsync_WithEmptyConversationIds_ShouldReturnEmptyResponse()
    {
        // Arrange
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int>(),
            UserId = 5
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(new List<Conversation>());

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(0);
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BulkAssignAsync_WithSomeNonExistingConversations_ShouldReportFailures()
    {
        // Arrange - 会话 999 不存在
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1, 999 },
            UserId = 5
        };

        var conversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "Test 1", MailboxId = 1, UserId = null, Status = ConversationStatus.Active, CustomerEmail = "t1@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("999");

        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerAssigneeChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(999, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BulkAssignAsync_WithAllNonExistingConversations_ShouldReportAllFailures()
    {
        // Arrange
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            UserId = 5
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(new List<Conversation>());

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(2);
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(e => e.Contains("1"));
        result.Errors.Should().Contain(e => e.Contains("2"));

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BulkAssignAsync_WhenSaveChangesFails_ShouldRollbackAndReportFailure()
    {
        // Arrange
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1 },
            UserId = 5
        };

        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test 1",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "t1@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(new List<Conversation> { conversation });

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ThrowsAsync(new InvalidOperationException("DB connection lost"));

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(1);
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("1");
        result.Errors[0].Should().Contain("服务器内部错误");

        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BulkAssignAsync_WhenFolderUpdateFails_ShouldReportFailure()
    {
        // Arrange
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1 },
            UserId = 5
        };

        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test 1",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "t1@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(new List<Conversation> { conversation });

        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .ThrowsAsync(new InvalidOperationException("Folder service unavailable"));

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors[0].Should().Contain("服务器内部错误");

        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task BulkAssignAsync_WithCurrentUserId_ShouldCompleteWithoutError()
    {
        // Arrange - currentUserId 仅用于日志记录，不影响业务逻辑
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1 },
            UserId = 5
        };

        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test 1",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "t1@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(new List<Conversation> { conversation });

        // Act
        var result = await _service.BulkAssignAsync(request, currentUserId: 99);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        conversation.UserId.Should().Be(5);
    }

    [Fact]
    public async Task BulkAssignAsync_WhenWorkflowFails_ShouldNotAffectSuccessCount()
    {
        // Arrange - 工作流失败不影响已提交的分配结果
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            UserId = 5
        };

        var conversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "Test 1", MailboxId = 1, UserId = null, Status = ConversationStatus.Active, CustomerEmail = "t1@example.com" },
            new() { Id = 2, Subject = "Test 2", MailboxId = 1, UserId = null, Status = ConversationStatus.Active, CustomerEmail = "t2@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Workflow crashed"));

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task BulkAssignAsync_ShouldTriggerWorkflowOnlyForSuccessfulAssignments()
    {
        // Arrange - 会话 1 成功，会话 999 不存在（失败）
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1, 999 },
            UserId = 5
        };

        var conversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "Test 1", MailboxId = 1, UserId = null, Status = ConversationStatus.Active, CustomerEmail = "t1@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);

        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerAssigneeChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(999, It.IsAny<string>()), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BulkAssignAsync_ShouldNotTriggerEventsOrNotifications()
    {
        // Arrange - 批量分配仅触发工作流，不触发事件分发与用户通知（与单条分配不同）
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1 },
            UserId = 5
        };

        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test 1",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "t1@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(new List<Conversation> { conversation });

        // Act
        await _service.BulkAssignAsync(request);

        // Assert
        _mockNotificationPublisher.Verify(n => n.NotifyUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        _mockNotificationPublisher.Verify(n => n.NotifyAllAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task BulkAssignAsync_WithMixedSuccessAndSaveFailure_ShouldProcessRemainingConversations()
    {
        // Arrange - 两个会话，第一个保存失败，第二个成功
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            UserId = 5
        };

        var conversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "Test 1", MailboxId = 1, UserId = null, Status = ConversationStatus.Active, CustomerEmail = "t1@example.com" },
            new() { Id = 2, Subject = "Test 2", MailboxId = 1, UserId = null, Status = ConversationStatus.Active, CustomerEmail = "t2@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        var saveCallCount = 0;
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(() =>
            {
                saveCallCount++;
                if (saveCallCount == 1)
                {
                    throw new InvalidOperationException("First save fails");
                }
                return 1;
            });

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert - 第一个失败后仍处理第二个会话
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("服务器内部错误");

        conversations[0].UserId.Should().Be(5); // AssignTo 已执行，但保存回滚
        conversations[1].UserId.Should().Be(5);

        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(2, WorkflowConst.TriggerAssigneeChanged), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BulkAssignAsync_WithUnassignUserId_ShouldUnassignAllConversations()
    {
        // Arrange - userId=0 表示取消分配
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            UserId = 0
        };

        var conversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "Test 1", MailboxId = 1, UserId = 10, Status = ConversationStatus.Active, CustomerEmail = "t1@example.com" },
            new() { Id = 2, Subject = "Test 2", MailboxId = 1, UserId = 20, Status = ConversationStatus.Active, CustomerEmail = "t2@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.SuccessCount.Should().Be(2);
        conversations.Should().AllSatisfy(c => c.UserId.Should().BeNull());
    }

    [Fact]
    public async Task BulkAssignAsync_WithSingleConversation_ShouldAssignSuccessfully()
    {
        // Arrange
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1 },
            UserId = 5
        };

        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test 1",
            MailboxId = 1,
            UserId = null,
            Status = ConversationStatus.Active,
            CustomerEmail = "t1@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdsWithFoldersForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(new List<Conversation> { conversation });

        // Act
        var result = await _service.BulkAssignAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(1);
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        conversation.UserId.Should().Be(5);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    #endregion
}
