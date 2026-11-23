using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
/// ChatService 单元测试：补齐在线聊天模块的核心行为验证（消息发送、撤回、
/// 编辑、已读、评分、发起会话、关闭会话、权限校验等）。
/// </summary>
public class ChatServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUser;
    private readonly Mock<INotificationHubService> _mockHub;
    private readonly Mock<IWorkflowService> _mockWorkflow;
    private readonly Mock<NoneadCSS.Domain.Events.INotificationPublisher> _mockPublisher;
    private readonly Mock<IMailTemplateService> _mockMailTemplate;
    private readonly Mock<IEmailQueueService> _mockEmailQueue;
    private readonly Mock<ISatisfactionTokenGenerator> _mockTokenGen;
    private readonly Mock<IFrontendUrlResolver> _mockFrontendUrl;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IChatSessionRepository> _mockSessions;
    private readonly Mock<IChatMessageRepository> _mockMessages;
    private readonly Mock<IUserRepository> _mockUsers;
    private readonly Mock<IConversationRepository> _mockConversations;
    private readonly Mock<ICustomerRepository> _mockCustomers;
    private readonly Mock<IAttachmentRepository> _mockAttachments;
    private readonly Mock<ISystemOptionRepository> _mockSystemOptions;
    private readonly Mock<ILogger<ChatService>> _mockLogger;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    private readonly ChatService _chatService;

    public ChatServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockCurrentUser = new Mock<ICurrentUserAccessor>();
        _mockHub = new Mock<INotificationHubService>();
        _mockWorkflow = new Mock<IWorkflowService>();
        _mockPublisher = new Mock<NoneadCSS.Domain.Events.INotificationPublisher>();
        _mockMailTemplate = new Mock<IMailTemplateService>();
        _mockEmailQueue = new Mock<IEmailQueueService>();
        _mockTokenGen = new Mock<ISatisfactionTokenGenerator>();
        _mockFrontendUrl = new Mock<IFrontendUrlResolver>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockSessions = new Mock<IChatSessionRepository>();
        _mockMessages = new Mock<IChatMessageRepository>();
        _mockUsers = new Mock<IUserRepository>();
        _mockConversations = new Mock<IConversationRepository>();
        _mockCustomers = new Mock<ICustomerRepository>();
        _mockAttachments = new Mock<IAttachmentRepository>();
        _mockSystemOptions = new Mock<ISystemOptionRepository>();
        _mockLogger = new Mock<ILogger<ChatService>>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.ChatSessions).Returns(_mockSessions.Object);
        _mockUnitOfWork.Setup(u => u.ChatMessages).Returns(_mockMessages.Object);
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUsers.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversations.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomers.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachments.Object);
        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptions.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        // 默认当前用户为超管：便于大多数测试不用再写权限 setup
        _mockCurrentUser.Setup(u => u.IsSuperAdmin).Returns(true);
        _mockCurrentUser.Setup(u => u.UserId).Returns(1);
        _mockCurrentUser
            .Setup(u => u.CanAccessMailboxAsync(It.IsAny<int?>()))
            .ReturnsAsync(true);
        _mockCurrentUser
            .Setup(u => u.GetAccessibleMailboxIdsAsync())
            .ReturnsAsync(new[] { 1 });

        // 默认满意度/邮件/URL：返回固定值，避免真关测试 NRE
        _mockTokenGen.Setup(x => x.GenerateToken(It.IsAny<int>(), It.IsAny<string>())).Returns("FAKE-TOKEN");
        _mockTokenGen.Setup(x => x.GenerateTokenByChannelId(It.IsAny<int>(), It.IsAny<string>())).Returns("FAKE-TOKEN-CHANNEL");
        _mockFrontendUrl.Setup(x => x.GetFrontendUrl()).Returns("https://app.example.com");
        _mockMailTemplate.Setup(x => x.GetBySlugAsync(It.IsAny<string>()))
            .ReturnsAsync((MailTemplateResponse?)null);

        _chatService = new ChatService(
            _mockUnitOfWork.Object,
            _mockCurrentUser.Object,
            _mockHub.Object,
            _mockWorkflow.Object,
            _mockPublisher.Object,
            _mockMailTemplate.Object,
            _mockEmailQueue.Object,
            _mockTokenGen.Object,
            _mockFrontendUrl.Object,
            _mockConfiguration.Object,
            _mockLogger.Object,
            Options.Create(new NoneadCSS.Application.Configuration.ChatOptions()));
    }

    // ================================================================
    // 1. 客户发起会话（InitiateChatAsync）
    // ================================================================
    [Fact]
    public async Task InitiateChatAsync_Should_CreateWaitingSession_And_SendWelcomeMessage()
    {
        // Arrange
        var request = new InitiateChatRequest
        {
            MailboxId = 1,
            CustomerName = "小明",
            CustomerEmail = "xiaoming@example.com",
            CustomerPhone = "13800000000",
            Subject = "关于订单的问题",
            InitialMessage = "我的订单怎么还没发货？"
        };

        ChatSession? addedSession = null;
        ChatMessage? addedWelcome = null;
        ChatMessage? addedInitial = null;
        _mockSessions.Setup(s => s.AddAsync(It.IsAny<ChatSession>()))
            .Callback<ChatSession>(s =>
            {
                s.Id = 42;     // 模拟数据库 SaveChanges 后自增主键赋值
                addedSession = s;
            })
            .Returns(Task.CompletedTask);
        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Callback<ChatMessage>(m =>
            {
                m.Id = addedWelcome == null ? 101 : 102;
                if (addedWelcome == null) addedWelcome = m;
                else addedInitial = m;
            })
            .Returns(Task.CompletedTask);
        _mockSessions.Setup(s => s.GetByIdAsync(42))
            .ReturnsAsync((ChatSession?)null);  // 让 ToDto 使用 session 对象本身
        _mockMessages.Setup(m => m.CountAsync(42, null, null))
            .ReturnsAsync(2);

        // Act
        var resp = await _chatService.InitiateChatAsync(request, "1.2.3.4", "TestUA");

        // Assert
        resp.Should().NotBeNull();
        resp.Session.Should().NotBeNull();
        resp.Session.Status.Should().Be("waiting");
        resp.Session.CustomerName.Should().Be("小明");
        resp.Session.MailboxId.Should().Be(1);
        resp.WelcomeMessage.Should().NotBeNull();
        resp.WelcomeMessage!.SenderType.Should().Be("system");
        resp.WelcomeMessage.Content.Should().Contain("小明");

        addedSession.Should().NotBeNull();
        addedSession!.Subject.Should().Be("关于订单的问题");
        addedSession.CustomerIp.Should().Be("1.2.3.4");
        addedSession.CustomerUserAgent.Should().Be("TestUA");
        addedSession.AgentUnreadCount.Should().Be(1); // 客户首条消息计入未读

        addedInitial.Should().NotBeNull();
        addedInitial!.SenderType.Should().Be("customer");
        addedInitial.Content.Should().Be("我的订单怎么还没发货？");

        _mockHub.Verify(h => h.SendToAll("RealtimeChat", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task InitiateChatAsync_Should_DefaultEmptyNameToGuest_And_NoInitialMessage()
    {
        // Arrange
        _mockSessions.Setup(s => s.AddAsync(It.IsAny<ChatSession>()))
            .Callback<ChatSession>(s => s.Id = 11)
            .Returns(Task.CompletedTask);
        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Callback<ChatMessage>(m => m.Id = 1)
            .Returns(Task.CompletedTask);
        _mockMessages.Setup(m => m.CountAsync(11, null, null)).ReturnsAsync(1);

        // Act
        var resp = await _chatService.InitiateChatAsync(new InitiateChatRequest
        {
            MailboxId = 2,
            CustomerName = "",
            CustomerEmail = "default@example.com",
            CustomerPhone = "13900000000",
            Subject = " ",
            InitialMessage = ""
        });

        // Assert
        resp.Session.CustomerName.Should().Be("访客");
        _mockMessages.Verify(m => m.AddAsync(It.IsAny<ChatMessage>()), Times.Once); // 仅欢迎消息
    }

    // ================================================================
    // 2. 客服发送消息（SendMessageAsync）
    // ================================================================
    [Fact]
    public async Task SendMessageAsync_Should_Persist_And_UpdateSessionUnread_And_PushRealtime()
    {
        // Arrange
        var session = new ChatSession
        {
            Id = 42,
            MailboxId = 1,
            UserId = 5,
            Status = "waiting",
            CustomerUnreadCount = 0
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(42)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(42))
            .ReturnsAsync(() =>
            {
                var dto = new ChatSession { Id = session.Id, Status = session.Status };
                var msgs = new List<ChatMessage>();
                foreach (var m in session.Messages) msgs.Add(m);
                dto.Messages = msgs;
                return dto;
            });
        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Callback<ChatMessage>(m => { m.Id = 201; session.Messages.Add(m); })
            .Returns(Task.CompletedTask);

        // Act
        var dto = await _chatService.SendMessageAsync(
            42,
            new SendChatMessageRequest { Content = "您好，我来帮您查看订单", MessageType = "text" },
            userId: 5,
            senderName: "客服老王");

        // Assert
        dto.Should().NotBeNull();
        dto.SenderType.Should().Be("agent");
        dto.SenderId.Should().Be(5);
        dto.SenderName.Should().Be("客服老王");
        dto.Content.Should().Be("您好，我来帮您查看订单");

        session.Status.Should().Be("active");      // 等待中会话首次回复应自动变为 active
        session.CustomerUnreadCount.Should().Be(1); // 客服发消息 → 客户侧未读 +1

        // 应推送给客服本人 + 会话（包含客户连接）
        _mockHub.Verify(h => h.NotifyAgentChatAsync(5, "RealtimeChat", It.IsAny<object>()), Times.Once);
        _mockHub.Verify(h => h.SendToChatSessionAsync(42, "RealtimeChat", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_Should_Throw_When_SessionClosed()
    {
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(1)).ReturnsAsync(new ChatSession
        {
            Id = 1,
            MailboxId = 1,
            Status = "closed"
        });

        var act = () => _chatService.SendMessageAsync(
            1, new SendChatMessageRequest { Content = "你好" }, userId: 2);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("会话已关闭");
    }

    [Fact]
    public async Task SendMessageAsync_Should_Throw_When_NoPermission()
    {
        _mockCurrentUser.Setup(u => u.IsSuperAdmin).Returns(false);
        _mockCurrentUser.Setup(u => u.CanAccessMailboxAsync(9)).ReturnsAsync(false);
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(1)).ReturnsAsync(new ChatSession
        {
            Id = 1,
            MailboxId = 9,
            Status = "active"
        });

        var act = () => _chatService.SendMessageAsync(
            1, new SendChatMessageRequest { Content = "hi" }, userId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("无权访问该会话");
    }

    [Fact]
    public async Task SendMessageAsync_Should_Throw_When_SessionNotExist()
    {
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(999)).ReturnsAsync((ChatSession?)null);

        var act = () => _chatService.SendMessageAsync(
            999, new SendChatMessageRequest { Content = "hi" }, userId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("会话不存在");
    }

    // ================================================================
    // 3. 撤回与编辑消息（RecallMessageAsync / EditMessageAsync）
    // ================================================================
    [Fact]
    public async Task RecallMessageAsync_Should_Succeed_ForOwnRecentAgentMessage()
    {
        // Arrange
        var message = new ChatMessage
        {
            Id = 201,
            SessionId = 42,
            SenderType = "agent",
            SenderId = 5,
            Content = "原来我打错了",
            CreatedAt = DateTime.UtcNow.AddSeconds(-30)
        };
        _mockMessages.Setup(m => m.GetByIdTrackedAsync(201)).ReturnsAsync(message);
        _mockSessions.Setup(s => s.GetByIdAsync(42)).ReturnsAsync(new ChatSession
        {
            Id = 42,
            UserId = 5,
            MailboxId = 1
        });

        // Act
        var dto = await _chatService.RecallMessageAsync(42, 201, userId: 5);

        // Assert
        dto.IsRecalled.Should().BeTrue();
        dto.RecalledAt.Should().NotBeNull();
        _mockHub.Verify(h => h.SendToChatSessionAsync(42, "ChatMessageRecalled", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task RecallMessageAsync_Should_Throw_When_NotSender()
    {
        var message = new ChatMessage
        {
            Id = 201,
            SessionId = 42,
            SenderType = "agent",
            SenderId = 5,
            CreatedAt = DateTime.UtcNow
        };
        _mockMessages.Setup(m => m.GetByIdTrackedAsync(201)).ReturnsAsync(message);

        var act = () => _chatService.RecallMessageAsync(42, 201, userId: 999);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("只能撤回自己发送的消息");
    }

    [Fact]
    public async Task RecallMessageAsync_Should_Throw_When_OlderThan2Minutes()
    {
        var message = new ChatMessage
        {
            Id = 201,
            SessionId = 42,
            SenderType = "agent",
            SenderId = 5,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _mockMessages.Setup(m => m.GetByIdTrackedAsync(201)).ReturnsAsync(message);

        var act = () => _chatService.RecallMessageAsync(42, 201, userId: 5);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("消息发送超过2分钟，无法撤回");
    }

    [Fact]
    public async Task EditMessageAsync_Should_UpdateContent_And_SetEditedAt()
    {
        var message = new ChatMessage
        {
            Id = 201,
            SessionId = 42,
            SenderType = "agent",
            SenderId = 5,
            Content = "旧内容",
            CreatedAt = DateTime.UtcNow
        };
        _mockMessages.Setup(m => m.GetByIdTrackedAsync(201)).ReturnsAsync(message);
        _mockSessions.Setup(s => s.GetByIdAsync(42)).ReturnsAsync(new ChatSession
        {
            Id = 42,
            UserId = 5,
            MailboxId = 1
        });

        var dto = await _chatService.EditMessageAsync(42, 201, "  新内容  ", userId: 5);

        dto.Content.Should().Be("新内容");
        dto.EditedAt.Should().NotBeNull();
        message.Content.Should().Be("新内容");
    }

    [Fact]
    public async Task EditMessageAsync_Should_Throw_When_ContentEmpty()
    {
        var message = new ChatMessage
        {
            Id = 201,
            SessionId = 42,
            SenderType = "agent",
            SenderId = 5,
            CreatedAt = DateTime.UtcNow
        };
        _mockMessages.Setup(m => m.GetByIdTrackedAsync(201)).ReturnsAsync(message);

        var act = () => _chatService.EditMessageAsync(42, 201, "   ", userId: 5);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("消息内容不能为空");
    }

    // ================================================================
    // 4. 已读回执与未读计数（MarkAsReadAsync / GetUnreadCountAsync）
    // ================================================================
    [Fact]
    public async Task MarkAsReadAsync_Should_ResetCustomerUnread_When_ReaderIsCustomer()
    {
        var session = new ChatSession
        {
            Id = 42,
            UserId = 5,
            CustomerUnreadCount = 3,
            AgentUnreadCount = 2
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(42)).ReturnsAsync(session);
        _mockMessages.Setup(m => m.MarkAsReadAsync(42, "customer"))
            .Returns(Task.CompletedTask);

        await _chatService.MarkAsReadAsync(42, "customer");

        session.CustomerUnreadCount.Should().Be(0);
        session.CustomerLastSeenAt.Should().NotBeNull();
        session.AgentUnreadCount.Should().Be(2); // 不应改动
        _mockMessages.Verify(m => m.MarkAsReadAsync(42, "customer"), Times.Once);
    }

    [Fact]
    public async Task MarkAsReadAsync_Should_ResetAgentUnread_When_ReaderIsAgent()
    {
        var session = new ChatSession
        {
            Id = 42,
            UserId = 5,
            CustomerUnreadCount = 7,
            AgentUnreadCount = 9
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(42)).ReturnsAsync(session);
        _mockMessages.Setup(m => m.MarkAsReadAsync(42, "agent")).Returns(Task.CompletedTask);

        await _chatService.MarkAsReadAsync(42, "agent");

        session.AgentUnreadCount.Should().Be(0);
        session.AgentLastSeenAt.Should().NotBeNull();
        session.CustomerUnreadCount.Should().Be(7);
    }

    [Fact]
    public async Task GetUnreadCountAsync_Should_DelegateToRepository()
    {
        _mockMessages.Setup(m => m.CountUnreadAsync(42, "customer")).ReturnsAsync(3);

        var count = await _chatService.GetUnreadCountAsync(42, "customer");

        count.Should().Be(3);
    }

    // ================================================================
    // 5. 分配 / 关闭会话（AssignSessionAsync / CloseSessionAsync）
    // ================================================================
    [Fact]
    public async Task AssignSessionAsync_Should_SetUserId_And_WriteSystemMessage()
    {
        var session = new ChatSession { Id = 42, MailboxId = 1, Status = "waiting", AgentUnreadCount = 5 };
        var user = new User { Id = 7, FirstName = "A", LastName = "B", Status = UserStatus.Active };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(42)).ReturnsAsync(session);
        _mockUsers.Setup(u => u.GetByIdAsync(7)).ReturnsAsync(user);
        _mockSessions.Setup(s => s.GetByIdAsync(42))
            .ReturnsAsync(() => session);
        _mockMessages.Setup(m => m.CountAsync(42, null, null)).ReturnsAsync(0);

        var dto = await _chatService.AssignSessionAsync(42, userId: 7);

        session.UserId.Should().Be(7);
        session.Status.Should().Be("active");
        session.AgentUnreadCount.Should().Be(0);
        _mockMessages.Verify(m => m.AddAsync(It.Is<ChatMessage>(
            mm => mm.SenderType == "system" && mm.Content.Contains("A B"))), Times.Once);
    }

    [Fact]
    public async Task CloseSessionAsync_Should_SetStatusClosed_And_InsertSystemMessage()
    {
        // 两阶段关闭（MR-4）：客服主动调用 CloseSessionAsync → 阶段 1: awaiting_rating，并触发 chat_close_requested 工作流。
        // 真正写"会话已关闭"system message、Status=closed、ClosedAt 由阶段 2 ReallyCloseChatSessionAsync 完成。
        var session = new ChatSession { Id = 42, MailboxId = 1, Status = "active" };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(42)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(42)).ReturnsAsync(() => session);
        _mockMessages.Setup(m => m.CountAsync(42, null, null)).ReturnsAsync(0);

        await _chatService.CloseSessionAsync(42);

        // 阶段 1：状态 & 时间戳设置
        session.Status.Should().Be("awaiting_rating");
        session.RatingRequestedAt.Should().NotBeNull();
        session.RatingTimeoutAt.Should().NotBeNull();
        session.ClosedAt.Should().BeNull();

        // 阶段 1：触发 [聊天]关闭满意度消息 工作流（chat_close_requested）
        _mockWorkflow.Verify(w => w.ExecuteForChatSessionAsync(
            42, WorkflowConst.TriggerChatCloseRequested, null, null), Times.Once);

        // 真关（阶段 2）：ReallyCloseChatSessionAsync 写 closed 状态 + 可选 system message
        await _chatService.ReallyCloseChatSessionAsync(session, "会话已关闭");
        session.Status.Should().Be("closed");
        session.ClosedAt.Should().NotBeNull();
        _mockMessages.Verify(m => m.AddAsync(It.Is<ChatMessage>(
            mm => mm.Content == "会话已关闭" && mm.SenderType == "system")), Times.Once);
    }

    // ================================================================
    // 6. 客户侧接口：SendCustomerMessage / CloseSessionByToken / RateChat
    // ================================================================
    [Fact]
    public async Task SendCustomerMessageAsync_Should_Push_ToAll_When_Unassigned()
    {
        var session = new ChatSession
        {
            Id = 11,
            SessionToken = "tok_aaa",
            CustomerName = "客户A",
            Status = "waiting",
            UserId = null,  // 未分配
            AgentUnreadCount = 0
        };
        _mockSessions.Setup(s => s.GetByTokenAsync("tok_aaa")).ReturnsAsync(session);
        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Callback<ChatMessage>(m => m.Id = 1)
            .Returns(Task.CompletedTask);

        var dto = await _chatService.SendCustomerMessageAsync("tok_aaa",
            new SendChatMessageRequest { Content = "有人吗？", MessageType = "text" });

        dto.SenderType.Should().Be("customer");
        dto.SenderName.Should().Be("客户A");
        session.AgentUnreadCount.Should().Be(1);
        // 未分配 → 推给所有在线客服
        _mockHub.Verify(h => h.SendToAll("RealtimeChat", It.IsAny<object>()), Times.Once);
        _mockHub.Verify(h => h.NotifyAgentChatAsync(It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task SendCustomerMessageAsync_Should_Push_ToAssignedAgent_When_UserIsSet()
    {
        var session = new ChatSession
        {
            Id = 11,
            SessionToken = "tok_bbb",
            CustomerName = "客户B",
            Status = "active",
            UserId = 9,
            AgentUnreadCount = 0
        };
        _mockSessions.Setup(s => s.GetByTokenAsync("tok_bbb")).ReturnsAsync(session);
        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Callback<ChatMessage>(m => m.Id = 1)
            .Returns(Task.CompletedTask);

        await _chatService.SendCustomerMessageAsync("tok_bbb",
            new SendChatMessageRequest { Content = "x" });

        session.AgentUnreadCount.Should().Be(1);
        _mockHub.Verify(h => h.NotifyAgentChatAsync(9, "RealtimeChat", It.IsAny<object>()), Times.Once);
        _mockHub.Verify(h => h.SendToAll(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task CloseSessionByTokenAsync_Should_MarkClosed_And_SendCustomerEndedMessage()
    {
        var session = new ChatSession { Id = 5, SessionToken = "tok_x", Status = "active" };
        _mockSessions.Setup(s => s.GetByTokenAsync("tok_x")).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(() => session);
        _mockMessages.Setup(m => m.CountAsync(5, null, null)).ReturnsAsync(0);

        await _chatService.CloseSessionByTokenAsync("tok_x");

        session.Status.Should().Be("closed");
        session.ClosedAt.Should().NotBeNull();
        _mockMessages.Verify(m => m.AddAsync(It.Is<ChatMessage>(
            mm => mm.Content == "客户已结束对话")), Times.Once);
    }

    [Fact]
    public async Task RateChatAsync_Should_ClampRatingBetween1And5()
    {
        var session = new ChatSession { Id = 8, SessionToken = "tok_rating", Rating = null, Status = "awaiting_rating" };
        _mockSessions.Setup(s => s.GetByTokenAsync("tok_rating")).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(8)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(8)).ReturnsAsync(() => session);
        _mockMessages.Setup(m => m.CountAsync(8, null, null)).ReturnsAsync(0);

        await _chatService.RateChatAsync("tok_rating", new RateChatRequest { Rating = 9, Comment = "非常好" });

        session.Rating.Should().Be(5);
        session.RatingComment.Should().Be("非常好");
    }

    [Fact]
    public async Task RateChatAsync_Should_ClampRating_LowerBound()
    {
        var session = new ChatSession { Id = 9, SessionToken = "tok_r2", Status = "awaiting_rating" };
        _mockSessions.Setup(s => s.GetByTokenAsync("tok_r2")).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(9)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(9)).ReturnsAsync(() => session);
        _mockMessages.Setup(m => m.CountAsync(9, null, null)).ReturnsAsync(0);

        await _chatService.RateChatAsync("tok_r2", new RateChatRequest { Rating = -3 });

        session.Rating.Should().Be(1);
    }

    // ================================================================
    // 7. 权限校验：非超管 GetSessionsAsync 的 mailboxId 限制
    // ================================================================
    [Fact]
    public async Task GetSessionsAsync_Should_ReturnEmpty_When_NoMailboxAccess_AsNonSuperAdmin()
    {
        _mockCurrentUser.Setup(u => u.IsSuperAdmin).Returns(false);
        _mockCurrentUser.Setup(u => u.GetAccessibleMailboxIdsAsync())
            .ReturnsAsync(new[] { 2 });
        // 传入了一个没有权限的 mailboxId → 返回空列表
        _mockCurrentUser.Setup(u => u.CanAccessMailboxAsync(99)).ReturnsAsync(false);

        var result = await _chatService.GetSessionsAsync(new ChatSessionQuery
        {
            MailboxId = 99,
            Page = 1,
            PageSize = 10
        });

        result.TotalItems.Should().Be(0);
        result.Data.Should().BeEmpty();
        _mockSessions.Verify(s => s.GetListAsync(
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTime?>(),
            It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    // ================================================================
    // 8. 客户侧 GetCustomerMessages / 客服侧 GetMessages
    // ================================================================
    [Fact]
    public async Task GetCustomerMessagesAsync_Should_Throw_When_TokenInvalid()
    {
        _mockSessions.Setup(s => s.GetByTokenAsync("nonexistent")).ReturnsAsync((ChatSession?)null);

        var act = () => _chatService.GetCustomerMessagesAsync("nonexistent");
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("会话不存在或已过期");
    }

    [Fact]
    public async Task GetMessagesAsync_Should_ReturnList()
    {
        var messages = new List<ChatMessage>
        {
            new() { Id = 1, SessionId = 42, SenderType = "customer", Content = "1", CreatedAt = DateTime.UtcNow },
            new() { Id = 2, SessionId = 42, SenderType = "agent", Content = "2", CreatedAt = DateTime.UtcNow.AddSeconds(1) }
        };
        _mockMessages.Setup(m => m.GetBySessionIdAsync(42, 100))
            .ReturnsAsync(messages);

        var result = await _chatService.GetMessagesAsync(42, limit: 100);

        result.Should().HaveCount(2);
        result[0].SenderType.Should().Be("customer");
        result[1].SenderType.Should().Be("agent");
    }

    // ================================================================
    // 9. 聊天转工单（TransferToTicketAsync）
    // ================================================================
    #region TransferToTicketAsync

    [Fact]
    public async Task TransferToTicketAsync_Should_GenerateUniqueNumber_ViaRepository_And_CommitTransaction()
    {
        // Arrange
        const int sessionId = 42;
        const int operatedByUserId = 5;
        var session = new ChatSession
        {
            Id = sessionId,
            MailboxId = 1,
            CustomerEmail = "customer@example.com",
            CustomerName = "测试客户",
            Subject = "原聊天主题",
            Status = "active"
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(sessionId)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(sessionId)).ReturnsAsync(() => session);

        _mockMessages.Setup(m => m.CountAsync(sessionId, null, null)).ReturnsAsync(0);
        _mockMessages.Setup(m => m.GetListAsync(
                It.Is<int?>(sid => sid == sessionId),
                null, null, null, null, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage>());

        var customer = new Customer { Id = 10 };
        customer.Emails.Add(new Email { EmailAddress = "customer@example.com" });
        _mockCustomers.Setup(c => c.GetOrCreateByEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(customer);

        _mockConversations.Setup(c => c.GetNextNumberAsync()).ReturnsAsync(20250001);

        Conversation? addedConversation = null;
        _mockConversations.Setup(c => c.AddAsync(It.IsAny<Conversation>()))
            .Callback<Conversation>(c =>
            {
                c.Id = 123;
                addedConversation = c;
            })
            .ReturnsAsync((Conversation c) => c);

        _mockAttachments.Setup(a => a.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>());

        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Returns(Task.CompletedTask);

        _mockWorkflow.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(0);

        _mockSystemOptions.Setup(s => s.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var request = new TransferToTicketRequest
        {
            Subject = "咨询产品价格"
        };

        // Act
        var result = await _chatService.TransferToTicketAsync(sessionId, request, operatedByUserId);

        // Assert
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockConversations.Verify(c => c.GetNextNumberAsync(), Times.Once);

        addedConversation.Should().NotBeNull();
        addedConversation!.Number.Should().Be(20250001);
        addedConversation.Subject.Should().Be("[聊天转工单] 咨询产品价格");
        addedConversation.CustomerId.Should().Be(10);

        session.Status.Should().Be("transferred");
        session.Conversation.Should().NotBeNull();
        session.Conversation!.Id.Should().Be(123);

        _mockPublisher.Verify(p => p.NotifyMailboxAsync(
            It.IsAny<string>(),
            "conversation_created",
            It.IsAny<object>()), Times.Once);

        _mockWorkflow.Verify(w => w.ExecuteForConversationAsync(
            123,
            WorkflowConst.TriggerNewConversation), Times.Once);
    }

    [Fact]
    public async Task TransferToTicketAsync_Should_Rollback_When_GetNextNumberThrows()
    {
        // Arrange
        const int sessionId = 42;
        var session = new ChatSession
        {
            Id = sessionId,
            MailboxId = 1,
            CustomerEmail = "customer@example.com",
            CustomerName = "测试客户",
            Subject = "原主题",
            Status = "active"
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(sessionId)).ReturnsAsync(session);

        _mockMessages.Setup(m => m.CountAsync(sessionId, null, null)).ReturnsAsync(0);
        _mockMessages.Setup(m => m.GetListAsync(
                It.Is<int?>(sid => sid == sessionId),
                null, null, null, null, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage>());

        var customer = new Customer { Id = 10 };
        customer.Emails.Add(new Email { EmailAddress = "customer@example.com" });
        _mockCustomers.Setup(c => c.GetOrCreateByEmailAsync("customer@example.com"))
            .ReturnsAsync(customer);

        _mockConversations.Setup(c => c.GetNextNumberAsync())
            .ThrowsAsync(new InvalidOperationException("db deadlock"));

        _mockTransaction.Setup(t => t.RollbackAsync()).Returns(Task.CompletedTask);

        var request = new TransferToTicketRequest();

        // Act & Assert
        var act = () => _chatService.TransferToTicketAsync(sessionId, request, operatedByUserId: 1);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("db deadlock");

        _mockTransaction.Verify(t => t.RollbackAsync(), Times.AtLeastOnce);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task TransferToTicketAsync_Should_PrefixSubject_And_UseRequestSubject_WhenNoPrefix()
    {
        // Arrange
        const int sessionId = 1;
        var session = new ChatSession
        {
            Id = sessionId,
            MailboxId = 1,
            CustomerEmail = "a@b.com",
            CustomerName = "测试",
            Subject = "原聊天主题",
            Status = "active"
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(sessionId)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(sessionId)).ReturnsAsync(() => session);

        _mockMessages.Setup(m => m.CountAsync(sessionId, null, null)).ReturnsAsync(0);
        _mockMessages.Setup(m => m.GetListAsync(
                It.Is<int?>(sid => sid == sessionId),
                null, null, null, null, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage>());

        var custA = new Customer { Id = 1 };
        custA.Emails.Add(new Email { EmailAddress = "a@b.com" });
        _mockCustomers.Setup(c => c.GetOrCreateByEmailAsync("a@b.com"))
            .ReturnsAsync(custA);

        _mockConversations.Setup(c => c.GetNextNumberAsync()).ReturnsAsync(1001);

        Conversation? captured = null;
        _mockConversations.Setup(c => c.AddAsync(It.IsAny<Conversation>()))
            .Callback<Conversation>(c => { c.Id = 10; captured = c; })
            .ReturnsAsync((Conversation c) => c);

        _mockAttachments.Setup(a => a.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>());

        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Returns(Task.CompletedTask);

        _mockWorkflow.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(0);

        _mockSystemOptions.Setup(s => s.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var request = new TransferToTicketRequest { Subject = "咨询产品价格" };

        // Act
        await _chatService.TransferToTicketAsync(sessionId, request, operatedByUserId: 1);

        // Assert
        captured.Should().NotBeNull();
        captured!.Subject.Should().Be("[聊天转工单] 咨询产品价格");
    }

    [Fact]
    public async Task TransferToTicketAsync_Should_PrefixSubject_Dedupe_WhenAlreadyPrefixed()
    {
        // Arrange
        const int sessionId = 2;
        var session = new ChatSession
        {
            Id = sessionId,
            MailboxId = 1,
            CustomerEmail = "a@b.com",
            CustomerName = "测试",
            Subject = "原聊天主题",
            Status = "active"
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(sessionId)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(sessionId)).ReturnsAsync(() => session);

        _mockMessages.Setup(m => m.CountAsync(sessionId, null, null)).ReturnsAsync(0);
        _mockMessages.Setup(m => m.GetListAsync(
                It.Is<int?>(sid => sid == sessionId),
                null, null, null, null, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage>());

        var custB = new Customer { Id = 1 };
        custB.Emails.Add(new Email { EmailAddress = "a@b.com" });
        _mockCustomers.Setup(c => c.GetOrCreateByEmailAsync("a@b.com"))
            .ReturnsAsync(custB);

        _mockConversations.Setup(c => c.GetNextNumberAsync()).ReturnsAsync(1002);

        Conversation? capturedB = null;
        _mockConversations.Setup(c => c.AddAsync(It.IsAny<Conversation>()))
            .Callback<Conversation>(c => { c.Id = 20; capturedB = c; })
            .ReturnsAsync((Conversation c) => c);

        _mockAttachments.Setup(a => a.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>());

        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Returns(Task.CompletedTask);

        _mockWorkflow.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(0);

        _mockSystemOptions.Setup(s => s.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var requestB = new TransferToTicketRequest { Subject = "[聊天转工单] 重复前缀" };

        // Act
        await _chatService.TransferToTicketAsync(sessionId, requestB, operatedByUserId: 1);

        // Assert
        capturedB.Should().NotBeNull();
        capturedB!.Subject.Should().Be("[聊天转工单] 重复前缀");
    }

    [Fact]
    public async Task TransferToTicketAsync_Should_PrefixSubject_FromSessionSubject_WhenRequestSubjectEmpty()
    {
        // Arrange
        const int sessionId = 3;
        var session = new ChatSession
        {
            Id = sessionId,
            MailboxId = 1,
            CustomerEmail = "a@b.com",
            CustomerName = "测试",
            Subject = "原聊天主题",
            Status = "active"
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(sessionId)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(sessionId)).ReturnsAsync(() => session);

        _mockMessages.Setup(m => m.CountAsync(sessionId, null, null)).ReturnsAsync(0);
        _mockMessages.Setup(m => m.GetListAsync(
                It.Is<int?>(sid => sid == sessionId),
                null, null, null, null, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage>());

        var custC = new Customer { Id = 1 };
        custC.Emails.Add(new Email { EmailAddress = "a@b.com" });
        _mockCustomers.Setup(c => c.GetOrCreateByEmailAsync("a@b.com"))
            .ReturnsAsync(custC);

        _mockConversations.Setup(c => c.GetNextNumberAsync()).ReturnsAsync(1003);

        Conversation? capturedC = null;
        _mockConversations.Setup(c => c.AddAsync(It.IsAny<Conversation>()))
            .Callback<Conversation>(c => { c.Id = 30; capturedC = c; })
            .ReturnsAsync((Conversation c) => c);

        _mockAttachments.Setup(a => a.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>());

        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Returns(Task.CompletedTask);

        _mockWorkflow.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(0);

        _mockSystemOptions.Setup(s => s.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var requestC = new TransferToTicketRequest { Subject = null };

        // Act
        await _chatService.TransferToTicketAsync(sessionId, requestC, operatedByUserId: 1);

        // Assert
        capturedC.Should().NotBeNull();
        capturedC!.Subject.Should().Be("[聊天转工单] 原聊天主题");
    }

    #endregion

    // ================================================================
    // MR-4：两阶段关闭 + 评分联动关闭 + 超时真关闭 单测
    // ================================================================
    [Fact]
    public async Task CloseSessionAsync_ActiveSession_Should_EnterAwaitingRating_And_TriggerChatCloseRequested()
    {
        // Arrange
        var session = new ChatSession
        {
            Id = 100,
            MailboxId = 1,
            Status = "active",
            CustomerEmail = "xiaoming@example.com",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(100)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(100)).ReturnsAsync(session);
        _mockMessages.Setup(m => m.CountAsync(100, null, null)).ReturnsAsync(5);
        DateTime beforeAct = DateTime.UtcNow;

        // Act
        var result = await _chatService.CloseSessionAsync(100);

        // Assert —— 阶段1：进入 awaiting_rating
        session.Status.Should().Be("awaiting_rating");
        session.RatingRequestedAt.Should().NotBeNull();
        session.RatingRequestedAt.Should().BeOnOrAfter(beforeAct);
        session.RatingTimeoutAt.Should().NotBeNull();
        session.RatingTimeoutAt.Should().Be(session.RatingRequestedAt!.Value.Add(ChatService.RatingWaitTimeout));

        // —— 触发 chat_close_requested 工作流（仅一次）
        _mockWorkflow.Verify(
            w => w.ExecuteForChatSessionAsync(
                100,
                WorkflowConst.TriggerChatCloseRequested,
                null, null),
            Times.Once);

        // —— 不应该真关闭
        session.ClosedAt.Should().BeNull();

        // —— 返回的 DTO 带新状态 & rating 时间字段
        result.Status.Should().Be("awaiting_rating");
        result.StatusLabel.Should().Be("等待评分");
        result.RatingRequestedAt.Should().Be(session.RatingRequestedAt);
        result.RatingTimeoutAt.Should().Be(session.RatingTimeoutAt);

        // —— SaveChanges 至少一次（阶段1保存状态+时间戳）
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CloseSessionAsync_AlreadyClosed_Should_Idempotent_NoStateChange_NoWorkflow()
    {
        // Arrange —— 已经 closed
        var closedAt = DateTime.UtcNow.AddMinutes(-10);
        var session = new ChatSession
        {
            Id = 200,
            MailboxId = 1,
            Status = "closed",
            ClosedAt = closedAt
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(200)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(200)).ReturnsAsync(session);
        _mockMessages.Setup(m => m.CountAsync(200, null, null)).ReturnsAsync(0);

        // Act
        await _chatService.CloseSessionAsync(200);

        // Assert
        session.Status.Should().Be("closed");
        session.ClosedAt.Should().Be(closedAt);
        session.RatingRequestedAt.Should().BeNull();
        session.RatingTimeoutAt.Should().BeNull();
        // 工作流不应被触发
        _mockWorkflow.Verify(w => w.ExecuteForChatSessionAsync(
            It.IsAny<int>(), It.IsAny<string>(), null, null), Times.Never);
    }

    [Fact]
    public async Task CloseSessionAsync_Transferred_Should_Idempotent()
    {
        // Arrange
        var session = new ChatSession { Id = 201, MailboxId = 1, Status = "transferred" };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(201)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(201)).ReturnsAsync(session);
        _mockMessages.Setup(m => m.CountAsync(201, null, null)).ReturnsAsync(0);

        // Act
        await _chatService.CloseSessionAsync(201);

        // Assert
        session.Status.Should().Be("transferred");
        session.RatingRequestedAt.Should().BeNull();
        _mockWorkflow.Verify(w => w.ExecuteForChatSessionAsync(
            It.IsAny<int>(), It.IsAny<string>(), null, null), Times.Never);
    }

    [Fact]
    public async Task CloseSessionAsync_AlreadyAwaitingRating_Should_ResetTimerToFresh3Minutes_And_NotTriggerWorkflowTwice()
    {
        // Arrange：会话当前处于 awaiting_rating（老的时间戳），客服再次点关闭
        var oldRequestedAt = DateTime.UtcNow.AddMinutes(-2);
        var oldTimeoutAt = oldRequestedAt.Add(ChatService.RatingWaitTimeout);
        var session = new ChatSession
        {
            Id = 300,
            MailboxId = 1,
            Status = "awaiting_rating",
            RatingRequestedAt = oldRequestedAt,
            RatingTimeoutAt = oldTimeoutAt
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(300)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(300)).ReturnsAsync(session);
        _mockMessages.Setup(m => m.CountAsync(300, null, null)).ReturnsAsync(0);
        var beforeAct = DateTime.UtcNow;

        // Act
        await _chatService.CloseSessionAsync(300);

        // Assert：状态仍然 awaiting_rating，但时间戳被刷新为当前时间 +3 分钟
        session.Status.Should().Be("awaiting_rating");
        session.RatingRequestedAt.Should().BeOnOrAfter(beforeAct);
        session.RatingRequestedAt.Should().NotBe(oldRequestedAt);
        session.RatingTimeoutAt.Should().Be(session.RatingRequestedAt!.Value.Add(ChatService.RatingWaitTimeout));
        session.RatingTimeoutAt.Should().NotBe(oldTimeoutAt);

        // 工作流再次触发（用户明确点了两次关闭，再发一次满意度邀请可接受）—— 当前实现不做"二次触发"跳过，所以正常触发
        _mockWorkflow.Verify(w => w.ExecuteForChatSessionAsync(
            300, WorkflowConst.TriggerChatCloseRequested, null, null), Times.Once);
    }

    [Fact]
    public async Task ReallyCloseChatSessionAsync_Should_SetClosedAt_And_AddSystemReason_And_SendEmail()
    {
        // Arrange
        var session = new ChatSession
        {
            Id = 400,
            MailboxId = 1,
            Status = "awaiting_rating",
            RatingRequestedAt = DateTime.UtcNow.AddMinutes(-2),
            RatingTimeoutAt = DateTime.UtcNow.AddMinutes(1),
            CustomerEmail = "customer@example.com",
            CustomerName = "测试客户"
        };
        var tpl = new MailTemplateResponse
        {
            Id = 20,
            Slug = "chat-session-closed",
            IsActive = true
        };
        _mockMailTemplate.Setup(m => m.GetBySlugAsync("chat-session-closed")).ReturnsAsync(tpl);
        _mockMailTemplate.Setup(m => m.RenderTemplateFullAsync(20, It.IsAny<Dictionary<string, string>>(), It.IsAny<HashSet<string>?>()))
            .ReturnsAsync((int id, Dictionary<string, string> vars, HashSet<string>? _) =>
                new NoneadCSS.Application.DTOs.Responses.RenderedTemplateResponse
                {
                    Subject = $"会话关闭通知-{vars.GetValueOrDefault("customer_name")}",
                    BodyText = $"感谢您的咨询，请点击评价：{vars.GetValueOrDefault("satisfaction_url")}",
                    BodyHtml = null!
                });
        _mockConfiguration.SetupGet(c => c["CompanyName"]).Returns("拓德测试");

        ChatMessage? capturedMsg = null;
        _mockMessages.Setup(m => m.AddAsync(It.IsAny<ChatMessage>()))
            .Callback<ChatMessage>(x => { capturedMsg = x; x.Id = 5001; })
            .Returns(Task.CompletedTask);
        var beforeAct = DateTime.UtcNow;

        // Act
        await _chatService.ReallyCloseChatSessionAsync(session, "等待客户满意度评价超时（3 分钟），会话已自动关闭。");

        // Assert —— 状态真关闭
        session.Status.Should().Be("closed");
        session.ClosedAt.Should().NotBeNull();
        session.ClosedAt.Should().BeOnOrAfter(beforeAct);

        // —— 系统消息插入
        capturedMsg.Should().NotBeNull();
        capturedMsg!.SenderType.Should().Be("system");
        capturedMsg.MessageType.Should().Be("system");
        capturedMsg.SessionId.Should().Be(400);
        capturedMsg.Content.Should().Contain("超时");
        capturedMsg.IsRead.Should().BeTrue();

        // —— 邮件模板获取+渲染+入队（customer@example.com 非空，应走到 Enqueue）
        _mockMailTemplate.Verify(m => m.GetBySlugAsync("chat-session-closed"), Times.Once);
        _mockMailTemplate.Verify(m => m.RenderTemplateFullAsync(20, It.IsAny<Dictionary<string, string>>()), Times.Once);
        _mockEmailQueue.Verify(q => q.EnqueueEmailAsync(
            /*mailboxId*/ It.IsAny<int>(),
            /*to*/ "customer@example.com",
            It.IsAny<string>(), It.IsAny<string>(), null, null, null, null, null, null, 1, null, null), Times.Once);
    }

    [Fact]
    public async Task ReallyCloseChatSessionAsync_AlreadyClosed_Should_Idempotent_And_SkipMessage_And_SkipEmail()
    {
        // Arrange：已关闭
        var closedAt = DateTime.UtcNow.AddMinutes(-5);
        var session = new ChatSession
        {
            Id = 401,
            Status = "closed",
            ClosedAt = closedAt,
            CustomerEmail = "customer2@example.com"
        };
        _mockMailTemplate.Reset();
        _mockEmailQueue.Reset();

        // Act
        await _chatService.ReallyCloseChatSessionAsync(session, "应该不会被写入");

        // Assert
        session.ClosedAt.Should().Be(closedAt);
        _mockMessages.Verify(m => m.AddAsync(It.IsAny<ChatMessage>()), Times.Never);
        _mockMailTemplate.Verify(m => m.GetBySlugAsync(It.IsAny<string>()), Times.Never);
        _mockEmailQueue.Verify(q => q.EnqueueEmailAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            null, null, null, null, null, null, It.IsAny<int>(), null, null), Times.Never);
    }

    [Fact]
    public async Task ReallyCloseChatSessionAsync_NoCustomerEmail_Should_CloseSession_But_SkipEmail()
    {
        // Arrange
        var session = new ChatSession
        {
            Id = 402,
            Status = "awaiting_rating",
            CustomerEmail = string.Empty,
            CustomerName = "无邮箱访客"
        };
        _mockMailTemplate.Reset();
        _mockEmailQueue.Reset();

        // Act
        await _chatService.ReallyCloseChatSessionAsync(session, "无邮箱，仅关闭不发邮件");

        // Assert
        session.Status.Should().Be("closed");
        session.ClosedAt.Should().NotBeNull();
        _mockMessages.Verify(m => m.AddAsync(It.IsAny<ChatMessage>()), Times.Once);
        _mockMailTemplate.Verify(m => m.GetBySlugAsync(It.IsAny<string>()), Times.Never);
        _mockEmailQueue.Verify(q => q.EnqueueEmailAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            null, null, null, null, null, null, It.IsAny<int>(), null, null), Times.Never);
    }

    [Fact]
    public async Task RateChatAsync_AwaitingRating_Should_SaveRating_TriggerSatisfactionSubmitted_ThenReallyClose_AndEmail()
    {
        // Arrange
        var token = "session-token-abc";
        var session = new ChatSession
        {
            Id = 500,
            SessionToken = token,
            MailboxId = 1,
            Status = "awaiting_rating",
            RatingRequestedAt = DateTime.UtcNow.AddMinutes(-1),
            RatingTimeoutAt = DateTime.UtcNow.AddMinutes(2),
            CustomerEmail = "rater@example.com",
            CustomerName = "评分人"
        };
        // RateChatAsync: GetByTokenAsync -> 返回 session 的不带追踪对象; 再 GetByIdTrackedAsync 返回同一个 tracked 对象
        _mockSessions.Setup(s => s.GetByTokenAsync(token)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(500)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(500)).ReturnsAsync(session);
        _mockMessages.Setup(m => m.CountAsync(500, null, null)).ReturnsAsync(10);
        // 邮箱模板：给一个 active 的，验证会入队邮件
        var tpl = new MailTemplateResponse { Id = 21, Slug = "chat-session-closed", IsActive = true };
        _mockMailTemplate.Setup(m => m.GetBySlugAsync("chat-session-closed")).ReturnsAsync(tpl);
        _mockMailTemplate.Setup(m => m.RenderTemplateFullAsync(21, It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new NoneadCSS.Application.DTOs.Responses.RenderedTemplateResponse
            { Subject = "感谢您的反馈", BodyText = "测试正文", BodyHtml = null! });

        // Act
        var result = await _chatService.RateChatAsync(token, new RateChatRequest { Rating = 5, Comment = "非常好，客服很耐心" });

        // Assert —— 评分保存
        session.Rating.Should().Be(5);
        session.RatingComment.Should().Be("非常好，客服很耐心");

        // —— 触发 chat_satisfaction_submitted 工作流（带 rating=5 & comment）
        _mockWorkflow.Verify(w => w.ExecuteForChatSessionAsync(
            500,
            WorkflowConst.TriggerChatSatisfactionSubmitted,
            5,
            "非常好，客服很耐心"), Times.Once);

        // —— 真关闭：Status=closed, ClosedAt!=null
        session.Status.Should().Be("closed");
        session.ClosedAt.Should().NotBeNull();

        // —— 返回 DTO：含评分内容
        result.Status.Should().Be("closed");
        result.Rating.Should().Be(5);
        result.RatingComment.Should().Be("非常好，客服很耐心");

        // —— 邮件入队（CustomerEmail 存在）
        _mockEmailQueue.Verify(q => q.EnqueueEmailAsync(
            It.IsAny<int>(), "rater@example.com", It.IsAny<string>(), It.IsAny<string>(),
            null, null, null, null, null, null, 1, null, It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task RateChatAsync_RegularActiveSession_Should_SaveRatingOnly_NotTriggerWorkflow_NotClose()
    {
        // Arrange：会话是 active 状态（普通评分），不应走联动关闭
        var token = "t-regular";
        var session = new ChatSession
        {
            Id = 501,
            SessionToken = token,
            MailboxId = 1,
            Status = "active",
            CustomerEmail = "user@example.com"
        };
        _mockSessions.Setup(s => s.GetByTokenAsync(token)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(501)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(501)).ReturnsAsync(session);
        _mockMessages.Setup(m => m.CountAsync(501, null, null)).ReturnsAsync(0);

        // Act
        await _chatService.RateChatAsync(token, new RateChatRequest { Rating = 3, Comment = "一般般" });

        // Assert
        session.Rating.Should().Be(3);
        session.RatingComment.Should().Be("一般般");
        session.Status.Should().Be("active");      // 不应被关闭
        session.ClosedAt.Should().BeNull();
        // chat_satisfaction_submitted 工作流应 NOT 被触发（只有 awaiting_rating→才触发）
        _mockWorkflow.Verify(w => w.ExecuteForChatSessionAsync(
            It.IsAny<int>(), WorkflowConst.TriggerChatSatisfactionSubmitted, It.IsAny<int?>(), It.IsAny<string?>()),
            Times.Never);
        // 邮件不应入队（会话未关闭）
        _mockEmailQueue.Verify(q => q.EnqueueEmailAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            null, null, null, null, null, null, It.IsAny<int>(), null, null), Times.Never);
    }

    [Fact]
    public async Task TransferToTicketAsync_ExistingCustomer_Should_OverwriteFirstAndLastName_WithWidgetSubmission()
    {
        // Arrange：老客户（DB 里有旧姓名 "张" "三"），本次 Widget 提交 CustomerName="李小明"
        var sessionId = 900;
        var session = new ChatSession
        {
            Id = sessionId,
            MailboxId = 1,
            Status = "active",
            CustomerName = "李小明",
            CustomerEmail = "lixiaoming@example.com",
            CustomerPhone = "13800000000",
            Subject = "原聊天主题",
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        _mockSessions.Setup(s => s.GetByIdTrackedAsync(sessionId)).ReturnsAsync(session);
        _mockSessions.Setup(s => s.GetByIdAsync(sessionId)).ReturnsAsync(() => session);
        _mockMessages.Setup(m => m.CountAsync(sessionId: sessionId)).ReturnsAsync(0);
        _mockMessages.Setup(m => m.GetListAsync(sessionId, null, null, null, null, 1, 1000)).ReturnsAsync(new List<ChatMessage>());
        _mockConversations.Setup(c => c.GetNextNumberAsync()).ReturnsAsync(1001);
        Conversation? capturedConv = null;
        _mockConversations.Setup(c => c.AddAsync(It.IsAny<Conversation>()))
            .Callback<Conversation>(x => { capturedConv = x; x.Id = 777; })
            .ReturnsAsync((Conversation x) => x);

        // 老客户（DB 中已有 FirstName="三" LastName="张"、无手机）
        var existingCustomer = new Customer
        {
            Id = 55,
            FirstName = "三",
            LastName = "张",
            Emails = new List<Email> { new Email { Id = 1, EmailAddress = "lixiaoming@example.com", Type = 1 } },
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };
        Customer? updatedCustomer = null;
        _mockCustomers.Setup(c => c.GetOrCreateByEmailAsync(
                "lixiaoming@example.com", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(existingCustomer);
        _mockCustomers.Setup(c => c.GetByIdAsync(55)).ReturnsAsync(existingCustomer);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1)
            .Callback(() => { updatedCustomer = existingCustomer; });

        _mockConversations.Setup(c => c.GetByIdAsync(777)).ReturnsAsync((Conversation?)null);
        _mockSystemOptions.Setup(s => s.GetValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        _mockWorkflow.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(0);

        var request = new TransferToTicketRequest { Subject = null };

        // Act
        await _chatService.TransferToTicketAsync(sessionId, request, operatedByUserId: 1);

        // Assert：客户姓名被"清空并改写"为李小明（ChineseNameSplitter 中文拆分：LastName="李"，FirstName="小明"）
        updatedCustomer.Should().NotBeNull();
        updatedCustomer!.LastName.Should().Be("李");
        updatedCustomer.FirstName.Should().Be("小明");

        // 手机被补齐
        var phones = updatedCustomer.GetPhones();
        phones.Should().Contain(p => p.Value == "13800000000");

        // 会话状态转 transferred
        session.Status.Should().Be("transferred");
        session.ConversationId.Should().Be(777);
    }
}

/// <summary>
/// 针对 CustomerService.ValidateAndNormalizeAvatarUrl 辅助方法的纯函数级单测：
/// 覆盖 https/data URI/相对路径 放行、http/javascript/data:text/html/ftp/blob/file 拦截。
/// </summary>
public class CustomerAvatarValidationTests
{
    private static string? Validate(string? avatar, out string? normalized)
        => CustomerService.ValidateAndNormalizeAvatarUrl(avatar, out normalized);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Allow_NullOrWhitespace(string? avatar)
    {
        var err = Validate(avatar, out var normalized);
        err.Should().BeNull();
    }

    [Fact]
    public void Allow_HttpsAbsoluteUrl()
    {
        var err = Validate("https://cdn.example.com/me.png", out var n);
        err.Should().BeNull();
        n.Should().Be("https://cdn.example.com/me.png");
    }

    [Fact]
    public void Allow_HttpsWithQuery()
    {
        var err = Validate("https://a.b/x.jpg?v=1", out _);
        err.Should().BeNull();
    }

    [Theory]
    [InlineData("/attachments/2025/01/a.png")]
    [InlineData("./relative.png")]
    [InlineData("../parent.png")]
    [InlineData("~/uploads/me.png")]
    public void Allow_RelativePaths(string path)
    {
        Validate(path, out _).Should().BeNull();
    }

    [Fact]
    public void Allow_DataImagePngBase64()
    {
        Validate("data:image/png;base64,iVBORw0KGgoAAAANS", out _).Should().BeNull();
    }

    [Fact]
    public void Allow_DataImageSvg()
    {
        Validate("data:image/svg+xml;base64,PHN2Zzz", out _).Should().BeNull();
    }

    [Fact]
    public void Reject_HttpScheme_WithFriendlyMessage()
    {
        var err = Validate("http://insecure.example.com/x.jpg", out _);
        err.Should().NotBeNull();
        err.Should().Contain("https");
    }

    [Fact]
    public void Reject_JavascriptPseudoProtocol()
    {
        var err = Validate("javascript:alert(1)", out _);
        err.Should().NotBeNull();
    }

    [Fact]
    public void Reject_Vbscript()
    {
        Validate("vbscript:msgbox(1)", out _).Should().NotBeNull();
    }

    [Fact]
    public void Reject_DataTextHtml()
    {
        Validate("data:text/html;base64,PHNjcmlwdD4=", out _).Should().NotBeNull();
    }

    [Fact]
    public void Reject_DataJavaScript()
    {
        Validate("data:application/javascript,alert(1)", out _).Should().NotBeNull();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://evil/x.png")]
    [InlineData("blob:https://a/xxx")]
    [InlineData("mailto:a@b.c")]
    public void Reject_OtherSchemes(string url)
    {
        Validate(url, out _).Should().NotBeNull();
    }

    [Fact]
    public void Reject_HttpCaseInsensitive()
    {
        Validate("HTTP://x.y/z.jpg", out _).Should().NotBeNull();
    }

    [Fact]
    public void Allow_HttpsCaseInsensitive()
    {
        Validate("HTTPS://X.Y/Z.PNG", out _).Should().BeNull();
    }
}
