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
using System.Text.RegularExpressions;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ConversationThreadService 单元测试
/// 验证 AddReplyAsync 在邮件渠道回复时：
/// 1. 主题包含 [#会话编号] 标记
/// 2. 邮件正文包含隐藏的 {#FS:线程ID#} 标记
/// </summary>
public class ConversationThreadServiceTests
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

    private readonly Mock<IConversationRepository> _mockConvRepo;
    private readonly Mock<IMailboxRepository> _mockMailboxRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    private readonly ConversationThreadService _service;

    public ConversationThreadServiceTests()
    {
        // 初始化所有 Mock 对象
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

        _mockConvRepo = new Mock<IConversationRepository>();
        _mockMailboxRepo = new Mock<IMailboxRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockUserRepo = new Mock<IUserRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockAttachmentRepo = new Mock<IAttachmentRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        // 配置 UnitOfWork 返回相应的 Mock 仓储
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConvRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);

        // 事务提交
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockTransaction.Setup(t => t.CommitAsync()).Returns(Task.CompletedTask);

        // 默认 SaveChangesAsync（SetupEmailConversation 会覆盖以模拟 EF 主键生成）
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // 附件查询：默认返回空列表
        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>());

        // 最新客户线程：默认无（In-Reply-To / References 保持 null）
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(It.IsAny<int>()))
            .ReturnsAsync((ConversationThread?)null);

        // 邮件模板：默认返回 null，走 RenderReplyEmailBodyAsync 的默认正文分支（不触发 RenderTemplateAsync）
        _mockMailTemplateService.Setup(m => m.GetBySlugAsync(It.IsAny<string>()))
            .ReturnsAsync((MailTemplateResponse?)null);

        // 邮件签名：默认无签名
        _mockEmailSignatureService.Setup(s => s.GetDefaultAsync(It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync((EmailSignatureDto?)null);

        // 用户 / 客户查找：返回 null 避免导航属性崩溃
        _mockUserRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((User?)null);
        _mockCustomerRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((Customer?)null);

        // 事件分发器 Trigger 为 void 方法，Loose Mock 默认不执行任何操作即可

        // 通知发布器
        _mockNotificationPublisher.Setup(n => n.NotifyMailboxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);

        // 工作流触发
        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(0);

        // 关键配置：IFrontendUrlResolver 已在上方 Setup 返回 https://app.example.com，
        // RenderReplyEmailBodyAsync 通过 _frontendUrlResolver.GetFrontendUrl() 获取前端 URL
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
            new Mock<Microsoft.Extensions.Hosting.IHostEnvironment>().Object);
    }

    /// <summary>
    /// 构造一个邮件渠道会话并装配 GetByIdForUpdateAsync / Mailbox.GetByIdAsync 的返回值，
    /// 同时在 SaveChangesAsync 回调中模拟 EF Core 主键生成（将新增线程的 Id 设置为 99）。
    /// </summary>
    private Conversation SetupEmailConversation(string subject, int number = 42)
    {
        var conversation = new Conversation
        {
            Id = 1,
            Number = number,
            Subject = subject,
            MailboxId = 1,
            Status = ConversationStatus.Active,
            SourceType = SourceType.Email,
            CustomerEmail = "customer@example.com"
        };

        _mockConvRepo.Setup(r => r.GetByIdForUpdateAsync(It.IsAny<int>()))
            .ReturnsAsync(conversation);

        var mailbox = new Mailbox { Id = 1, Email = "support@example.com", Name = "Support" };
        _mockMailboxRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(mailbox);

        // 模拟 EF Core 在 SaveChangesAsync 后为新增线程生成主键
        void AssignThreadId()
        {
            foreach (var t in conversation.Threads.Where(t => t.Id == 0))
            {
                t.Id = 99;
            }
        }

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .Callback(AssignThreadId)
            .ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(AssignThreadId)
            .ReturnsAsync(1);

        return conversation;
    }

    private string _capturedSubject = string.Empty;
    private string _capturedBody = string.Empty;

    /// <summary>
    /// 装配 EnqueueEmailAsync 的 Mock，将 subject / body 写入实例字段（闭包可修改）。
    /// </summary>
    private void SetupEnqueueEmailCapture()
    {
        _capturedSubject = string.Empty;
        _capturedBody = string.Empty;

        _mockEmailQueueService.Setup(e => e.EnqueueEmailAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<int>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<int?>()))
            .Callback<int, string, string, string, List<int>?, string?, string?, int?, string?, string?, int, int?, int?>(
                (_, _, subject, body, _, _, _, _, _, _, _, _, _) =>
                {
                    _capturedSubject = subject;
                    _capturedBody = body;
                })
            .Returns(Task.CompletedTask);
    }

    private static CreateThreadRequest BuildReplyRequest(int conversationId)
    {
        return new CreateThreadRequest
        {
            ConversationId = conversationId,
            Body = "<p>Hello</p>",
            From = "support@example.com",
            To = "customer@example.com"
        };
    }

    private const string EnqueueEmailVerify =
        "EnqueueEmailAsync should be called exactly once for email channel reply";

    [Fact]
    public async Task AddReplyAsync_EmailChannel_SubjectShouldContainConversationNumber()
    {
        // Arrange
        var conversation = SetupEmailConversation("Test Subject", number: 42);
        SetupEnqueueEmailCapture();

        var request = BuildReplyRequest(conversation.Id);

        // Act
        await _service.AddReplyAsync(request);

        // Assert - 主题应包含 [#会话编号] 标记
        _capturedSubject.Should().NotBeNullOrEmpty();
        _capturedSubject.Should().Contain("[#42]");

        _mockEmailQueueService.Verify(
            e => e.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()),
            Times.Once, EnqueueEmailVerify);
    }

    [Fact]
    public async Task AddReplyAsync_EmailChannel_BodyShouldContainHiddenThreadMarker()
    {
        // Arrange
        var conversation = SetupEmailConversation("Test Subject", number: 42);
        SetupEnqueueEmailCapture();

        var request = BuildReplyRequest(conversation.Id);

        // Act
        await _service.AddReplyAsync(request);

        // Assert - 邮件正文应包含隐藏的 {#FS:线程ID#} 标记
        _capturedBody.Should().NotBeNullOrEmpty();
        _capturedBody.Should().Contain("{#FS:");
        _capturedBody.Should().Contain("#}");
        // thread.Id 在 SaveChangesAsync 回调中被模拟为 99
        _capturedBody.Should().Contain("{#FS:99#}");
        // 隐藏 div 的样式标记
        _capturedBody.Should().Contain("display:none");

        _mockEmailQueueService.Verify(
            e => e.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()),
            Times.Once, EnqueueEmailVerify);
    }

    [Fact]
    public async Task AddReplyAsync_EmailChannel_SubjectAlreadyHasNumber_ShouldNotDuplicate()
    {
        // Arrange - 主题已包含 [#42]
        var conversation = SetupEmailConversation("[#42] Test Subject", number: 42);
        SetupEnqueueEmailCapture();

        var request = BuildReplyRequest(conversation.Id);

        // Act
        await _service.AddReplyAsync(request);

        // Assert - 主题中 [#42] 只出现一次（不重复前置），且保留 Re: 前缀
        _capturedSubject.Should().NotBeNullOrEmpty();
        _capturedSubject.Should().StartWith("Re: ");
        var matchCount = Regex.Matches(_capturedSubject, Regex.Escape("[#42]")).Count;
        matchCount.Should().Be(1, "主题已包含 [#42] 时不应重复添加");

        _mockEmailQueueService.Verify(
            e => e.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()),
            Times.Once, EnqueueEmailVerify);
    }
}
