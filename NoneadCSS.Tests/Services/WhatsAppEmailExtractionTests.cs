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
/// WhatsApp 邮箱提取与引导逻辑测试 — 验证对话中自动识别邮箱 + 新会话引导发送。
/// 测试场景：
/// - 客户消息中包含邮箱 → 自动提取并写入 Customer.Emails
/// - 客户消息中不含邮箱 + 新会话 + 无邮箱 → 发送引导消息
/// - 客户消息中不含邮箱 + 已有会话 → 不发送引导消息
/// - 客户已有邮箱 → 不重复提取
/// </summary>
public class WhatsAppEmailExtractionTests
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

    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    public WhatsAppEmailExtractionTests()
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
            ContactCollectionEnabled = true,
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
            Options.Create(new IngestionMatchOptions()),
            new Mock<IHostEnvironment>().Object);
    }

    /// <summary>
    /// 构造 WhatsApp text 消息的 Webhook payload JSON。
    /// </summary>
    private static string BuildTextPayload(string waId, string profileName, string messageId, string textBody)
    {
        var nameEsc = System.Text.Json.JsonEncodedText.Encode(profileName);
        var bodyEsc = System.Text.Json.JsonEncodedText.Encode(textBody);
        var fromEsc = System.Text.Json.JsonEncodedText.Encode(waId);
        var idEsc = System.Text.Json.JsonEncodedText.Encode(messageId);
        return $$"""
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "contacts": [{ "profile": { "name": "{{profileName}}" }, "wa_id": "{{waId}}" }],
                        "messages": [{
                            "from": "{{waId}}",
                            "id": "{{messageId}}",
                            "timestamp": "1700000000",
                            "type": "text",
                            "text": { "body": "{{textBody}}" }
                        }]
                    }
                }]
            }]
        }
        """;
    }

    private void SetupNewConversation(Customer customer, string waId, string profileName)
    {
        // 无重复线程
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        // 模拟客户创建/匹配
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", waId, profileName))
            .ReturnsAsync(customer);
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", waId, null))
            .ReturnsAsync(customer);
        _mockCustomerRepo.Setup(r => r.FindTracked(customer.Id))
            .Returns(customer);

        // 模拟无活跃会话（创建新会话）
        _mockConversationRepo.Setup(r => r.FindActiveWhatsAppConversationByWaIdAsync(
            waId, It.IsAny<TimeSpan>()))
            .ReturnsAsync((Conversation?)null);

        // 模拟线程添加
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 100; return t; });

        // 模拟 SaveChangesAsync
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // 模拟 Conversation 保存
        _mockConversationRepo.Setup(r => r.AddAsync(It.IsAny<Conversation>()))
            .ReturnsAsync((Conversation c) => { c.Id = 50; return c; });

        _mockConversationRepo.Setup(r => r.GetNextNumberAsync())
            .ReturnsAsync(1001);
    }

    #region 邮箱提取 — 客户消息中包含邮箱

    [Fact]
    public async Task NewMessage_ContainsEmail_ExtractsAndSavesToCustomer()
    {
        // 模拟客户：无邮箱
        var customer = new Customer
        {
            Id = 1,
            Channel = "whatsapp",
            ChannelId = "8613800138000",
            FirstName = "Test User",
            Emails = new List<Email>()
        };
        SetupNewConversation(customer, "8613800138000", "Test User");

        // 客户消息中包含邮箱
        var payload = BuildTextPayload("8613800138000", "Test User", "wamid.email1", "我的邮箱是 alice@example.com");

        await _service.ProcessWebhookPayloadAsync(payload);

        // 验证客户被写入了邮箱
        customer.Emails.Should().NotBeEmpty();
        customer.Emails.First().EmailAddress.Should().Be("alice@example.com");

        // 验证 SaveChangesAsync 被调用（邮箱持久化）
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);

        // 验证未发送引导消息（因为已提取到邮箱）
        _mockWhatsAppClient.Verify(c => c.SendTextMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task NewMessage_ContainsEmailInEnglish_ExtractsCorrectly()
    {
        var customer = new Customer
        {
            Id = 1,
            Channel = "whatsapp",
            ChannelId = "8613800138000",
            FirstName = "John",
            Emails = new List<Email>()
        };
        SetupNewConversation(customer, "8613800138000", "John");

        var payload = BuildTextPayload("8613800138000", "John", "wamid.email2",
            "Sure, my email is john.doe+test@gmail.com");

        await _service.ProcessWebhookPayloadAsync(payload);

        customer.Emails.Should().HaveCount(1);
        customer.Emails.First().EmailAddress.Should().Be("john.doe+test@gmail.com");
    }

    [Fact]
    public async Task NewMessage_AlreadyHasEmail_DoesNotDuplicate()
    {
        // 客户已有该邮箱
        var existingEmail = "alice@example.com";
        var customer = new Customer
        {
            Id = 1,
            Channel = "whatsapp",
            ChannelId = "8613800138000",
            FirstName = "Test User",
            Emails = new List<Email>
            {
                new() { EmailAddress = existingEmail, Type = 1, CustomerId = 1 }
            }
        };
        SetupNewConversation(customer, "8613800138000", "Test User");

        // 客户消息中再次包含同一邮箱
        var payload = BuildTextPayload("8613800138000", "Test User", "wamid.dup3",
            "my email is ALICE@EXAMPLE.COM");

        await _service.ProcessWebhookPayloadAsync(payload);

        // 邮箱不应重复写入
        customer.Emails.Should().HaveCount(1);
        customer.Emails.First().EmailAddress.Should().Be(existingEmail);
    }

    #endregion

    #region 邮箱引导 — 新会话且无邮箱时发送引导消息

    [Fact]
    public async Task NewConversation_NoEmailInMessage_SendsEmailGuidance()
    {
        var customer = new Customer
        {
            Id = 1,
            Channel = "whatsapp",
            ChannelId = "8613800138000",
            FirstName = "New User",
            Emails = new List<Email>()
        };
        SetupNewConversation(customer, "8613800138000", "New User");

        // 客户消息中不含邮箱
        var payload = BuildTextPayload("8613800138000", "New User", "wamid.noemail1",
            "Hello, I want to ask about pricing");

        await _service.ProcessWebhookPayloadAsync(payload);

        // 验证发送了引导消息
        _mockWhatsAppClient.Verify(
            c => c.SendTextMessageAsync("8613800138000", It.Is<string>(s => s.Contains("邮箱")), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task NewConversation_ChineseMessageNoEmail_SendsGuidance()
    {
        var customer = new Customer
        {
            Id = 1,
            Channel = "whatsapp",
            ChannelId = "8613800138000",
            FirstName = "张三",
            Emails = new List<Email>()
        };
        SetupNewConversation(customer, "8613800138000", "张三");

        var payload = BuildTextPayload("8613800138000", "张三", "wamid.noemail2",
            "你好，我想了解一下产品");

        await _service.ProcessWebhookPayloadAsync(payload);

        _mockWhatsAppClient.Verify(
            c => c.SendTextMessageAsync("8613800138000", It.Is<string>(s => s.Contains("邮箱")), It.IsAny<string?>()),
            Times.Once);
    }

    #endregion

    #region 邮箱引导 — 非新会话不发送引导

    [Fact]
    public async Task ExistingConversation_NoEmailInMessage_DoesNotSendGuidance()
    {
        var customer = new Customer
        {
            Id = 1,
            Channel = "whatsapp",
            ChannelId = "8613800138000",
            FirstName = "Existing User",
            Emails = new List<Email>()
        };

        // 无重复线程
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync("whatsapp", "8613800138000", "Existing User"))
            .ReturnsAsync(customer);

        // 模拟已有活跃会话（非新会话）
        var existingConversation = new Conversation
        {
            Id = 200,
            Number = 500,
            CustomerId = 1,
            MailboxId = 1,
            Status = ConversationStatus.Active
        };
        _mockConversationRepo.Setup(r => r.FindActiveWhatsAppConversationByWaIdAsync(
            "8613800138000", It.IsAny<TimeSpan>()))
            .ReturnsAsync(existingConversation);

        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 100; return t; });

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        // 客户消息中不含邮箱
        var payload = BuildTextPayload("8613800138000", "Existing User", "wamid.noemail3",
            "Follow up question about the order");

        await _service.ProcessWebhookPayloadAsync(payload);

        // 非新会话不应发送引导消息
        _mockWhatsAppClient.Verify(
            c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    #endregion

    #region 邮箱引导 — 客户已有邮箱时不发送引导

    [Fact]
    public async Task NewConversation_CustomerAlreadyHasEmail_DoesNotSendGuidance()
    {
        var customer = new Customer
        {
            Id = 1,
            Channel = "whatsapp",
            ChannelId = "8613800138000",
            FirstName = "Has Email",
            Emails = new List<Email>
            {
                new() { EmailAddress = "existing@test.com", Type = 1, CustomerId = 1 }
            }
        };
        SetupNewConversation(customer, "8613800138000", "Has Email");

        // 客户消息中不含邮箱，但客户已有邮箱
        var payload = BuildTextPayload("8613800138000", "Has Email", "wamid.hasemail1",
            "Hi, need help with my order");

        await _service.ProcessWebhookPayloadAsync(payload);

        // 客户已有邮箱，不应发送引导消息
        _mockWhatsAppClient.Verify(
            c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    #endregion
}
