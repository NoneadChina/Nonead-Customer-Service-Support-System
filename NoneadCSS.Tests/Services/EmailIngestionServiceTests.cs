using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// EmailIngestionService 单元测试 — 邮件收取与解析
/// 使用 Moq 完全模拟依赖，测试核心流程：
/// - 自动回复邮件跳过
/// - 退信邮件处理
/// - 重复邮件跳过
/// - 新会话创建
/// - 已有会话回复
/// - 工作流触发隔离
/// </summary>
public class EmailIngestionServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IEmailProcessingService> _mockEmailProcessingService;
    private readonly Mock<IEmailQueueService> _mockEmailQueueService;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<IDistributedLockService> _mockDistributedLockService;
    private readonly Mock<ILogger<EmailIngestionService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly EmailIngestionService _service;

    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IFolderRepository> _mockFolderRepo;
    private readonly Mock<ISendLogRepository> _mockSendLogRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    public EmailIngestionServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockEmailProcessingService = new Mock<IEmailProcessingService>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockDistributedLockService = new Mock<IDistributedLockService>();
        var mockLockHandle = new Mock<IDistributedLockHandle>();
        mockLockHandle.Setup(h => h.LockKey).Returns("test-lock");
        _mockDistributedLockService.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(mockLockHandle.Object);
        _mockLogger = new Mock<ILogger<EmailIngestionService>>();
        _mockConfiguration = new Mock<IConfiguration>();

        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockFolderRepo = new Mock<IFolderRepository>();
        _mockSendLogRepo = new Mock<ISendLogRepository>();
        var mockSystemOptionsRepo = new Mock<ISystemOptionRepository>();
        mockSystemOptionsRepo.Setup(r => r.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Folders).Returns(_mockFolderRepo.Object);
        _mockUnitOfWork.Setup(u => u.SendLogs).Returns(_mockSendLogRepo.Object);
        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(mockSystemOptionsRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _mockConfiguration.Setup(c => c["App:FrontendUrl"]).Returns("http://localhost:5173");

        _service = new EmailIngestionService(
            _mockUnitOfWork.Object,
            _mockEmailProcessingService.Object,
            _mockEmailQueueService.Object,
            _mockWorkflowService.Object,
            _mockDistributedLockService.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            Options.Create(new EmailOptions()),
            new NoneadCSS.Application.Templates.TemplateRenderer());
    }

    #region ProcessMessageAsync - Auto Reply Tests

    [Fact]
    public async Task ProcessMessageAsync_WithAutoReply_ShouldSkipAndReturnIsAutoReply()
    {
        // Arrange
        var message = CreateMimeMessage("auto-reply@example.com", "test@example.com", "Auto Reply");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<auto-reply@example.com>",
            FromEmail = "auto-reply@example.com",
            IsAutoReply = true
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        // Act
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.IsAutoReply.Should().BeTrue();
        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region ProcessMessageAsync - Bounce Tests

    [Fact]
    public async Task ProcessMessageAsync_WithBounce_ShouldHandleBounceAndReturnIsBounce()
    {
        // Arrange
        var message = CreateMimeMessage("mailer-daemon@example.com", "test@example.com", "Mail delivery failed");
        var mailbox = CreateTestMailbox();

        var bounceInfo = new BounceInfo
        {
            OriginalMessageId = "<original@example.com>",
            RecipientEmail = "dest@example.com",
            BounceType = "Permanent"
        };

        var parseResult = new EmailParseResult
        {
            MessageId = "<bounce@example.com>",
            FromEmail = "mailer-daemon@example.com",
            IsBounce = true,
            BounceInfo = bounceInfo
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockSendLogRepo.Setup(r => r.FindByMessageIdAsync(bounceInfo.OriginalMessageId))
            .ReturnsAsync((SendLog?)null);

        _mockSendLogRepo.Setup(r => r.FindRecentByEmailAsync(bounceInfo.RecipientEmail, mailbox.Id))
            .ReturnsAsync((SendLog?)null);

        // Act
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.IsBounce.Should().BeTrue();
        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region ProcessMessageAsync - Duplicate Email Tests

    [Fact]
    public async Task ProcessMessageAsync_WithDuplicateMessageId_ShouldSkipAndReturnEmptyResult()
    {
        // Arrange
        var message = CreateMimeMessage("sender@example.com", "test@example.com", "Test Subject");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<duplicate@example.com>",
            FromEmail = "sender@example.com",
            Subject = "Test Subject",
            Date = DateTime.UtcNow
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        var existingThread = new ConversationThread { Id = 10, MessageId = "<duplicate@example.com>" };
        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<duplicate@example.com>"))
            .ReturnsAsync(existingThread);

        // Act
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        result.IsAutoReply.Should().BeFalse();
        result.IsBounce.Should().BeFalse();
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region ProcessMessageAsync - Missing Sender Email Tests

    [Fact]
    public async Task ProcessMessageAsync_WithEmptySenderEmail_ShouldSkipAndReturnEmptyResult()
    {
        // Arrange
        var message = CreateMimeMessage("", "test@example.com", "Test Subject");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<test@example.com>",
            FromEmail = "",
            Subject = "Test Subject",
            Date = DateTime.UtcNow
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ConversationThread?)null);

        // Act
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
    }

    #endregion

    #region ProcessMessageAsync - New Conversation Tests

    [Fact]
    public async Task ProcessMessageAsync_WithNewConversation_ShouldCreateConversationAndTriggerWorkflow()
    {
        // Arrange
        var message = CreateMimeMessage("newcustomer@example.com", "test@example.com", "New Inquiry");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<new@example.com>",
            FromEmail = "newcustomer@example.com",
            FromName = "New Customer",
            Subject = "New Inquiry",
            HtmlBody = "<p>Hello</p>",
            TextBody = "Hello",
            Date = DateTime.UtcNow,
            HasAttachments = false
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<new@example.com>"))
            .ReturnsAsync((ConversationThread?)null);

        var customer = new Customer { Id = 1, FirstName = "New" };
        customer.Emails.Add(new Email { EmailAddress = "newcustomer@example.com" });
        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("newcustomer@example.com"))
            .ReturnsAsync(customer);

        _mockEmailProcessingService.Setup(e => e.FindRelatedConversationAsync(message, mailbox.Id, _mockUnitOfWork.Object))
            .ReturnsAsync((Conversation?)null);

        var thread = new ConversationThread { Id = 1, Body = "Hello", Type = ThreadType.Customer };
        _mockEmailProcessingService.Setup(e => e.CreateThreadFromEmail(message, parseResult, It.IsAny<int>(), It.IsAny<Customer?>()))
            .Returns(thread);

        _mockFolderRepo.Setup(r => r.GetByMailboxIdAndTypeAsync(mailbox.Id, It.IsAny<int>()))
            .ReturnsAsync((Folder?)null);

        // Act
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.NewConversationCount.Should().Be(1);
        result.ReplyCount.Should().Be(0);
        _mockConversationRepo.Verify(r => r.AddAsync(It.IsAny<Conversation>()), Times.Once);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), "new_conversation"), Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_NewConversation_ShouldGetNextNumber_AndAssignToConversation()
    {
        // Arrange
        var message = CreateMimeMessage("newcustomer@example.com", "test@example.com", "New Inquiry");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<new-number@example.com>",
            FromEmail = "newcustomer@example.com",
            FromName = "New Customer",
            Subject = "New Inquiry",
            HtmlBody = "<p>Hello</p>",
            TextBody = "Hello",
            Date = DateTime.UtcNow,
            HasAttachments = false
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<new-number@example.com>"))
            .ReturnsAsync((ConversationThread?)null);

        var customer = new Customer { Id = 1, FirstName = "New" };
        customer.Emails.Add(new Email { EmailAddress = "newcustomer@example.com" });
        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("newcustomer@example.com"))
            .ReturnsAsync(customer);

        _mockEmailProcessingService.Setup(e => e.FindRelatedConversationAsync(message, mailbox.Id, _mockUnitOfWork.Object))
            .ReturnsAsync((Conversation?)null);

        var thread = new ConversationThread { Id = 1, Body = "Hello", Type = ThreadType.Customer };
        _mockEmailProcessingService.Setup(e => e.CreateThreadFromEmail(message, parseResult, It.IsAny<int>(), It.IsAny<Customer?>()))
            .Returns(thread);

        _mockFolderRepo.Setup(r => r.GetByMailboxIdAndTypeAsync(mailbox.Id, It.IsAny<int>()))
            .ReturnsAsync((Folder?)null);

        _mockConversationRepo.Setup(r => r.GetNextNumberAsync())
            .ReturnsAsync(42);

        // Act
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.NewConversationCount.Should().Be(1);
        result.ReplyCount.Should().Be(0);
        _mockConversationRepo.Verify(r => r.GetNextNumberAsync(), Times.Once);
        _mockConversationRepo.Verify(r => r.AddAsync(It.Is<Conversation>(c =>
            c.Number == 42 &&
            c.Type == ConversationType.Email &&
            c.SourceType == SourceType.Email)), Times.Once);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), "new_conversation"), Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_NewConversation_WhenGetNextNumberThrows_ShouldRollbackTransaction()
    {
        // Arrange
        var message = CreateMimeMessage("newcustomer@example.com", "test@example.com", "New Inquiry");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<new-number-throw@example.com>",
            FromEmail = "newcustomer@example.com",
            FromName = "New Customer",
            Subject = "New Inquiry",
            HtmlBody = "<p>Hello</p>",
            TextBody = "Hello",
            Date = DateTime.UtcNow,
            HasAttachments = false
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<new-number-throw@example.com>"))
            .ReturnsAsync((ConversationThread?)null);

        var customer = new Customer { Id = 1, FirstName = "New" };
        customer.Emails.Add(new Email { EmailAddress = "newcustomer@example.com" });
        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("newcustomer@example.com"))
            .ReturnsAsync(customer);

        _mockEmailProcessingService.Setup(e => e.FindRelatedConversationAsync(message, mailbox.Id, _mockUnitOfWork.Object))
            .ReturnsAsync((Conversation?)null);

        var thread = new ConversationThread { Id = 1, Body = "Hello", Type = ThreadType.Customer };
        _mockEmailProcessingService.Setup(e => e.CreateThreadFromEmail(message, parseResult, It.IsAny<int>(), It.IsAny<Customer?>()))
            .Returns(thread);

        _mockFolderRepo.Setup(r => r.GetByMailboxIdAndTypeAsync(mailbox.Id, It.IsAny<int>()))
            .ReturnsAsync((Folder?)null);

        _mockConversationRepo.Setup(r => r.GetNextNumberAsync())
            .ThrowsAsync(new InvalidOperationException("db timeout"));

        // Act
        var act = async () => await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        // ProcessMessageAsync 在 IngestConversationInTransactionAsync 的 catch 中 throw，
        // 外层 ProcessMessageAsync 本身没有 try-catch，异常会向上抛出。
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("db timeout");

        _mockConversationRepo.Verify(r => r.GetNextNumberAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
    }

    #endregion

    #region ProcessMessageAsync - Reply to Existing Conversation Tests

    [Fact]
    public async Task ProcessMessageAsync_WithReplyToExistingConversation_ShouldAddReplyAndTriggerWorkflow()
    {
        // Arrange
        var message = CreateMimeMessage("customer@example.com", "test@example.com", "Re: Existing Subject");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<reply@example.com>",
            FromEmail = "customer@example.com",
            Subject = "Re: Existing Subject",
            HtmlBody = "<p>Reply</p>",
            TextBody = "Reply",
            Date = DateTime.UtcNow,
            HasAttachments = false
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<reply@example.com>"))
            .ReturnsAsync((ConversationThread?)null);

        var customer = new Customer { Id = 1 };
        customer.Emails.Add(new Email { EmailAddress = "customer@example.com" });
        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("customer@example.com"))
            .ReturnsAsync(customer);

        var existingConversation = new Conversation
        {
            Id = 5,
            Subject = "Existing Subject",
            MailboxId = mailbox.Id,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer@example.com"
        };

        _mockEmailProcessingService.Setup(e => e.FindRelatedConversationAsync(message, mailbox.Id, _mockUnitOfWork.Object))
            .ReturnsAsync(existingConversation);

        var trackedConversation = new Conversation
        {
            Id = 5,
            Subject = "Existing Subject",
            MailboxId = mailbox.Id,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer@example.com"
        };
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(5))
            .ReturnsAsync(trackedConversation);

        var thread = new ConversationThread { Id = 2, Body = "Reply", Type = ThreadType.Customer };
        _mockEmailProcessingService.Setup(e => e.CreateThreadFromEmail(message, parseResult, It.IsAny<int>(), It.IsAny<Customer?>()))
            .Returns(thread);

        // Act
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(1);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Once);
        _mockConversationRepo.Verify(r => r.AddAsync(It.IsAny<Conversation>()), Times.Never);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(5, "new_reply"), Times.Once);
    }

    #endregion

    #region ProcessMessageAsync - Reply Reactivates Closed Conversation

    [Fact]
    public async Task ProcessMessageAsync_WithReplyToClosedConversation_ShouldReactivateToActive()
    {
        // Arrange
        var message = CreateMimeMessage("customer@example.com", "test@example.com", "Re: Closed Subject");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<reactivate@example.com>",
            FromEmail = "customer@example.com",
            Subject = "Re: Closed Subject",
            HtmlBody = "<p>Reopen</p>",
            TextBody = "Reopen",
            Date = DateTime.UtcNow,
            HasAttachments = false
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<reactivate@example.com>"))
            .ReturnsAsync((ConversationThread?)null);

        var customer = new Customer { Id = 1 };
        customer.Emails.Add(new Email { EmailAddress = "customer@example.com" });
        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("customer@example.com"))
            .ReturnsAsync(customer);

        var existingConversation = new Conversation
        {
            Id = 10,
            Subject = "Closed Subject",
            MailboxId = mailbox.Id,
            Status = ConversationStatus.Closed,
            CustomerEmail = "customer@example.com"
        };

        _mockEmailProcessingService.Setup(e => e.FindRelatedConversationAsync(message, mailbox.Id, _mockUnitOfWork.Object))
            .ReturnsAsync(existingConversation);

        var trackedConversation = new Conversation
        {
            Id = 10,
            Subject = "Closed Subject",
            MailboxId = mailbox.Id,
            Status = ConversationStatus.Closed,
            CustomerEmail = "customer@example.com"
        };
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(10))
            .ReturnsAsync(trackedConversation);

        var thread = new ConversationThread { Id = 3, Body = "Reopen", Type = ThreadType.Customer };
        _mockEmailProcessingService.Setup(e => e.CreateThreadFromEmail(message, parseResult, It.IsAny<int>(), It.IsAny<Customer?>()))
            .Returns(thread);

        // Act
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.ReplyCount.Should().Be(1);
        trackedConversation.Status.Should().Be(ConversationStatus.Active);
    }

    #endregion

    #region ProcessMessageAsync - Customer Creation Fails

    [Fact]
    public async Task ProcessMessageAsync_WhenCustomerCreationFails_ShouldThrowAndRollback()
    {
        // Arrange
        var message = CreateMimeMessage("unknown@example.com", "test@example.com", "Test");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<unknown@example.com>",
            FromEmail = "unknown@example.com",
            Subject = "Test",
            Date = DateTime.UtcNow
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<unknown@example.com>"))
            .ReturnsAsync((ConversationThread?)null);

        // 仓储契约：GetOrCreateByEmailAsync 不应返回 null（成功返回 Customer，失败抛异常）。
        // 此处模拟仓储契约被破坏的极端情况，验证服务层会抛 InvalidOperationException
        // 而非静默吞掉错误伪装为"重复邮件跳过"，避免上游统计 NewConversationCount/ReplyCount 数据丢失。
        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("unknown@example.com"))
            .ReturnsAsync((Customer?)null);

        // Act & Assert
        var act = async () => await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*GetOrCreateByEmailAsync 返回 null*");

        // 验证事务已通过 SafeRollbackAsync 安全回滚（不再静默返回空结果）
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
    }

    #endregion

    #region ProcessMessageAsync - Workflow Failure Isolation

    [Fact]
    public async Task ProcessMessageAsync_WhenWorkflowFails_ShouldNotAffectIngestionResult()
    {
        // Arrange
        var message = CreateMimeMessage("newcustomer@example.com", "test@example.com", "New Inquiry");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<workflow-fail@example.com>",
            FromEmail = "newcustomer@example.com",
            Subject = "New Inquiry",
            HtmlBody = "<p>Hello</p>",
            TextBody = "Hello",
            Date = DateTime.UtcNow,
            HasAttachments = false
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<workflow-fail@example.com>"))
            .ReturnsAsync((ConversationThread?)null);

        var customer = new Customer { Id = 1 };
        customer.Emails.Add(new Email { EmailAddress = "newcustomer@example.com" });
        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("newcustomer@example.com"))
            .ReturnsAsync(customer);

        _mockEmailProcessingService.Setup(e => e.FindRelatedConversationAsync(message, mailbox.Id, _mockUnitOfWork.Object))
            .ReturnsAsync((Conversation?)null);

        var thread = new ConversationThread { Id = 1, Body = "Hello", Type = ThreadType.Customer };
        _mockEmailProcessingService.Setup(e => e.CreateThreadFromEmail(message, parseResult, It.IsAny<int>(), It.IsAny<Customer?>()))
            .Returns(thread);

        _mockFolderRepo.Setup(r => r.GetByMailboxIdAndTypeAsync(mailbox.Id, It.IsAny<int>()))
            .ReturnsAsync((Folder?)null);

        // 工作流抛异常
        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), "new_conversation"))
            .ThrowsAsync(new InvalidOperationException("Workflow execution failed"));

        // Act — 工作流失败不应影响邮件收取结果
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.NewConversationCount.Should().Be(1);
        result.ReplyCount.Should().Be(0);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenNewReplyWorkflowFails_ShouldNotAffectIngestionResult()
    {
        // Arrange — 回复已有会话时 new_reply 工作流抛异常，不应影响邮件收取
        var message = CreateMimeMessage("customer@example.com", "test@example.com", "Re: Existing Subject");
        var mailbox = CreateTestMailbox();

        var parseResult = new EmailParseResult
        {
            MessageId = "<reply-workflow-fail@example.com>",
            FromEmail = "customer@example.com",
            Subject = "Re: Existing Subject",
            HtmlBody = "<p>Reply</p>",
            TextBody = "Reply",
            Date = DateTime.UtcNow,
            HasAttachments = false
        };

        _mockEmailProcessingService.Setup(e => e.ParseEmail(message, mailbox.Id))
            .Returns(parseResult);

        _mockThreadRepo.Setup(r => r.FindByMessageIdAsync("<reply-workflow-fail@example.com>"))
            .ReturnsAsync((ConversationThread?)null);

        var customer = new Customer { Id = 1 };
        customer.Emails.Add(new Email { EmailAddress = "customer@example.com" });
        _mockCustomerRepo.Setup(r => r.GetOrCreateByEmailAsync("customer@example.com"))
            .ReturnsAsync(customer);

        var existingConversation = new Conversation
        {
            Id = 7,
            Subject = "Existing Subject",
            MailboxId = mailbox.Id,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer@example.com"
        };

        _mockEmailProcessingService.Setup(e => e.FindRelatedConversationAsync(message, mailbox.Id, _mockUnitOfWork.Object))
            .ReturnsAsync(existingConversation);

        var trackedConversation = new Conversation
        {
            Id = 7,
            Subject = "Existing Subject",
            MailboxId = mailbox.Id,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer@example.com"
        };
        _mockConversationRepo.Setup(r => r.GetByIdTrackedAsync(7))
            .ReturnsAsync(trackedConversation);

        var thread = new ConversationThread { Id = 4, Body = "Reply", Type = ThreadType.Customer };
        _mockEmailProcessingService.Setup(e => e.CreateThreadFromEmail(message, parseResult, It.IsAny<int>(), It.IsAny<Customer?>()))
            .Returns(thread);

        // new_reply 工作流抛异常
        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(7, "new_reply"))
            .ThrowsAsync(new InvalidOperationException("Workflow execution failed"));

        // Act — 工作流失败不应影响回复收取结果
        var result = await _service.ProcessMessageAsync(message, mailbox, "/tmp/attachments");

        // Assert
        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(1);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithMissingFrontendUrl_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(c => c["App:FrontendUrl"]).Returns((string?)null);

        // Act & Assert
        var act = () => new EmailIngestionService(
            _mockUnitOfWork.Object,
            _mockEmailProcessingService.Object,
            _mockEmailQueueService.Object,
            _mockWorkflowService.Object,
            _mockDistributedLockService.Object,
            _mockLogger.Object,
            mockConfig.Object,
            Options.Create(new EmailOptions()),
            new NoneadCSS.Application.Templates.TemplateRenderer());

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*App:FrontendUrl*");
    }

    #endregion

    #region Helper Methods

    private static Mailbox CreateTestMailbox()
    {
        return new Mailbox
        {
            Id = 1,
            Email = "test@example.com",
            Name = "Test Mailbox"
        };
    }

    private static MimeMessage CreateMimeMessage(string from, string to, string subject)
    {
        var message = new MimeMessage();
        if (!string.IsNullOrEmpty(from))
            message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = "Test body" };
        return message;
    }

    #endregion
}
