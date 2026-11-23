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

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ConversationService 单元测试 - 使用 Moq 完全模拟依赖
/// 测试覆盖率目标：核心方法的 60%+
/// </summary>
public class ConversationServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<IEmailQueueService> _mockEmailQueueService;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<IMailTemplateService> _mockMailTemplateService;
    private readonly Mock<IEmailSignatureService> _mockEmailSignatureService;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<IConversationAssignmentService> _mockAssignmentService;
    private readonly Mock<IConversationStatusService> _mockStatusService;
    private readonly Mock<IConversationThreadService> _mockThreadService;
    private readonly Mock<IConversationDraftService> _mockDraftService;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<ConversationService>> _mockLogger;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<IRepository<Follower>> _mockFollowerRepo;
    private readonly Mock<IRepository<Star>> _mockStarRepo;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver;

    private readonly ConversationService _service;

    public ConversationServiceTests()
    {
        // 初始化所有 Mock 对象
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockUserRepo = new Mock<IUserRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockMailTemplateService = new Mock<IMailTemplateService>();
        _mockEmailSignatureService = new Mock<IEmailSignatureService>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockAssignmentService = new Mock<IConversationAssignmentService>();
        _mockStatusService = new Mock<IConversationStatusService>();
        _mockThreadService = new Mock<IConversationThreadService>();
        _mockDraftService = new Mock<IConversationDraftService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<ConversationService>>();
        _mockAuditService = new Mock<IAuditService>();
        _mockFollowerRepo = new Mock<IRepository<Follower>>();
        _mockStarRepo = new Mock<IRepository<Star>>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockUserRoleResolver = new Mock<IUserRoleResolver>();

        // 默认超管身份，避免硬删除双重校验拦截掉正常业务测试
        _mockCurrentUserAccessor.Setup(a => a.UserId).Returns(1);
        _mockCurrentUserAccessor.Setup(a => a.IsSuperAdmin).Returns(true);
        _mockUserRoleResolver.Setup(r => r.IsSuperAdminAsync(It.IsAny<int>())).ReturnsAsync(true);

        // 配置 UnitOfWork 返回相应的 Mock 仓储
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Repository<Follower>()).Returns(_mockFollowerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Repository<Star>()).Returns(_mockStarRepo.Object);

        // 全局默认事务与 SaveChangesAsync 配置（个别测试可按需覆盖）
        var defaultTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(defaultTransaction.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        // 创建 ConversationService 实例
        _service = new ConversationService(
            _mockUnitOfWork.Object,
            _mockEmailQueueService.Object,
            _mockNotificationPublisher.Object,
            _mockMailTemplateService.Object,
            _mockEmailSignatureService.Object,
            _mockWorkflowService.Object,
            _mockAssignmentService.Object,
            _mockStatusService.Object,
            _mockThreadService.Object,
            _mockDraftService.Object,
            _mockConfiguration.Object,
            _mockLogger.Object,
            _mockAuditService.Object,
            _mockCurrentUserAccessor.Object,
            _mockUserRoleResolver.Object);
    }

    #region GetConversationByIdAsync Tests

    [Fact]
    public async Task GetConversationByIdAsync_WithExistingId_ShouldReturnConversation()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test Conversation",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer@example.com",
            Threads = new List<ConversationThread>
            {
                new ConversationThread { Id = 1, Body = "First thread", Type = ThreadType.Customer }
            }
        };

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        var result = await _service.GetConversationByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Subject.Should().Be("Test Conversation");
    }

    [Fact]
    public async Task GetConversationByIdAsync_WithNonExistingId_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.GetConversationByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateConversationAsync Tests

    [Fact]
    public async Task CreateConversationAsync_WithEmailType_ShouldCreateSuccessfully()
    {
        // Arrange
        var request = new CreateConversationRequest
        {
            MailboxId = 1,
            CustomerEmail = "customer@example.com",
            Subject = "Test Subject",
            Body = "Test Body",
            Type = (int)ConversationType.Email,
            SourceVia = (int)SourceVia.Customer,
            Priority = (int)ConversationPriority.Normal
        };

        var customer = new Customer { Id = 1, FirstName = "John", LastName = "Doe" };

        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(mockTransaction.Object);

        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("customer@example.com"))
            .ReturnsAsync(customer);

        Conversation? savedConversation = null;
        _mockConversationRepo.Setup(r => r.AddAsync(It.IsAny<Conversation>()))
            .Callback<Conversation>(c => savedConversation = c);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _service.CreateConversationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Subject.Should().Be("Test Subject");
        result.CustomerEmail.Should().Be("customer@example.com");
        result.Type.Should().Be((int)ConversationType.Email);

        savedConversation.Should().NotBeNull();
        savedConversation!.Threads.Should().HaveCount(1);
        savedConversation.Threads.First().Body.Should().Be("Test Body");

        _mockNotificationPublisher.Verify(n => n.NotifyMailboxAsync(It.IsAny<string>(), "conversation_created", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task CreateConversationAsync_ShouldTriggerWorkflow()
    {
        // Arrange
        var request = new CreateConversationRequest
        {
            MailboxId = 1,
            CustomerEmail = "test@example.com",
            Subject = "Test",
            Body = "Body",
            Type = 1,
            SourceVia = 1
        };

        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(mockTransaction.Object);

        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Customer?)null);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), WorkflowConst.TriggerNewConversation))
            .ReturnsAsync(1);

        // Act
        await _service.CreateConversationAsync(request);

        // Assert
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), WorkflowConst.TriggerNewConversation), Times.Once);
    }

    [Fact]
    public async Task CreateConversationAsync_WhenWorkflowFails_ShouldStillReturnConversation()
    {
        // Arrange
        var request = new CreateConversationRequest
        {
            MailboxId = 1,
            CustomerEmail = "test@example.com",
            Subject = "Test",
            Body = "Body",
            Type = 1,
            SourceVia = 1
        };

        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(mockTransaction.Object);

        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((Customer?)null);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), WorkflowConst.TriggerNewConversation))
            .ThrowsAsync(new Exception("Workflow failed"));

        // Act
        var result = await _service.CreateConversationAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Subject.Should().Be("Test");
    }

    #endregion

    #region UpdateStatusAsync Tests

    [Fact]
    public async Task UpdateStatusAsync_ShouldDelegateToStatusService()
    {
        // Arrange
        var expectedResponse = new ConversationResponse
        {
            Id = 1,
            Subject = "Test",
            Status = (int)ConversationStatus.Closed
        };

        _mockStatusService.Setup(s => s.UpdateStatusAsync(1, (int)ConversationStatus.Closed, null))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.UpdateStatusAsync(1, (int)ConversationStatus.Closed);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be((int)ConversationStatus.Closed);

        _mockStatusService.Verify(s => s.UpdateStatusAsync(1, (int)ConversationStatus.Closed, null), Times.Once);
    }

    #endregion

    #region AssignAsync Tests

    [Fact]
    public async Task AssignAsync_ShouldDelegateToAssignmentService()
    {
        // Arrange
        var expectedResponse = new ConversationResponse
        {
            Id = 1,
            Subject = "Test",
            UserId = 5
        };

        _mockAssignmentService.Setup(s => s.AssignAsync(1, 5))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.AssignAsync(1, 5);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(5);

        _mockAssignmentService.Verify(s => s.AssignAsync(1, 5), Times.Once);
    }

    #endregion

    #region AddNoteAsync Tests

    [Fact]
    public async Task AddNoteAsync_ShouldDelegateToThreadService()
    {
        // Arrange
        var request = new AddNoteRequest { Body = "This is a note" };
        var expectedResponse = new ThreadResponse
        {
            Id = 1,
            Body = "This is a note"
        };

        _mockThreadService.Setup(s => s.AddNoteAsync(1, request, 100))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.AddNoteAsync(1, request, 100);

        // Assert
        result.Should().NotBeNull();
        result!.Body.Should().Be("This is a note");

        _mockThreadService.Verify(s => s.AddNoteAsync(1, request, 100), Times.Once);
    }

    #endregion

    #region DeleteConversationAsync Tests

    [Fact]
    public async Task DeleteConversationAsync_WithExistingId_ShouldReturnTrue()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "To Delete",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _service.DeleteConversationAsync(1);

        // Assert
        result.Should().BeTrue();
        conversation.State.Should().Be(ConversationState.Deleted);
        conversation.DeletedAt.Should().NotBeNull();

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteConversationAsync_WithNonExistingId_ShouldReturnFalse()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.DeleteConversationAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region ToggleFollowAsync Tests

    [Fact]
    public async Task ToggleFollowAsync_WhenNotFollowing_ShouldAddFollower()
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

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockFollowerRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Follower, bool>>>()))
            .ReturnsAsync(new List<Follower>());

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        var updatedConversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com",
            Followers = new List<Follower> { new Follower { UserId = 100 } }
        };

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(updatedConversation);

        // Act
        var result = await _service.ToggleFollowAsync(1, 100);

        // Assert
        result.Should().NotBeNull();

        _mockFollowerRepo.Verify(r => r.AddAsync(It.IsAny<Follower>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ToggleFollowAsync_WhenAlreadyFollowing_ShouldRemoveFollower()
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

        var existingFollower = new Follower { ConversationId = 1, UserId = 100 };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockFollowerRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Follower, bool>>>()))
            .ReturnsAsync(new List<Follower> { existingFollower });

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        var updatedConversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com",
            Followers = new List<Follower>()
        };

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(updatedConversation);

        // Act
        var result = await _service.ToggleFollowAsync(1, 100);

        // Assert
        result.Should().NotBeNull();

        _mockFollowerRepo.Verify(r => r.DeleteAsync(It.IsAny<Follower>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ToggleFollowAsync_WithNonExistingConversation_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.ToggleFollowAsync(999, 100);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ToggleStarAsync Tests

    [Fact]
    public async Task ToggleStarAsync_WhenNotStarred_ShouldAddStar()
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

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        _mockStarRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Star, bool>>>()))
            .ReturnsAsync(new List<Star>());

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _service.ToggleStarAsync(1, 100);

        // Assert
        result.Should().NotBeNull();
        _mockStarRepo.Verify(r => r.AddAsync(It.IsAny<Star>()), Times.Once);
    }

    [Fact]
    public async Task ToggleStarAsync_WhenAlreadyStarred_ShouldRemoveStar()
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

        var existingStar = new Star { ConversationId = 1, UserId = 100 };

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        _mockStarRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Star, bool>>>()))
            .ReturnsAsync(new List<Star> { existingStar });

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _service.ToggleStarAsync(1, 100);

        // Assert
        result.Should().NotBeNull();
        _mockStarRepo.Verify(r => r.DeleteAsync(It.IsAny<Star>()), Times.Once);
    }

    [Fact]
    public async Task ToggleStarAsync_WithNonExistingConversation_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.ToggleStarAsync(999, 100);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region BulkDeleteAsync Tests

    [Fact]
    public async Task BulkDeleteAsync_WithValidIds_ShouldDeleteSuccessfully()
    {
        // Arrange
        var request = new BulkConversationRequest
        {
            ConversationIds = new List<int> { 1, 2, 3 }
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, State = ConversationState.Published, CustomerEmail = "test1@example.com" },
            new Conversation { Id = 2, Subject = "Test 2", MailboxId = 1, State = ConversationState.Published, CustomerEmail = "test2@example.com" },
            new Conversation { Id = 3, Subject = "Test 3", MailboxId = 1, State = ConversationState.Published, CustomerEmail = "test3@example.com" }
        };

        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(mockTransaction.Object);

        _mockConversationRepo.Setup(r => r.GetByIdsForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(3);

        // Act
        var result = await _service.BulkDeleteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(3);
        result.FailedCount.Should().Be(0);
        result.TotalCount.Should().Be(3);

        conversations.All(c => c.State == ConversationState.Deleted).Should().BeTrue();
    }

    [Fact]
    public async Task BulkDeleteAsync_WithNonExistingIds_ShouldReportFailures()
    {
        // Arrange
        var request = new BulkConversationRequest
        {
            ConversationIds = new List<int> { 1, 999, 1000 }
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, State = ConversationState.Published, CustomerEmail = "test1@example.com" }
        };

        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(mockTransaction.Object);

        _mockConversationRepo.Setup(r => r.GetByIdsForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _service.BulkDeleteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(2);
        result.TotalCount.Should().Be(3);
        result.Errors.Should().HaveCount(2);
    }

    #endregion

    #region BulkUpdatePriorityAsync Tests

    [Fact]
    public async Task BulkUpdatePriorityAsync_WithValidIds_ShouldUpdateSuccessfully()
    {
        // Arrange
        var request = new BulkPriorityRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            Priority = (int)ConversationPriority.High
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, Priority = ConversationPriority.Normal, CustomerEmail = "test1@example.com" },
            new Conversation { Id = 2, Subject = "Test 2", MailboxId = 1, Priority = ConversationPriority.Normal, CustomerEmail = "test2@example.com" }
        };

        var mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(mockTransaction.Object);

        _mockConversationRepo.Setup(r => r.GetByIdsForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(2);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), WorkflowConst.TriggerPriorityChanged))
            .ReturnsAsync(1);

        // Act
        var result = await _service.BulkUpdatePriorityAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);

        conversations.All(c => c.Priority == ConversationPriority.High).Should().BeTrue();
    }

    #endregion

    #region RestoreConversationAsync Tests

    [Fact]
    public async Task RestoreConversationAsync_WithDeletedConversation_ShouldRestoreSuccessfully()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Deleted Conv",
            MailboxId = 1,
            State = ConversationState.Deleted,
            DeletedAt = DateTime.UtcNow.AddDays(-1),
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetDeletedTrackedByIdAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        var restoredConversation = new Conversation
        {
            Id = 1,
            Subject = "Deleted Conv",
            MailboxId = 1,
            State = ConversationState.Published,
            DeletedAt = null,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(restoredConversation);

        // Act
        var result = await _service.RestoreConversationAsync(1);

        // Assert
        result.Should().NotBeNull();
        conversation.State.Should().Be(ConversationState.Published);
        conversation.DeletedAt.Should().BeNull();

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task RestoreConversationAsync_WithNonExistingId_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetDeletedTrackedByIdAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.RestoreConversationAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region UpdatePriorityAsync Tests

    [Fact]
    public async Task UpdatePriorityAsync_WithExistingId_ShouldUpdateSuccessfully()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Priority = ConversationPriority.Normal,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerPriorityChanged))
            .ReturnsAsync(1);

        // Act
        var result = await _service.UpdatePriorityAsync(1, (int)ConversationPriority.High);

        // Assert
        result.Should().NotBeNull();
        conversation.Priority.Should().Be(ConversationPriority.High);

        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerPriorityChanged), Times.Once);
    }

    [Fact]
    public async Task UpdatePriorityAsync_WithNonExistingId_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.UpdatePriorityAsync(999, (int)ConversationPriority.High);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetFollowersAsync Tests

    [Fact]
    public async Task GetFollowersAsync_ShouldReturnFollowerIds()
    {
        // Arrange
        var followers = new List<Follower>
        {
            new Follower { ConversationId = 1, UserId = 10 },
            new Follower { ConversationId = 1, UserId = 20 },
            new Follower { ConversationId = 1, UserId = 30 }
        };

        _mockFollowerRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Follower, bool>>>()))
            .ReturnsAsync(followers);

        // Act
        var result = await _service.GetFollowersAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().Contain(new List<int> { 10, 20, 30 });
    }

    #endregion

    #region GetStarredConversationIdsAsync Tests

    [Fact]
    public async Task GetStarredConversationIdsAsync_ShouldReturnStarredIds()
    {
        // Arrange
        var stars = new List<Star>
        {
            new Star { ConversationId = 1, UserId = 100 },
            new Star { ConversationId = 2, UserId = 100 },
            new Star { ConversationId = 3, UserId = 100 }
        };

        _mockStarRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Star, bool>>>()))
            .ReturnsAsync(stars);

        // Act
        var result = await _service.GetStarredConversationIdsAsync(100);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().Contain(new List<int> { 1, 2, 3 });
    }

    #endregion

    #region CloneConversationAsync Tests

    [Fact]
    public async Task CloneConversationAsync_WithExistingId_ShouldReturnClonedConversation()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Original",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer@example.com",
            CustomerId = 10
        };

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        var result = await _service.CloneConversationAsync(1, userId: 100);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CloneConversationAsync_WithNonExistingId_ShouldReturnNull()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.CloneConversationAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region HardDeleteConversationAsync Tests

    [Fact]
    public async Task HardDeleteConversationAsync_WithExistingId_ShouldReturnTrue()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "To Hard Delete",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com",
            DeletedAt = DateTime.UtcNow // 已软删除
        };

        _mockConversationRepo.Setup(r => r.GetDeletedByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        var result = await _service.HardDeleteConversationAsync(1);

        // Assert
        result.Should().BeTrue();
        _mockConversationRepo.Verify(r => r.HardDeleteAsync(1), Times.Once);
    }

    [Fact]
    public async Task HardDeleteConversationAsync_WithNonExistingId_ShouldReturnFalse()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.HardDeleteConversationAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ClearTrashByMailboxAsync Tests

    [Fact]
    public async Task ClearTrashByMailboxAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.HardDeleteAllDeletedByMailboxIdAsync(1))
            .ReturnsAsync(5);

        // Act
        var result = await _service.ClearTrashByMailboxAsync(1);

        // Assert
        result.Should().Be(5);
    }

    #endregion

    #region GetTrashByMailboxAsync Tests

    [Fact]
    public async Task GetTrashByMailboxAsync_ShouldReturnPagedDeletedConversations()
    {
        // Arrange — 使用 Find 模拟返回已删除的会话
        var deletedConversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "Deleted 1", MailboxId = 1, DeletedAt = DateTime.UtcNow },
            new() { Id = 2, Subject = "Deleted 2", MailboxId = 1, DeletedAt = DateTime.UtcNow }
        };

        _mockConversationRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Conversation, bool>>>()))
            .ReturnsAsync(deletedConversations);

        // Act
        var result = await _service.GetTrashByMailboxAsync(1, 1, 10);

        // Assert
        result.Should().NotBeNull();
        result.TotalItems.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region CreatePhoneConversationAsync Tests

    [Fact]
    public async Task CreatePhoneConversationAsync_WithValidRequest_ShouldCreateConversation()
    {
        // Arrange
        var request = new CreatePhoneConversationRequest
        {
            MailboxId = 1,
            CustomerName = "Phone Customer",
            CustomerEmail = "phone@example.com",
            Subject = "Phone Call",
            Body = "Called about issue"
        };

        var mailbox = new Mailbox { Id = 1, Email = "support@example.com", Name = "Support" };
        _mockUnitOfWork.Setup(u => u.Mailboxes.GetByIdAsync(1))
            .ReturnsAsync(mailbox);

        // Act
        var result = await _service.CreatePhoneConversationAsync(request, userId: 100);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region BulkAssignAsync Tests

    [Fact]
    public async Task BulkAssignAsync_WithValidIds_ShouldAssignSuccessfully()
    {
        // Arrange — ConversationService 将批量分配委托给 IConversationAssignmentService
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            UserId = 100
        };

        var expected = new BulkOperationResponse
        {
            TotalCount = 2,
            SuccessCount = 2,
            FailedCount = 0
        };
        _mockAssignmentService.Setup(s => s.BulkAssignAsync(request, 200))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.BulkAssignAsync(request, currentUserId: 200);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        _mockAssignmentService.Verify(s => s.BulkAssignAsync(request, 200), Times.Once);
    }

    [Fact]
    public async Task BulkAssignAsync_WithNonExistingIds_ShouldReportFailures()
    {
        // Arrange — ConversationService 将批量分配委托给 IConversationAssignmentService
        var request = new BulkAssignRequest
        {
            ConversationIds = new List<int> { 999, 998 },
            UserId = 100
        };

        var expected = new BulkOperationResponse
        {
            TotalCount = 2,
            SuccessCount = 0,
            FailedCount = 2
        };
        _mockAssignmentService.Setup(s => s.BulkAssignAsync(request, 200))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.BulkAssignAsync(request, currentUserId: 200);

        // Assert
        result.Should().NotBeNull();
        result.FailedCount.Should().Be(2);
    }

    #endregion

    #region BulkUpdateStatusAsync Tests

    [Fact]
    public async Task BulkUpdateStatusAsync_WithValidIds_ShouldUpdateSuccessfully()
    {
        // Arrange — ConversationService 将批量状态更新委托给 IConversationStatusService
        var request = new BulkStatusRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            Status = 3 // Closed
        };

        var expected = new BulkOperationResponse
        {
            TotalCount = 2,
            SuccessCount = 2,
            FailedCount = 0
        };
        _mockStatusService.Setup(s => s.BulkUpdateStatusAsync(request))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.BulkUpdateStatusAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        _mockStatusService.Verify(s => s.BulkUpdateStatusAsync(request), Times.Once);
    }

    #endregion

    #region SaveDraftAsync / GetDraftAsync / DeleteDraftAsync Tests

    [Fact]
    public async Task GetDraftAsync_WithNoExistingDraft_ShouldReturnNull()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        // Act
        var result = await _service.GetDraftAsync(1);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteDraftAsync_WithNoExistingDraft_ShouldReturnFalse()
    {
        // Arrange
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        // Act
        var result = await _service.DeleteDraftAsync(1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetDraftsByUserIdAsync_ShouldReturnUserDrafts()
    {
        // Arrange — ConversationService 将草稿查询委托给 IConversationDraftService
        var drafts = new List<ThreadResponse>
        {
            new() { Id = 1, Body = "Draft 1" },
            new() { Id = 2, Body = "Draft 2" }
        };

        _mockDraftService.Setup(s => s.GetDraftsByUserIdAsync(100))
            .ReturnsAsync(drafts);

        // Act
        var result = await _service.GetDraftsByUserIdAsync(100);

        // Assert
        result.Should().HaveCount(2);
        _mockDraftService.Verify(s => s.GetDraftsByUserIdAsync(100), Times.Once);
    }

    #endregion

    #region GetConversationsAsync 邮箱级授权（IDOR 防护）Tests

    /// <summary>
    /// 安全缺陷：GET /api/conversations 原先不做邮箱归属校验，
    /// 非超管角色传入任意 mailboxId 即可读取他人邮箱的工单（违反"邮箱级权限隔离"）。
    /// 锁定行为：调用方指定的 mailboxId 必须与"当前用户可访问邮箱白名单"同时下推到仓储。
    /// </summary>
    [Fact]
    public async Task GetConversationsAsync_WithRestrictedUser_ShouldPushAccessibleMailboxIdsToRepository()
    {
        // Arrange：非超管，仅可访问邮箱 1
        _mockCurrentUserAccessor.Setup(a => a.IsSuperAdmin).Returns(false);
        _mockCurrentUserAccessor.Setup(a => a.GetAccessibleMailboxIdsAsync()).ReturnsAsync(new[] { 1 });
        _mockUserRepo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync((User?)null);

        _mockConversationRepo
            .Setup(r => r.AdvancedSearchAsync(
                It.IsAny<string?>(), It.IsAny<ConversationStatus?>(), It.IsAny<ConversationType?>(), It.IsAny<SourceType?>(),
                It.IsAny<int?>(), It.IsAny<List<int>?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync((new List<Conversation>(), 0));

        // Act：指定一个无权限的邮箱 2
        await _service.GetConversationsAsync(
            mailboxId: 2, folderId: 0, page: 1, pageSize: 20, userId: 9);

        // Assert：必须带上白名单，仓储侧同时按 mailboxId 与白名单过滤
        _mockConversationRepo.Verify(r => r.AdvancedSearchAsync(
            It.IsAny<string?>(), It.IsAny<ConversationStatus?>(), It.IsAny<ConversationType?>(), It.IsAny<SourceType?>(),
            It.Is<int?>(mid => mid == 2),
            It.Is<List<int>?>(ids => ids != null && ids.SequenceEqual(new[] { 1 })),
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()), Times.AtLeastOnce,
            "非超管查询必须把可访问邮箱白名单下推到仓储，否则可跨邮箱枚举工单");
    }

    /// <summary>
    /// 空白名单陷阱：Repository.AdvancedSearchAsync 对"空 List"不附加过滤（等效于不限制），
    /// 因此"无任何可访问邮箱"的用户必须在此处直接返回空页，绝不可把空数组当白名单传下去。
    /// </summary>
    [Fact]
    public async Task GetConversationsAsync_WithNoAccessibleMailbox_ShouldReturnEmptyPageWithoutQueryingRepository()
    {
        // Arrange：非超管，且不隶属任何邮箱
        _mockCurrentUserAccessor.Setup(a => a.IsSuperAdmin).Returns(false);
        _mockCurrentUserAccessor.Setup(a => a.GetAccessibleMailboxIdsAsync()).ReturnsAsync(Array.Empty<int>());
        _mockUserRepo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync((User?)null);

        // Act
        var result = await _service.GetConversationsAsync(
            mailboxId: 0, folderId: 0, page: 1, pageSize: 20, userId: 9);

        // Assert：直接空页，且绝不触达仓储（避免落入"空白名单=不过滤"的越权分支）
        result.Data.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
        _mockConversationRepo.Verify(r => r.AdvancedSearchAsync(
            It.IsAny<string?>(), It.IsAny<ConversationStatus?>(), It.IsAny<ConversationType?>(), It.IsAny<SourceType?>(),
            It.IsAny<int?>(), It.IsAny<List<int>?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<bool?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never,
            "无任何可访问邮箱的用户必须直接得到空结果，不得把空数组传成'不限制'");
    }

    #endregion

    #region GetChatModeThreadsAsync Tests

    [Fact]
    public async Task GetChatModeThreadsAsync_ShouldReturnThreads()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com",
            Threads = new List<ConversationThread>
            {
                new() { Id = 1, Body = "Message 1", Type = ThreadType.Customer, State = Domain.Entities.ThreadState.Active },
                new() { Id = 2, Body = "Reply 1", Type = ThreadType.Message, State = Domain.Entities.ThreadState.Active }
            }
        };

        _mockConversationRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(conversation);

        // Act
        var result = await _service.GetChatModeThreadsAsync(1);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion
}