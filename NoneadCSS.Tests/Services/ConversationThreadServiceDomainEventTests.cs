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
/// ConversationThreadService 领域事件发布单元测试。
/// 验证坐席回复（非备注）保存后发布 UserRepliedEvent，由 UserRepliedListener 给关注者发站内通知
/// （「给关注者发通知」此前没有等价实现，GetConversationFollowersAsync 的唯一调用点就是该监听器）。
/// 备注（IsNote=true）不算回复，不发布该事件。
/// </summary>
public class ConversationThreadServiceDomainEventTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IEmailQueueService> _mockEmailQueueService;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<IMailTemplateService> _mockMailTemplateService;
    private readonly Mock<IEmailSignatureService> _mockEmailSignatureService;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<IWeChatClient> _mockWeChatClient;
    private readonly Mock<IDingDingClient> _mockDingDingClient;
    private readonly Mock<IDingDingSettingsProvider> _mockDingDingSettingsProvider;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IFrontendUrlResolver> _mockFrontendUrlResolver;
    private readonly Mock<ILogger<ConversationThreadService>> _mockLogger;
    private readonly Mock<IDomainEventDispatcher> _mockDispatcher = new();

    private readonly Mock<IConversationRepository> _mockConvRepo = new();
    private readonly Mock<IMailboxRepository> _mockMailboxRepo = new();
    private readonly Mock<IThreadRepository> _mockThreadRepo = new();
    private readonly Mock<IUserRepository> _mockUserRepo = new();
    private readonly Mock<ICustomerRepository> _mockCustomerRepo = new();
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo = new();
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction = new();

    private readonly ConversationThreadService _service;

    public ConversationThreadServiceDomainEventTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockMailTemplateService = new Mock<IMailTemplateService>();
        _mockEmailSignatureService = new Mock<IEmailSignatureService>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockWeChatClient = new Mock<IWeChatClient>();
        _mockDingDingClient = new Mock<IDingDingClient>();
        _mockDingDingSettingsProvider = new Mock<IDingDingSettingsProvider>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockFrontendUrlResolver = new Mock<IFrontendUrlResolver>();
        _mockFrontendUrlResolver.Setup(r => r.GetFrontendUrl()).Returns("https://app.example.com");
        _mockLogger = new Mock<ILogger<ConversationThreadService>>();

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConvRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);

        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockTransaction.Setup(t => t.CommitAsync()).Returns(Task.CompletedTask);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>());
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(It.IsAny<int>()))
            .ReturnsAsync((ConversationThread?)null);
        _mockMailTemplateService.Setup(m => m.GetBySlugAsync(It.IsAny<string>()))
            .ReturnsAsync((NoneadCSS.Application.DTOs.Responses.MailTemplateResponse?)null);
        _mockEmailSignatureService.Setup(s => s.GetDefaultAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync((EmailSignatureDto?)null);

        // 邮箱返回 null：跳过整段邮件入队，只观察回复保存与领域事件发布
        _mockMailboxRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((Mailbox?)null);
        _mockCustomerRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((Customer?)null);

        _mockNotificationPublisher.Setup(n => n.NotifyMailboxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);
        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(0);

        _mockConfiguration.Setup(c => c["Attachment:UploadPath"]).Returns("Data/Attachments");

        _service = new ConversationThreadService(
            _mockUnitOfWork.Object,
            _mockEmailQueueService.Object,
            _mockNotificationPublisher.Object,
            _mockMailTemplateService.Object,
            _mockEmailSignatureService.Object,
            _mockWorkflowService.Object,
            _mockWeChatClient.Object,
            _mockDingDingClient.Object,
            _mockDingDingSettingsProvider.Object,
            _mockConfiguration.Object,
            _mockFrontendUrlResolver.Object,
            _mockLogger.Object,
            new NoneadCSS.Application.Templates.TemplateRenderer(),
            Microsoft.Extensions.Options.Options.Create(new NoneadCSS.Application.Configuration.BrandOptions()),
            new Mock<Microsoft.Extensions.Hosting.IHostEnvironment>().Object,
            webhookService: null,
            domainEventDispatcher: _mockDispatcher.Object);
    }

    private Conversation SetupEmailConversation()
    {
        var conversation = new Conversation
        {
            Id = 10,
            Number = 10,
            Subject = "Hello",
            CustomerEmail = "cust@example.com",
            MailboxId = 1,
            SourceType = SourceType.Email,
            Status = ConversationStatus.Active
        };
        _mockConvRepo.Setup(r => r.GetByIdForUpdateAsync(It.IsAny<int>())).ReturnsAsync(conversation);
        return conversation;
    }

    private User SetupAgent(int userId)
    {
        var agent = new User { Id = userId, Email = $"agent{userId}@example.com", FirstName = "张", LastName = "三", Status = UserStatus.Active };
        _mockUserRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(agent);
        return agent;
    }

    private static CreateThreadRequest BuildRequest(bool isNote) => new()
    {
        ConversationId = 10,
        Body = "<p>已处理，请查收。</p>",
        From = "support@example.com",
        IsNote = isNote
    };

    private void CaptureDispatchedEvents(List<object> sink)
    {
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<UserRepliedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserRepliedEvent, CancellationToken>((e, _) => sink.Add(e))
            .Returns(Task.CompletedTask);
    }

    [Fact(DisplayName = "坐席回复保存后发布 UserRepliedEvent")]
    public async Task AddReply_AgentReply_PublishesUserRepliedEvent()
    {
        var conversation = SetupEmailConversation();
        var agent = SetupAgent(7);
        var dispatched = new List<object>();
        CaptureDispatchedEvents(dispatched);

        var response = await _service.AddReplyAsync(BuildRequest(isNote: false), userId: 7);

        response.Should().NotBeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);

        dispatched.Should().ContainSingle();
        var evt = (UserRepliedEvent)dispatched[0];
        evt.Conversation.Should().BeSameAs(conversation);
        evt.User.Should().BeSameAs(agent);
        evt.Thread.Should().NotBeNull();
        evt.Thread.ConversationId.Should().Be(10);
        evt.Thread.UserId.Should().Be(7);
    }

    [Fact(DisplayName = "内部备注不发布 UserRepliedEvent")]
    public async Task AddReply_Note_DoesNotPublishUserRepliedEvent()
    {
        SetupEmailConversation();
        SetupAgent(7);
        var dispatched = new List<object>();
        CaptureDispatchedEvents(dispatched);

        var response = await _service.AddReplyAsync(BuildRequest(isNote: true), userId: 7);

        response.Should().NotBeNull();
        dispatched.Should().BeEmpty();
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<UserRepliedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "回复者无法解析时不发布 UserRepliedEvent")]
    public async Task AddReply_ReplyingUserUnresolvable_DoesNotPublishUserRepliedEvent()
    {
        SetupEmailConversation();
        // Users.GetByIdAsync 返回 null（未 Setup）：thread.User 为空，事件缺少回复者，跳过发布
        _mockUserRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((User?)null);
        var dispatched = new List<object>();
        CaptureDispatchedEvents(dispatched);

        var response = await _service.AddReplyAsync(BuildRequest(isNote: false), userId: 7);

        response.Should().NotBeNull();
        dispatched.Should().BeEmpty();
    }
}
