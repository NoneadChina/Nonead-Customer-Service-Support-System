using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// WhatsAppIngestionService 单元测试 — Webhook 消息接入
/// 测试核心流程：
/// - 非 WhatsApp payload 忽略
/// - 状态回执处理
/// - 消息去重
/// - 客户创建/匹配
/// - 系统消息忽略
/// - 工作流触发隔离
/// </summary>
public class WhatsAppIngestionServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<IWhatsAppSettingsProvider> _mockSettingsProvider;
    private readonly Mock<IWhatsAppClient> _mockWhatsAppClient;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<IDistributedLockService> _mockDistributedLockService;
    private readonly Mock<ILogger<WhatsAppIngestionService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly WhatsAppIngestionService _service;

    private readonly Mock<IThreadRepository> _mockThreadRepo; // IThreadRepository 管理 ConversationThread
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    public WhatsAppIngestionServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockSettingsProvider = new Mock<IWhatsAppSettingsProvider>();
        _mockWhatsAppClient = new Mock<IWhatsAppClient>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockDistributedLockService = new Mock<IDistributedLockService>();
        var mockLockHandle = new Mock<IDistributedLockHandle>();
        mockLockHandle.Setup(h => h.LockKey).Returns("test-lock");
        _mockDistributedLockService.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(mockLockHandle.Object);
        _mockLogger = new Mock<ILogger<WhatsAppIngestionService>>();
        _mockConfiguration = new Mock<IConfiguration>();

        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockAttachmentRepo = new Mock<IAttachmentRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "123456",
            AccessToken = "test_token",
            ApiVersion = "v24.0",
            Enabled = true,
            AutoReplyEnabled = false,
            MailboxId = 1
        });

        _mockConfiguration.Setup(c => c["App:FrontendUrl"]).Returns("http://localhost:5173");

        _service = new WhatsAppIngestionService(
            _mockUnitOfWork.Object,
            _mockWorkflowService.Object,
            _mockSettingsProvider.Object,
            _mockWhatsAppClient.Object,
            _mockNotificationPublisher.Object,
            _mockDistributedLockService.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            Options.Create(new NoneadCSS.Application.Configuration.IngestionMatchOptions()),
            new Mock<IHostEnvironment>().Object);
    }

    #region ProcessWebhookPayloadAsync - Non-WhatsApp Payload

    [Fact]
    public async Task ProcessWebhookPayloadAsync_NonWhatsAppPayload_ReturnsEmptyResult()
    {
        var payload = "{\"object\":\"page\",\"entry\":[]}";

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        result.IsDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_EmptyPayload_ReturnsEmptyResult()
    {
        var result = await _service.ProcessWebhookPayloadAsync("");

        result.NewConversationCount.Should().Be(0);
    }

    #endregion

    #region ProcessWebhookPayloadAsync - Status Updates

    [Fact]
    public async Task ProcessWebhookPayloadAsync_StatusOnly_SetsIsStatusFlag()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "statuses": [{
                            "id": "wamid.status1",
                            "recipient_id": "8613800138000",
                            "status": "delivered",
                            "timestamp": "1700000000"
                        }]
                    }
                }]
            }]
        }
        """;

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.IsStatus.Should().BeTrue();
        result.NewConversationCount.Should().Be(0);
    }

    #endregion

    #region ProcessWebhookPayloadAsync - Duplicate Detection

    [Fact]
    public async Task ProcessWebhookPayloadAsync_DuplicateMessage_SetsIsDuplicate()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "contacts": [{ "profile": { "name": "Test" }, "wa_id": "8613800138000" }],
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.dup123",
                            "timestamp": "1700000000",
                            "type": "text",
                            "text": { "body": "Duplicate" }
                        }]
                    }
                }]
            }]
        }
        """;

        // 模拟客户匹配成功（TD-06 去重检查移入事务内、客户匹配之后，需让流程通过前置步骤到达事务内去重）
        var customer = new Customer { Id = 1, Channel = "whatsapp", ChannelId = "8613800138000" };
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", "8613800138000", "Test"))
            .ReturnsAsync(customer);

        // 模拟会话匹配（无论是否匹配到会话，去重检查均在事务内执行）
        _mockConversationRepo.Setup(r => r.FindActiveWhatsAppConversationByWaIdAsync(
            "8613800138000", It.IsAny<TimeSpan>()))
            .ReturnsAsync((Conversation?)null);

        // 模拟已存在的线程（去重命中）
        var existingThread = new ConversationThread { MessageId = "wamid.dup123" };
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread> { existingThread });

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.IsDuplicate.Should().BeTrue();
        result.NewConversationCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_LockNotAcquired_TreatsAsDuplicate()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "contacts": [{ "profile": { "name": "Test" }, "wa_id": "8613800138000" }],
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.lock1",
                            "timestamp": "1700000000",
                            "type": "text",
                            "text": { "body": "Lock contention" }
                        }]
                    }
                }]
            }]
        }
        """;

        // 模拟锁已被其他实例持有，应跳过处理
        _mockDistributedLockService.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync((IDistributedLockHandle?)null);

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.IsDuplicate.Should().BeTrue();
        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        result.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region ProcessWebhookPayloadAsync - New Conversation

    [Fact]
    public async Task ProcessWebhookPayloadAsync_NewMessage_CreatesConversationAndThread()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "contacts": [{ "profile": { "name": "New User" }, "wa_id": "8613800138000" }],
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.new123",
                            "timestamp": "1700000000",
                            "type": "text",
                            "text": { "body": "Hello" }
                        }]
                    }
                }]
            }]
        }
        """;

        // 无重复线程
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        // 模拟客户创建/匹配
        var customer = new Customer
        {
            Id = 1,
            Channel = "whatsapp",
            ChannelId = "8613800138000",
            FirstName = "New User"
        };
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", "8613800138000", "New User"))
            .ReturnsAsync(customer);

        // 模拟无活跃会话（创建新会话）
        _mockConversationRepo.Setup(r => r.FindActiveWhatsAppConversationByWaIdAsync(
            "8613800138000", It.IsAny<TimeSpan>()))
            .ReturnsAsync((Conversation?)null);

        // 模拟线程添加
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 100; return t; });

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.NewConversationCount.Should().Be(1);
        result.ConversationId.Should().HaveValue();

        // 验证事务提交
        _mockTransaction.Verify(t => t.CommitAsync(), Times.AtLeastOnce);
    }

    #endregion

    #region ProcessWebhookPayloadAsync - System Message

    [Fact]
    public async Task ProcessWebhookPayloadAsync_SystemMessage_IgnoresWithoutError()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.sys1",
                            "timestamp": "1700000000",
                            "type": "system",
                            "system": { "type": "user_changed_number" }
                        }]
                    }
                }]
            }]
        }
        """;

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.NewConversationCount.Should().Be(0);
        result.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region ProcessWebhookPayloadAsync - Workflow Isolation

    [Fact]
    public async Task ProcessWebhookPayloadAsync_WorkflowFailure_DoesNotBlockIngestion()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "contacts": [{ "profile": { "name": "Test" }, "wa_id": "8613800138000" }],
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.wf1",
                            "timestamp": "1700000000",
                            "type": "text",
                            "text": { "body": "Workflow test" }
                        }]
                    }
                }]
            }]
        }
        """;

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var customer = new Customer { Id = 1, Channel = "whatsapp", ChannelId = "8613800138000" };
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", "8613800138000", "Test"))
            .ReturnsAsync(customer);

        _mockConversationRepo.Setup(r => r.FindActiveWhatsAppConversationByWaIdAsync(
            "8613800138000", It.IsAny<TimeSpan>()))
            .ReturnsAsync((Conversation?)null);

        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 200; return t; });

        // 工作流抛异常
        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Workflow error"));

        // 不应抛异常
        var result = await _service.ProcessWebhookPayloadAsync(payload);

        // 消息仍应被处理
        result.NewConversationCount.Should().Be(1);
    }

    #endregion

    #region ProcessWebhookPayloadAsync - Customer Creation Failure

    [Fact]
    public async Task ProcessWebhookPayloadAsync_CustomerCreationFailure_ReturnsErrorMessage()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "contacts": [{ "profile": { "name": "Test" }, "wa_id": "8613800138000" }],
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.cust1",
                            "timestamp": "1700000000",
                            "type": "text",
                            "text": { "body": "Customer fail" }
                        }]
                    }
                }]
            }]
        }
        """;

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        // 客户创建返回 null
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", "8613800138000", "Test"))
            .ReturnsAsync((Customer?)null);

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.NewConversationCount.Should().Be(0);
    }

    #endregion

    #region ProcessWebhookPayloadAsync - Existing Conversation (Reply)

    [Fact]
    public async Task ProcessWebhookPayloadAsync_ExistingConversation_CreatesReplyThread()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "contacts": [{ "profile": { "name": "Existing" }, "wa_id": "8613800138000" }],
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.reply1",
                            "timestamp": "1700000000",
                            "type": "text",
                            "text": { "body": "Reply" }
                        }]
                    }
                }]
            }]
        }
        """;

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var customer = new Customer { Id = 1, Channel = "whatsapp", ChannelId = "8613800138000" };
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", "8613800138000", "Existing"))
            .ReturnsAsync(customer);

        // 模拟已有活跃会话
        var existingConv = new Conversation
        {
            Id = 50,
            CustomerId = 1,
            SourceType = SourceType.WhatsApp,
            Status = ConversationStatus.Pending,
            MailboxId = 1
        };
        _mockConversationRepo.Setup(r => r.FindActiveWhatsAppConversationByWaIdAsync(
            "8613800138000", It.IsAny<TimeSpan>()))
            .ReturnsAsync(existingConv);

        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 300; return t; });

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.ReplyCount.Should().Be(1);
        result.NewConversationCount.Should().Be(0);
        result.ConversationId.Should().Be(50);
    }

    #endregion

    #region ProcessWebhookPayloadAsync - Multiple Messages

    [Fact]
    public async Task ProcessWebhookPayloadAsync_MultipleMessages_ProcessesAll()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "contacts": [{ "profile": { "name": "Multi" }, "wa_id": "8613800138000" }],
                        "messages": [
                            { "from": "8613800138000", "id": "wamid.m1", "timestamp": "1700000000", "type": "text", "text": { "body": "Msg 1" } },
                            { "from": "8613800138000", "id": "wamid.m2", "timestamp": "1700000001", "type": "text", "text": { "body": "Msg 2" } }
                        ]
                    }
                }]
            }]
        }
        """;

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var customer = new Customer { Id = 1, Channel = "whatsapp", ChannelId = "8613800138000" };
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", "8613800138000", "Multi"))
            .ReturnsAsync(customer);

        _mockConversationRepo.Setup(r => r.FindActiveWhatsAppConversationByWaIdAsync(
            "8613800138000", It.IsAny<TimeSpan>()))
            .ReturnsAsync((Conversation?)null);

        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = new Random().Next(); return t; });

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.NewConversationCount.Should().Be(2);
    }

    #endregion
}
