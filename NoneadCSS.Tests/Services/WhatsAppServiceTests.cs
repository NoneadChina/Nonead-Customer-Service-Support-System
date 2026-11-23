using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Domain.Services;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// WhatsAppService 单元测试 — 配置管理、状态查询、回复
/// 测试核心流程：
/// - GetSettingsAsync 返回脱敏配置
/// - UpdateSettingsAsync 持久化 + 加密敏感字段
/// - GetStatusAsync 返回连接状态
/// - ReplyAsync 窗口内回复成功
/// - ReplyAsync 窗口外拒绝
/// </summary>
public class WhatsAppServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IEncryptionService> _mockEncryptionService;
    private readonly Mock<IWhatsAppSettingsProvider> _mockSettingsProvider;
    private readonly Mock<IWhatsAppClient> _mockWhatsAppClient;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<ILogger<WhatsAppService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly WhatsAppService _service;

    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    public WhatsAppServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockEncryptionService = new Mock<IEncryptionService>();
        _mockSettingsProvider = new Mock<IWhatsAppSettingsProvider>();
        _mockWhatsAppClient = new Mock<IWhatsAppClient>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockLogger = new Mock<ILogger<WhatsAppService>>();
        _mockConfiguration = new Mock<IConfiguration>();

        _mockSystemOptionRepo = new Mock<ISystemOptionRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockAttachmentRepo = new Mock<IAttachmentRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _mockConfiguration.Setup(c => c["App:FrontendUrl"]).Returns("http://localhost:5173");
        _mockConfiguration.Setup(c => c["App:BaseUrl"]).Returns("http://localhost:5009");
        _mockCurrentUserAccessor.Setup(u => u.UserId).Returns(1);

        _service = new WhatsAppService(
            _mockUnitOfWork.Object,
            _mockEncryptionService.Object,
            _mockSettingsProvider.Object,
            _mockWhatsAppClient.Object,
            _mockCurrentUserAccessor.Object,
            _mockNotificationPublisher.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            Options.Create(new NoneadCSS.Application.Configuration.IngestionMatchOptions()),
            new Mock<IHostEnvironment>().Object);
    }

    #region GetSettingsAsync

    [Fact]
    public async Task GetSettingsAsync_WithFullConfig_ReturnsDesensitizedSettings()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "123456",
            AccessToken = "secret_token",
            AppSecret = "secret_app_secret",
            VerifyToken = "verify_token",
            BusinessPhoneNumber = "+8613800138000",
            ApiVersion = "v24.0",
            MailboxId = 1,
            AutoReplyEnabled = true,
            AutoReplyMessage = "Welcome",
            Enabled = true
        });

        var result = await _service.GetSettingsAsync();

        result.PhoneNumberId.Should().Be("123456");
        result.BusinessPhoneNumber.Should().Be("+8613800138000");
        result.ApiVersion.Should().Be("v24.0");
        result.MailboxId.Should().Be(1);
        result.AutoReplyEnabled.Should().BeTrue();
        result.AutoReplyMessage.Should().Be("Welcome");
        result.Enabled.Should().BeTrue();

        // 敏感字段不应返回原值，仅返回是否存在
        result.HasAccessToken.Should().BeTrue();
        result.HasAppSecret.Should().BeTrue();
        result.HasVerifyToken.Should().BeTrue();
    }

    [Fact]
    public async Task GetSettingsAsync_WithEmptyConfig_ReturnsFalseForSensitiveFlags()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions());

        var result = await _service.GetSettingsAsync();

        result.HasAccessToken.Should().BeFalse();
        result.HasAppSecret.Should().BeFalse();
        result.HasVerifyToken.Should().BeFalse();
        result.Enabled.Should().BeFalse();
    }

    #endregion

    #region UpdateSettingsAsync

    [Fact]
    public async Task UpdateSettingsAsync_WithAccessToken_EncryptsBeforeSaving()
    {
        var request = new UpdateWhatsAppSettingsRequest
        {
            PhoneNumberId = "new_phone_id",
            AccessToken = "plain_token",
            AppSecret = "plain_secret",
            Enabled = true
        };

        _mockSystemOptionRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<SystemOption, bool>>>()))
            .ReturnsAsync(new List<SystemOption>());

        _mockEncryptionService.Setup(e => e.Encrypt("plain_token")).Returns("encrypted_token");
        _mockEncryptionService.Setup(e => e.Encrypt("plain_secret")).Returns("encrypted_secret");

        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "new_phone_id",
            AccessToken = "plain_token",
            AppSecret = "plain_secret",
            Enabled = true,
            ApiVersion = "v24.0"
        });

        var result = await _service.UpdateSettingsAsync(request);

        // 验证加密被调用
        _mockEncryptionService.Verify(e => e.Encrypt("plain_token"), Times.Once);
        _mockEncryptionService.Verify(e => e.Encrypt("plain_secret"), Times.Once);

        // 验证 SaveChangesAsync 被调用
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);

        // 验证配置缓存刷新
        _mockSettingsProvider.Verify(s => s.RefreshAsync(), Times.Once);

        result.PhoneNumberId.Should().Be("new_phone_id");
        result.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettingsAsync_WithNullSensitiveFields_DoesNotEncrypt()
    {
        var request = new UpdateWhatsAppSettingsRequest
        {
            PhoneNumberId = "phone_id_only",
            AccessToken = null,
            AppSecret = null,
            Enabled = true
        };

        _mockSystemOptionRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<SystemOption, bool>>>()))
            .ReturnsAsync(new List<SystemOption>());

        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "phone_id_only",
            Enabled = true,
            ApiVersion = "v24.0"
        });

        var result = await _service.UpdateSettingsAsync(request);

        // 敏感字段为 null 时不应调用加密
        _mockEncryptionService.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);

        result.PhoneNumberId.Should().Be("phone_id_only");
    }

    [Fact]
    public async Task UpdateSettingsAsync_WithExistingOption_UpdatesValue()
    {
        var existingOption = new SystemOption { Id = 10, Name = "whatsapp.phone_number_id", Value = "old_id" };

        // 仅对 phone_number_id 返回已存在记录（tracked），其他键返回 null（创建新记录）
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(WhatsAppOptionKeys.PhoneNumberId))
            .ReturnsAsync(existingOption);
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.Is<string>(s => s != WhatsAppOptionKeys.PhoneNumberId)))
            .ReturnsAsync((SystemOption?)null);

        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "new_id",
            ApiVersion = "v24.0"
        });

        var request = new UpdateWhatsAppSettingsRequest
        {
            PhoneNumberId = "new_id",
            Enabled = true
        };

        await _service.UpdateSettingsAsync(request);

        // 已存在的选项直接修改实体属性（EF Core 追踪），不调用 UpdateAsync
        // 验证 phone_number_id 的值被修改
        existingOption.Value.Should().Be("new_id");

        // 验证 SaveChangesAsync 被调用以持久化修改
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region GetStatusAsync

    [Fact]
    public async Task GetStatusAsync_WithConfiguredEnabled_ReturnsConfiguredStatus()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "123",
            AccessToken = "token",
            AppSecret = "secret",
            VerifyToken = "verify",
            Enabled = true
        });

        // 模拟无 WhatsApp 会话
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var result = await _service.GetStatusAsync();

        result.Configured.Should().BeTrue();
        result.Enabled.Should().BeTrue();
        result.CallbackUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetStatusAsync_WithUnconfigured_ReturnsNotConfigured()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions());

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var result = await _service.GetStatusAsync();

        result.Configured.Should().BeFalse();
        result.Enabled.Should().BeFalse();
    }

    #endregion

    #region ReplyAsync

    [Fact]
    public async Task ReplyAsync_WithinWindow_SendsMessageAndCreatesThread()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "123",
            AccessToken = "token",
            AppSecret = "secret",
            VerifyToken = "verify",
            Enabled = true
        });

        var conversation = new Conversation
        {
            Id = 10,
            CustomerId = 1,
            SourceType = SourceType.WhatsApp,
            Status = ConversationStatus.Pending,
            MailboxId = 1
        };

        // 最近客户消息在 1 小时前（窗口内）
        var recentThread = new ConversationThread
        {
            Id = 5,
            ConversationId = 10,
            Type = ThreadType.Customer,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            MessageId = "wamid.customer_msg",
            From = "8613800138000"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread> { recentThread });
        _mockWhatsAppClient.Setup(c => c.SendTextMessageAsync("8613800138000", "Hello reply", null))
            .ReturnsAsync("wamid.reply_msg_001");
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 200; return t; });

        var request = new WhatsAppReplyRequest
        {
            ConversationId = 10,
            Content = "Hello reply"
        };

        var result = await _service.ReplyAsync(request, 1);

        result.Sent.Should().BeTrue();
        result.ThreadCreated.Should().BeTrue();
        result.ThreadId.Should().HaveValue();

        _mockWhatsAppClient.Verify(c => c.SendTextMessageAsync("8613800138000", "Hello reply", null), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_OutsideWindow_RejectsReply()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "123",
            AccessToken = "token",
            AppSecret = "secret",
            VerifyToken = "verify",
            Enabled = true
        });

        var conversation = new Conversation
        {
            Id = 10,
            CustomerId = 1,
            SourceType = SourceType.WhatsApp,
            Status = ConversationStatus.Pending,
            MailboxId = 1
        };

        // 最近客户消息在 25 小时前（窗口外）
        var oldThread = new ConversationThread
        {
            Id = 5,
            ConversationId = 10,
            Type = ThreadType.Customer,
            CreatedAt = DateTime.UtcNow.AddHours(-25),
            MessageId = "wamid.old_msg",
            From = "8613800138000"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10))
            .ReturnsAsync(conversation);
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread> { oldThread });

        var request = new WhatsAppReplyRequest
        {
            ConversationId = 10,
            Content = "Reply after window"
        };

        var result = await _service.ReplyAsync(request, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();

        // 不应调用 Cloud API
        _mockWhatsAppClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_ConversationNotFound_ReturnsError()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new WhatsAppOptions
        {
            PhoneNumberId = "123",
            AccessToken = "token",
            AppSecret = "secret",
            VerifyToken = "verify",
            Enabled = true
        });

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999))
            .ReturnsAsync((Conversation?)null);

        var request = new WhatsAppReplyRequest
        {
            ConversationId = 999,
            Content = "Test"
        };

        var result = await _service.ReplyAsync(request, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion
}
