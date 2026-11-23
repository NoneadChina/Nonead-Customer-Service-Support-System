using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Domain.Services;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// TelegramService 单元测试 — 配置读写（脱敏 + ENC: 加密、不回显密钥）、入站模式同步（webhook/polling 409 冲突处理）、
/// 状态查询（getMe / getWebhookInfo / 轮询游标 / 不可达计数）、坐席回复（无窗口限制、4096 分片、
/// 403 拉黑标记不可达、429/420 retry_after 队列友好退避、chat.id 迁移改写 ChannelId）。
/// 全程无网络：ITelegramClient 用 Moq 替身；TelegramThrottle 用注入时钟的真实实例。
/// </summary>
public class TelegramServiceTests
{
    private const string ChatId = "5551234567890";
    private const string BotToken = "123456:AAHsupersecrettoken";

    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IEncryptionService> _mockEncryptionService;
    private readonly Mock<ITelegramSettingsProvider> _mockSettingsProvider;
    private readonly Mock<ITelegramClient> _mockClient;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<ILogger<TelegramService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IHostEnvironment> _mockEnvironment;
    private readonly Mock<ITelegramPollingConnectionStatus> _mockPollingStatus;
    private readonly Mock<IWebhookService> _mockWebhookService;
    private readonly TelegramThrottle _throttle;
    private readonly TelegramService _service;

    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IMailboxRepository> _mockMailboxRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    /// <summary>捕获写入的线程，便于断言 MessageId / 类型 / 状态</summary>
    private readonly List<ConversationThread> _addedThreads = [];

    public TelegramServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockEncryptionService = new Mock<IEncryptionService>();
        _mockSettingsProvider = new Mock<ITelegramSettingsProvider>();
        _mockClient = new Mock<ITelegramClient>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockLogger = new Mock<ILogger<TelegramService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockEnvironment = new Mock<IHostEnvironment>();
        _mockPollingStatus = new Mock<ITelegramPollingConnectionStatus>();
        _mockWebhookService = new Mock<IWebhookService>();

        // 可注入时钟的时钟推进式假时钟：默认固定"现在"，让限流器行为可预测
        long fakeNowMs = 1_763_100_000_000;
        _throttle = new TelegramThrottle(25, 1, () => fakeNowMs);

        _mockSystemOptionRepo = new Mock<ISystemOptionRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockAttachmentRepo = new Mock<IAttachmentRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockMailboxRepo = new Mock<IMailboxRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _mockConfiguration.Setup(c => c["App:BaseUrl"]).Returns("https://css.example.com");
        _mockEnvironment.Setup(e => e.ContentRootPath).Returns(Path.Combine(Path.GetTempPath(), "telegram-tests"));
        _mockCurrentUserAccessor.Setup(u => u.UserId).Returns(7);
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.IsAny<string>()))
            .ReturnsAsync((SystemOption?)null);
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) =>
            {
                _addedThreads.Add(t);
                t.Id = _addedThreads.Count + 900;
                return t;
            });

        _service = new TelegramService(
            _mockUnitOfWork.Object,
            _mockEncryptionService.Object,
            _mockSettingsProvider.Object,
            _mockClient.Object,
            _mockCurrentUserAccessor.Object,
            _mockNotificationPublisher.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEnvironment.Object,
            _mockPollingStatus.Object,
            _throttle,
            _mockWebhookService.Object);
    }

    /// <summary>默认基线：webhook 模式、已配置密钥、轮询删除开关关闭（避免每次保存都触发 setWebhook）</summary>
    private void SetupConfigured(TelegramOptions? options = null)
    {
        var effective = options ?? new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModeWebhook,
            BotToken = BotToken,
            WebhookSecretToken = "registered-secret",
            BotUsername = "css_bot",
            MailboxId = 1,
            DeleteWebhookOnSwitch = false
        };

        _mockSettingsProvider.SetupGet(s => s.Current).Returns(effective);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(effective);
    }

    private void SetupConversationWithCustomer(int conversationId = 10, string? channelId = ChatId)
    {
        var conversation = new Conversation
        {
            Id = conversationId,
            CustomerId = channelId == null ? 0 : 3,
            SourceType = SourceType.Telegram,
            Status = ConversationStatus.Active,
            MailboxId = 1
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(conversationId)).ReturnsAsync(conversation);

        if (channelId != null)
        {
            var customer = new Customer { Id = 3, Channel = "telegram", ChannelId = channelId };
            _mockCustomerRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Customer, bool>>>()))
                .ReturnsAsync(new List<Customer> { customer });
        }
        else
        {
            _mockCustomerRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Customer, bool>>>()))
                .ReturnsAsync(new List<Customer>());
        }

        // 入站线程：MessageId 存去重键 chat.id:message_id，To 存 chat.id
        var inbound = new ConversationThread
        {
            Id = 55,
            ConversationId = conversationId,
            Type = ThreadType.Customer,
            State = ThreadState.Active,
            SourceType = SourceType.Telegram,
            From = ChatId,
            To = ChatId,
            MessageId = $"{ChatId}:42",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(channelId == null ? new List<ConversationThread>() : new List<ConversationThread> { inbound });
    }

    private static TelegramSendResult Ok(string messageId) => new() { Success = true, MessageId = messageId };

    #region GetSettingsAsync：脱敏（密钥永不回显）

    [Fact]
    public async Task GetSettingsAsync_FullConfig_ReturnsMaskedSettingsAndDeepLink()
    {
        SetupConfigured(new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModeWebhook,
            BotToken = BotToken,
            WebhookSecretToken = "registered-secret",
            LinkTokenHmacKey = "hmac-key",
            ApiBaseUrl = "https://api.telegram.org",
            BotUsername = "css_bot",
            MailboxId = 4,
            AcceptGroupMessages = true,
            MaxWebhookConnections = 20,
            PollingTimeout = 25,
            PollingLimit = 50,
            AllowedUpdates = ["message", "edited_message"],
            MaxMessageChars = 4096,
            RateLimitGlobalPerSeconds = 25,
            RateLimitPerChatPerSeconds = 1,
            ConversationMatchWindowHours = 720,
            AutoReplyEnabled = true,
            AutoReplyMessage = "已收到"
        });

        var result = await _service.GetSettingsAsync();

        result.Enabled.Should().BeTrue();
        result.Mode.Should().Be("webhook");
        result.ApiBaseUrl.Should().Be("https://api.telegram.org");
        result.BotUsername.Should().Be("css_bot");
        result.MailboxId.Should().Be(4);
        result.AcceptGroupMessages.Should().BeTrue();
        result.MaxWebhookConnections.Should().Be(20);
        result.AllowedUpdates.Should().BeEquivalentTo(["message", "edited_message"]);
        result.ConversationMatchWindowHours.Should().Be(720);
        // 机器人不能主动发起私聊 ⇒ 深链前缀是坐席必须可见的硬约束
        result.DeepLinkPrefix.Should().Be("https://t.me/css_bot");
        result.CallbackUrl.Should().Be("https://css.example.com/api/telegram/webhook");

        // 密钥只回报"是否已配置"，DTO 里没有任何密钥字段
        result.HasBotToken.Should().BeTrue();
        result.HasWebhookSecretToken.Should().BeTrue();
        result.HasLinkTokenHmacKey.Should().BeTrue();
    }

    [Fact]
    public async Task GetSettingsAsync_EmptyConfig_FlagsAreFalseAndNoDeepLink()
    {
        SetupConfigured(new TelegramOptions());

        var result = await _service.GetSettingsAsync();

        result.HasBotToken.Should().BeFalse();
        result.HasWebhookSecretToken.Should().BeFalse();
        result.HasLinkTokenHmacKey.Should().BeFalse();
        result.DeepLinkPrefix.Should().BeNull();
        result.Mode.Should().Be(TelegramOptions.ModeWebhook);
        result.MaxMessageChars.Should().Be(4096);
    }

    [Fact]
    public async Task GetSettingsAsync_MissingAppBaseUrl_CallbackUrlIsNullInsteadOfThrowing()
    {
        _mockConfiguration.Setup(c => c["App:BaseUrl"]).Returns((string?)null);
        SetupConfigured(new TelegramOptions { BotToken = BotToken, Enabled = true });

        var result = await _service.GetSettingsAsync();

        result.CallbackUrl.Should().BeNull();
        result.HasBotToken.Should().BeTrue();
    }

    [Fact]
    public async Task GetSettingsAsync_ExplicitWebhookUrl_WinsOverBaseUrlPlusCallbackPath()
    {
        SetupConfigured(new TelegramOptions
        {
            BotToken = BotToken,
            Enabled = true,
            WebhookUrl = "https://tunnel.example.dev/api/telegram/webhook"
        });

        var result = await _service.GetSettingsAsync();

        result.CallbackUrl.Should().Be("https://tunnel.example.dev/api/telegram/webhook");
    }

    #endregion

    #region UpdateSettingsAsync：ENC: 加密 + 不回显 + 模式同步

    [Fact]
    public async Task UpdateSettingsAsync_Secrets_AreEncryptedWithEncPrefix()
    {
        SetupConfigured();
        _mockEncryptionService.Setup(e => e.Encrypt(BotToken)).Returns("cipher_token");
        _mockEncryptionService.Setup(e => e.Encrypt("new-webhook-secret")).Returns("cipher_secret");
        _mockEncryptionService.Setup(e => e.Encrypt("new-hmac-key")).Returns("cipher_hmac");

        var result = await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest
        {
            BotToken = BotToken,
            WebhookSecretToken = "new-webhook-secret",
            LinkTokenHmacKey = "new-hmac-key",
            Enabled = true
        });

        _mockEncryptionService.Verify(e => e.Encrypt(BotToken), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.BotToken && o.Value == "ENC:cipher_token" && o.Category == "telegram")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.WebhookSecretToken && o.Value == "ENC:cipher_secret")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.LinkTokenHmacKey && o.Value == "ENC:cipher_hmac")), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockSettingsProvider.Verify(s => s.RefreshAsync(), Times.Once);
        // 回读仍是脱敏视图
        result.HasBotToken.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettingsAsync_NullSecrets_NeverEncrypt()
    {
        SetupConfigured(new TelegramOptions { Enabled = true, BotToken = BotToken, WebhookSecretToken = "old" });

        await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest
        {
            BotToken = null,
            WebhookSecretToken = null,
            LinkTokenHmacKey = null,
            BotUsername = "css_bot",
            Enabled = true
        });

        _mockEncryptionService.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ClearBotToken_DeletesStoredRowInsteadOfWritingEmptyValue()
    {
        // 审计 #5：前端把 BotToken 输入框清空并提交，库里旧密文必须真的消失，否则泄露的 Token 撤不掉。
        // 基线（appsettings / TELEGRAM__BotToken）不受本接口影响，这里只断言"库里的覆盖值被删除"。
        var effective = new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModePolling,
            BotToken = BotToken,
            DeleteWebhookOnSwitch = false
        };
        _mockSettingsProvider.SetupGet(s => s.Current).Returns(effective);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(() => effective);
        // 模拟 provider 重新加载：删行后读不到覆盖值 ⇒ BotToken 回到基线（测试基线为空）
        _mockSettingsProvider.Setup(s => s.RefreshAsync()).Returns(() =>
        {
            effective.BotToken = null;
            return Task.CompletedTask;
        });

        var stored = new SystemOption { Name = TelegramOptionKeys.BotToken, Value = "ENC:cipher_token", Category = "telegram" };
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(TelegramOptionKeys.BotToken)).ReturnsAsync(stored);

        var result = await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest { ClearBotToken = true });

        _mockSystemOptionRepo.Verify(r => r.DeleteAsync(stored), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.InvalidateCache(TelegramOptionKeys.BotToken), Times.Once);
        // 清空绝不能落成"新增空值行"或"把值写成空串"：空串会被 provider 当成"没有覆盖"而退回基线
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.IsAny<SystemOption>()), Times.Never);
        _mockEncryptionService.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        // 清除后 IsConfigured=false：不登记 webhook，也不产生任何 API 调用
        result.HasBotToken.Should().BeFalse();
        _mockClient.Verify(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(),
            It.IsAny<IReadOnlyList<string>?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ClearWebhookSecretToken_DeletesRowAndNeverReusesOldSecret()
    {
        // 审计 #5：WebhookSecretToken 清空后，旧的 X-Telegram-Bot-Api-Secret-Token 必须不再被接受。
        // Api 侧 TelegramController.ReceiveWebhook 在读取请求体之前就返回 Unauthorized（fail-closed，等价于关闭端点），
        // 这里断言服务侧确实把覆盖值删掉、且没有把旧密文重新写回去。
        var effective = new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModePolling,
            BotToken = BotToken,
            WebhookSecretToken = "registered-secret",
            DeleteWebhookOnSwitch = false
        };
        _mockSettingsProvider.SetupGet(s => s.Current).Returns(effective);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(() => effective);
        _mockSettingsProvider.Setup(s => s.RefreshAsync()).Returns(() =>
        {
            effective.WebhookSecretToken = null;
            return Task.CompletedTask;
        });

        var stored = new SystemOption { Name = TelegramOptionKeys.WebhookSecretToken, Value = "ENC:cipher_secret", Category = "telegram" };
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(TelegramOptionKeys.WebhookSecretToken)).ReturnsAsync(stored);

        var result = await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest
        {
            ClearWebhookSecretToken = true,
            WebhookSecretToken = "   "
        });

        _mockSystemOptionRepo.Verify(r => r.DeleteAsync(stored), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.WebhookSecretToken)), Times.Never);
        // Clear 开关优先于字段值：即使同时传了空白值也不会走"非空才覆盖"的分支
        _mockEncryptionService.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);
        result.HasWebhookSecretToken.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSettingsAsync_ClearLinkTokenHmacKey_DeletesRowAndReportsUnconfigured()
    {
        var effective = new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModePolling,
            BotToken = BotToken,
            LinkTokenHmacKey = "hmac-key",
            DeleteWebhookOnSwitch = false
        };
        _mockSettingsProvider.SetupGet(s => s.Current).Returns(effective);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(() => effective);
        _mockSettingsProvider.Setup(s => s.RefreshAsync()).Returns(() =>
        {
            effective.LinkTokenHmacKey = null;
            return Task.CompletedTask;
        });

        var stored = new SystemOption { Name = TelegramOptionKeys.LinkTokenHmacKey, Value = "ENC:cipher_hmac", Category = "telegram" };
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(TelegramOptionKeys.LinkTokenHmacKey)).ReturnsAsync(stored);

        var result = await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest { ClearLinkTokenHmacKey = true });

        _mockSystemOptionRepo.Verify(r => r.DeleteAsync(stored), Times.Once);
        _mockEncryptionService.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);
        result.HasLinkTokenHmacKey.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSettingsAsync_ClearFlagsDefaultFalse_KeepLegacySemantics()
    {
        // 三个开关默认 false ⇒ 老前端（不发送 Clear*）的语义完全不变：null / 空白 = 不修改
        SetupConfigured(new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModeWebhook, BotToken = BotToken, DeleteWebhookOnSwitch = false });

        await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest { BotUsername = "css_bot" });

        _mockSystemOptionRepo.Verify(r => r.DeleteAsync(It.IsAny<SystemOption>()), Times.Never);
        _mockSystemOptionRepo.Verify(r => r.InvalidateCache(TelegramOptionKeys.BotToken), Times.Never);
        _mockSystemOptionRepo.Verify(r => r.InvalidateCache(TelegramOptionKeys.WebhookSecretToken), Times.Never);
        _mockSystemOptionRepo.Verify(r => r.InvalidateCache(TelegramOptionKeys.LinkTokenHmacKey), Times.Never);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ExistingOption_UpdatesTrackedEntityInsteadOfInserting()
    {
        SetupConfigured();
        var existing = new SystemOption { Id = 11, Name = TelegramOptionKeys.BotUsername, Value = "old_bot" };
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(TelegramOptionKeys.BotUsername)).ReturnsAsync(existing);

        await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest { BotUsername = "@new_bot" });

        // 必须是受跟踪实体（AsNoTracking 的结果 Detached，不会发出 UPDATE）
        existing.Value.Should().Be("new_bot");
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => o.Name == TelegramOptionKeys.BotUsername)), Times.Never);
    }

    [Fact]
    public async Task UpdateSettingsAsync_NormalizesUrlsUsernamesAndBools()
    {
        SetupConfigured();

        await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest
        {
            ApiBaseUrl = "https://bot-api.internal:8081/",
            BotUsername = "@my_help_bot",
            AcceptGroupMessages = true,
            UseLocalBotApiServer = true,
            AllowedUpdates = ["message", " channel_post ", ""],
            MaxMessageChars = 4096,
            RateLimitGlobalPerSeconds = 30,
            ConversationMatchWindowHours = 24
        });

        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.ApiBaseUrl && o.Value == "https://bot-api.internal:8081")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.BotUsername && o.Value == "my_help_bot")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.AcceptGroupMessages && o.Value == "true")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.UseLocalBotApiServer && o.Value == "true")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.AllowedUpdates && o.Value == "message,channel_post")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.ConversationMatchWindowHours && o.Value == "24")), Times.Once);
    }

    [Fact]
    public async Task UpdateSettingsAsync_SwitchToPolling_DeletesWebhookToAvoid409()
    {
        // 409 Conflict: can't use getUpdates while webhook is active ⇒ 切 polling 前必须 deleteWebhook
        var webhookState = new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModeWebhook, BotToken = BotToken, DeleteWebhookOnSwitch = true };
        var pollingState = new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModePolling, BotToken = BotToken, DeleteWebhookOnSwitch = true };
        _mockSettingsProvider.SetupSequence(s => s.Current).Returns(webhookState).Returns(pollingState);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(pollingState);
        _mockClient.Setup(c => c.DeleteWebhookAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest { Mode = TelegramOptions.ModePolling });

        _mockClient.Verify(c => c.DeleteWebhookAsync(false, It.IsAny<CancellationToken>()), Times.Once);
        _mockClient.Verify(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Mode.Should().Be("polling");
    }

    [Fact]
    public async Task UpdateSettingsAsync_SwitchToPolling_KeepsWebhookWhenDeleteFlagOff()
    {
        var webhookState = new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModeWebhook, BotToken = BotToken, DeleteWebhookOnSwitch = false };
        var pollingState = new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModePolling, BotToken = BotToken, DeleteWebhookOnSwitch = false };
        _mockSettingsProvider.SetupSequence(s => s.Current).Returns(webhookState).Returns(pollingState);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(pollingState);

        await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest { Mode = TelegramOptions.ModePolling });

        _mockClient.Verify(c => c.DeleteWebhookAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WebhookModeUnchangedAndEnabled_ReRegistersWebhookIdempotently()
    {
        var before = new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModeWebhook, BotToken = BotToken, WebhookSecretToken = "registered-secret" };
        var after = new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModeWebhook, BotToken = BotToken, WebhookSecretToken = "registered-secret", MaxWebhookConnections = 12 };
        _mockSettingsProvider.SetupSequence(s => s.Current).Returns(before).Returns(after);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(after);
        _mockClient.Setup(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest { MaxWebhookConnections = 12 });

        _mockClient.Verify(c => c.SetWebhookAsync(
            "https://css.example.com/api/telegram/webhook",
            "registered-secret",
            12,
            It.IsAny<IReadOnlyList<string>?>(),
            false,
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.MaxWebhookConnections && o.Value == "12")), Times.Once);
    }

    [Fact]
    public async Task UpdateSettingsAsync_SetWebhookFailure_StillSavesConfiguration()
    {
        SetupConfigured();
        _mockClient.Setup(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network down"));

        var result = await _service.UpdateSettingsAsync(new UpdateTelegramSettingsRequest { Enabled = true });

        result.HasBotToken.Should().BeTrue();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region GetStatusAsync

    [Fact]
    public async Task GetStatusAsync_WebhookMode_ReportsGetMeAndWebhookInfo()
    {
        SetupConfigured(new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModeWebhook, BotToken = BotToken });
        _mockClient.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramBotInfo { Id = 777, IsBot = true, Username = "css_bot", CanReadAllGroupMessages = false });
        _mockClient.Setup(c => c.GetWebhookInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramWebhookInfo { Url = "https://css.example.com/api/telegram/webhook", PendingUpdateCount = 9, LastErrorMessage = "connection refused", LastErrorTime = 1763100000 });

        var result = await _service.GetStatusAsync();

        result.Configured.Should().BeTrue();
        result.ApiReachable.Should().BeTrue();
        result.BotUsername.Should().Be("css_bot");
        // can_read_all_group_messages=false ⇒ 隐私模式开启，群里只收到命令/@
        result.CanReadAllGroupMessages.Should().BeFalse();
        result.WebhookRegistered.Should().BeTrue();
        result.PendingUpdateCount.Should().Be(9);
        result.LastErrorMessage.Should().Be("connection refused");
        result.PollingRunning.Should().BeFalse();
        _mockClient.Verify(c => c.GetWebhookInfoAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStatusAsync_PollingMode_ReportsHostedServiceStateAndCursor()
    {
        SetupConfigured(new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModePolling, BotToken = BotToken });
        _mockClient.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramBotInfo { Id = 1, Username = "css_bot" });
        _mockPollingStatus.SetupGet(s => s.IsRunning).Returns(true);
        _mockPollingStatus.SetupGet(s => s.LastError).Returns("getUpdates 409 conflict");
        _mockPollingStatus.SetupGet(s => s.LastActivityAt).Returns(new DateTime(2026, 11, 16, 3, 0, 0, DateTimeKind.Utc));
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(TelegramOptionKeys.PollingOffsetCursor)).ReturnsAsync("987654322");

        var result = await _service.GetStatusAsync();

        result.Mode.Should().Be("polling");
        result.PollingRunning.Should().BeTrue();
        result.PollingLastErrorMessage.Should().Be("getUpdates 409 conflict");
        // 游标是 update_id+1（update_id 会重置 ⇒ 只作游标）
        result.PollingOffset.Should().Be(987654322);
        result.WebhookRegistered.Should().BeFalse();
        _mockClient.Verify(c => c.GetWebhookInfoAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatusAsync_NotConfigured_SkipsAllProbes()
    {
        SetupConfigured(new TelegramOptions { Enabled = false });
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var result = await _service.GetStatusAsync();

        result.Configured.Should().BeFalse();
        result.ApiReachable.Should().BeFalse();
        _mockClient.Verify(c => c.GetMeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatusAsync_GetMeThrows_IsReportedAsUnreachableNotCrash()
    {
        SetupConfigured(new TelegramOptions { Enabled = true, BotToken = BotToken });
        _mockClient.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("dns fail"));

        var result = await _service.GetStatusAsync();

        result.ApiReachable.Should().BeFalse();
        result.Configured.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_CountsDistinctUnreachableConversations()
    {
        SetupConfigured(new TelegramOptions { Enabled = true, BotToken = BotToken, Mode = TelegramOptions.ModePolling });
        _mockClient.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>())).ReturnsAsync((TelegramBotInfo?)null);
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>
            {
                new() { ConversationId = 10, MessageId = TelegramUnreachableState.MarkMessageId(10), Type = ThreadType.Note, IsInternal = true },
                new() { ConversationId = 11, MessageId = TelegramUnreachableState.MarkMessageId(11), Type = ThreadType.Note, IsInternal = true },
                new() { ConversationId = 10, MessageId = TelegramUnreachableState.MarkMessageId(10), Type = ThreadType.Note, IsInternal = true }
            });

        var result = await _service.GetStatusAsync();

        result.UnreachableConversationCount.Should().Be(2);
    }

    #endregion

    #region ReplyAsync：无窗口限制 + 4096 分片 + 错误码分类

    [Fact]
    public async Task ReplyAsync_Disabled_DoesNotCallTelegram()
    {
        SetupConfigured(new TelegramOptions { Enabled = false, Mode = TelegramOptions.ModeWebhook });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("未启用或未配置");
        VerifySendNeverCalled();
    }

    [Fact]
    public async Task ReplyAsync_MissingBotToken_DoesNotCallTelegram()
    {
        SetupConfigured(new TelegramOptions { Enabled = true });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        VerifySendNeverCalled();
    }

    [Fact]
    public async Task ReplyAsync_EmptyContentAndNoAttachments_IsRejected()
    {
        SetupConfigured();

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "   " }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("回复内容为空");
    }

    [Fact]
    public async Task ReplyAsync_ConversationNotFound_IsRejected()
    {
        SetupConfigured();
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999)).ReturnsAsync((Conversation?)null);

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 999, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("会话不存在");
    }

    [Fact]
    public async Task ReplyAsync_ChatIdUnresolvable_IsRejectedWithoutSending()
    {
        SetupConfigured();
        SetupConversationWithCustomer(10, channelId: null);

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("chat_id");
        VerifySendNeverCalled();
    }

    [Fact]
    public async Task ReplyAsync_ShortText_SendsOnceAndRecordsOutboundThread()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(ChatId, "你好呀", null, It.IsAny<CancellationToken>())).ReturnsAsync(Ok("4321"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "你好呀" }, 7);

        result.Sent.Should().BeTrue();
        result.ThreadCreated.Should().BeTrue();
        result.PartsSent.Should().Be(1);
        result.MessageId.Should().Be("4321");
        result.RetryAfterSeconds.Should().BeNull();
        result.MarkedUnreachable.Should().BeFalse();

        var outbound = _addedThreads.Should().ContainSingle().Subject;
        outbound.Type.Should().Be(ThreadType.Message);
        outbound.SourceType.Should().Be(SourceType.Telegram);
        outbound.SourceVia.Should().Be(SourceVia.User);
        outbound.SendStatus.Should().Be(SendStatus.Sent);
        outbound.To.Should().Be(ChatId);
        // 存平台 message_id 作为映射（不是去重键）
        outbound.MessageId.Should().Be("4321");
        outbound.CreatedByUserId.Should().Be(7);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockNotificationPublisher.Verify(n => n.NotifyMailboxAsync("1", "conversation_updated", It.IsAny<object>()), Times.Once);
        _mockWebhookService.Verify(w => w.TriggerAsync("conversation_updated", 1, It.IsAny<object>(), "Conversation", 10), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_AgentReplyIsAllowedYearsLater_BecauseTelegramHasNoWindow()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        // 入站消息很久之前：WhatsApp/Messenger 会因 24 小时窗口拒绝，Telegram 无窗口限制
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>
            {
                new()
                {
                    ConversationId = 10,
                    Type = ThreadType.Customer,
                    State = ThreadState.Active,
                    SourceType = SourceType.Telegram,
                    From = ChatId,
                    To = ChatId,
                    MessageId = $"{ChatId}:42",
                    CreatedAt = DateTime.UtcNow.AddDays(-30)
                }
            });
        _mockCustomerRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Customer, bool>>>()))
            .ReturnsAsync(new List<Customer> { new() { Id = 3, Channel = "telegram", ChannelId = ChatId } });
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok("9001"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "很久之后的回复" }, 7);

        result.Sent.Should().BeTrue();
        _mockClient.Verify(c => c.SendTextMessageAsync(ChatId, "很久之后的回复", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_QuoteLastCustomerMessage_ForwardsNumericMessageIdOnly()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(ChatId, "引用回复", "42", It.IsAny<CancellationToken>())).ReturnsAsync(Ok("4322"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest
        {
            ConversationId = 10,
            Content = "引用回复",
            QuoteLastCustomerMessage = true
        }, 7);

        // 站内 MessageId 存的是去重键 "chat.id:message_id"，reply_parameters 只能带纯 message_id
        result.Sent.Should().BeTrue();
        _mockClient.Verify(c => c.SendTextMessageAsync(ChatId, "引用回复", "42", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_ParseModeHtml_ForwardsNormalizedParseMode()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(
                ChatId, "加粗回复", null, It.IsAny<CancellationToken>(), null, "HTML"))
            .ReturnsAsync(Ok("4323"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest
        {
            ConversationId = 10,
            Content = "加粗回复",
            ParseMode = " HTML "
        }, 7);

        result.Sent.Should().BeTrue();
        // 归一化后应为规范值 "HTML"，且原样透传给 Bot API 层
        _mockClient.Verify(c => c.SendTextMessageAsync(
            ChatId, "加粗回复", null, It.IsAny<CancellationToken>(), null, "HTML"), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_ParseModeNoneOrNull_DoesNotSendParseMode()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
                It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync(Ok("4324"));

        var noneResult = await _service.ReplyAsync(new TelegramReplyRequest
        {
            ConversationId = 10,
            Content = "纯文本一",
            ParseMode = "none"
        }, 7);
        var nullResult = await _service.ReplyAsync(new TelegramReplyRequest
        {
            ConversationId = 10,
            Content = "纯文本二"
        }, 7);

        noneResult.Sent.Should().BeTrue();
        nullResult.Sent.Should().BeTrue();
        // 任何一次下发都不得带 parse_mode（null ⇒ 不写该字段）
        _mockClient.Verify(c => c.SendTextMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
            It.IsAny<int?>(), It.Is<string?>(p => p != null)), Times.Never);
        _mockClient.Verify(c => c.SendTextMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
            It.IsAny<int?>(), null), Times.Exactly(2));
    }

    [Fact]
    public async Task ReplyAsync_UnknownParseMode_NormalizesToNullAndWarns()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(
                ChatId, "未知格式回复", null, It.IsAny<CancellationToken>(), null, null))
            .ReturnsAsync(Ok("4325"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest
        {
            ConversationId = 10,
            Content = "未知格式回复",
            ParseMode = "MarkdownV3"
        }, 7);

        result.Sent.Should().BeTrue();
        // 无法识别的值按纯文本发送，且要留下一条 warning 便于排查
        _mockClient.Verify(c => c.SendTextMessageAsync(
            ChatId, "未知格式回复", null, It.IsAny<CancellationToken>(), null, null), Times.Once);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("parse_mode")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_LongText_SplitsAtMaxMessageCharsAndSendsEveryPart()
    {
        SetupConfigured(new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModeWebhook,
            BotToken = BotToken,
            MaxMessageChars = 20,
            RateLimitGlobalPerSeconds = 25,
            RateLimitPerChatPerSeconds = 5,
            MaxLocalBackoffSeconds = 10
        });
        SetupConversationWithCustomer();

        var sent = new List<string>();
        _mockClient.Setup(c => c.SendTextMessageAsync(ChatId, It.IsAny<string>(), null, It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync((string _, string body, string? _, CancellationToken _, int? _, string? _) =>
            {
                sent.Add(body);
                return Ok((4400 + sent.Count).ToString());
            });

        var longText = string.Join("\n", Enumerable.Repeat("长文本内容用于分片测试", 8));

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = longText }, 7);

        result.Sent.Should().BeTrue();
        result.PartsSent.Should().Be(sent.Count);
        sent.Should().HaveCountGreaterThan(1);
        sent.Should().OnlyContain(p => p.Length <= 20);
        // 只有首段引用客户原消息，避免刷屏式引用
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(sent.Count));
        _addedThreads.Should().ContainSingle().Subject.MessageId.Should().Be("4401");
        _addedThreads.Should().ContainSingle().Subject.Body.Should().Contain($"已分 {sent.Count} 段发送");
    }

    [Fact]
    public async Task ReplyAsync_BlockedByUser_MarksConversationUnreachableAndStopsRetrying()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramSendResult
            {
                Success = false,
                ErrorCode = 403,
                Blocked = true,
                Description = "Forbidden: bot was blocked by the user"
            });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.MarkedUnreachable.Should().BeTrue();
        result.ErrorMessage.Should().Contain("不可达");
        // 终态：只发一次，不重试，也不写出站线程
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _addedThreads.Should().ContainSingle().Subject.MessageId.Should().Be(TelegramUnreachableState.MarkMessageId(10));
        _addedThreads.Should().ContainSingle().Subject.Type.Should().Be(ThreadType.Note);
        _addedThreads.Should().ContainSingle().Subject.IsInternal.Should().BeTrue();
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_ChatNotFound_MarksUnreachableToo()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramSendResult
            {
                Success = false,
                ErrorCode = 400,
                Unreachable = true,
                Description = "Bad Request: chat not found"
            });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.MarkedUnreachable.Should().BeTrue();
        result.Sent.Should().BeFalse();
    }

    [Fact]
    public async Task ReplyAsync_FortysThreeLongRetryAfter_DelegatesBackoffToTheQueue()
    {
        SetupConfigured(new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModeWebhook,
            BotToken = BotToken,
            MaxLocalBackoffSeconds = 10
        });
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramSendResult
            {
                Success = false,
                ErrorCode = 429,
                Description = "Too Many Requests: retry after 37",
                RetryAfterSeconds = 37
            });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        // 长退避不在 worker 内 sleep：把 retry_after 交回消息队列延后重试
        result.RetryAfterSeconds.Should().Be(37);
        result.MarkedUnreachable.Should().BeFalse();
        _addedThreads.Should().BeEmpty();
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_FortysTwoChatScopedShortRetryAfter_RetriesOnceThenSucceeds()
    {
        SetupConfigured(new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModeWebhook,
            BotToken = BotToken,
            MaxLocalBackoffSeconds = 30
        });
        SetupConversationWithCustomer();

        var calls = 0;
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync((string _, string _, string? _, CancellationToken _, int? _, string? _) => ++calls == 1
                ? new TelegramSendResult { Success = false, ErrorCode = 420, Description = "Flood Error: retry after 1", RetryAfterSeconds = 1 }
                : Ok("4500"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeTrue();
        calls.Should().Be(2);
        result.MessageId.Should().Be("4500");
    }

    [Fact]
    public async Task ReplyAsync_LocalThrottleAlreadyBlockingChat_DefersWithoutCallingTelegram()
    {
        SetupConfigured(new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModeWebhook,
            BotToken = BotToken,
            RateLimitGlobalPerSeconds = 25,
            RateLimitPerChatPerSeconds = 1,
            MaxLocalBackoffSeconds = 0
        });
        SetupConversationWithCustomer();
        // 先登记一次发送，占用"每 chat 1 条/秒"配额
        _throttle.RegisterSend(long.Parse(ChatId));
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok("4600"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.RetryAfterSeconds.Should().BeGreaterThan(0);
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_MigrateToChatId_RewritesCustomerChannelIdAndStops()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        var customer = new Customer { Id = 3, Channel = "telegram", ChannelId = "-100111" };
        _mockCustomerRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Customer, bool>>>()))
            .ReturnsAsync(new List<Customer> { customer });
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramSendResult
            {
                Success = false,
                ErrorCode = 400,
                Description = "Bad Request: chat moved to supergroup",
                MigrateToChatId = -100222
            });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        // 群升级为 supergroup ⇒ chat.id 变化，站内绑定必须改写
        customer.ChannelId.Should().Be("-100222");
        result.ErrorMessage.Should().Contain("已迁移").And.Contain("-100222");
        result.Sent.Should().BeFalse();
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_InvalidTokenReportsConfigPageActionAndSkipsThread()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramSendResult { Success = false, ErrorCode = 401, InvalidToken = true, Description = "Unauthorized" });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("401");
        result.MarkedUnreachable.Should().BeFalse();
        _addedThreads.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplyAsync_Conflict409_TellsOperatorToDeleteWebhook()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramSendResult { Success = false, ErrorCode = 409, Conflict = true, Description = "Conflict: terminated by other getUpdates request" });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("deleteWebhook");
    }

    [Fact]
    public async Task ReplyAsync_MediaFailureKeepsTextSentAndReportsAttachmentError()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok("4700"));
        _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<Attachment>());
        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<Attachment>());

        var result = await _service.ReplyAsync(new TelegramReplyRequest
        {
            ConversationId = 10,
            Content = "带不存在的附件",
            AttachmentIds = [404]
        }, 7);

        result.Sent.Should().BeTrue();
        result.ErrorMessage.Should().Contain("404");
        _addedThreads.Should().ContainSingle().Subject.HasAttachments.Should().BeFalse();
    }

    [Fact]
    public async Task ReplyAsync_OnlyAttachmentsAllowedByTypeSwitch_UsesDocumentForOtherMime()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        var tempDir = Path.Combine(Path.GetTempPath(), "telegram-tests", "2026/11/16");
        Directory.CreateDirectory(tempDir);
        var storedName = "invoice.pdf";
        var filePath = Path.Combine(tempDir, storedName);
        await File.WriteAllTextAsync(filePath, "fake-pdf");
        try
        {
            _mockConfiguration.Setup(c => c["Attachment:UploadPath"]).Returns(Path.Combine(Path.GetTempPath(), "telegram-tests"));
            _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
                .ReturnsAsync(new List<Attachment>
                {
                    new() { Id = 1, FileName = "发票.pdf", StoredFileName = storedName, FileDir = "2026/11/16", MimeType = "application/pdf", Type = AttachmentType.Other, Size = 9 }
                });
            _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>()))
                .ReturnsAsync(new List<Attachment> { new() { Id = 1, FileName = "发票.pdf", StoredFileName = storedName, FileDir = "2026/11/16", MimeType = "application/pdf", Type = AttachmentType.Other, Size = 9 } });
            _mockClient.Setup(c => c.UploadAndSendMediaAsync(ChatId, "document", It.IsAny<byte[]>(), "发票.pdf", "application/pdf", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ok("4800"));

            var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, AttachmentIds = [1] }, 7);

            result.Sent.Should().BeTrue();
            result.PartsSent.Should().Be(1);
            _addedThreads.Should().ContainSingle().Subject.HasAttachments.Should().BeTrue();
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ReplyAsync_RejectsPathTraversalOutsideUploadRoot()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockConfiguration.Setup(c => c["Attachment:UploadPath"]).Returns(Path.Combine(Path.GetTempPath(), "telegram-tests"));
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok("4900"));
        _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>
            {
                new() { Id = 2, FileName = "etc", StoredFileName = "..\\..\\windows\\win.ini", FileDir = "2026-11-16", MimeType = "application/octet-stream", Type = AttachmentType.Other, Size = 1 }
            });

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi", AttachmentIds = [2] }, 7);

        result.ErrorMessage.Should().Contain("路径非法");
        _mockClient.Verify(c => c.UploadAndSendMediaAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_ClearsExistingUnreachableMarkAfterSuccess()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        var mark = new ConversationThread
        {
            Id = 61,
            ConversationId = 10,
            Type = ThreadType.Note,
            State = ThreadState.Active,
            IsInternal = true,
            MessageId = TelegramUnreachableState.MarkMessageId(10),
            SourceType = SourceType.Telegram
        };
        // 第二次 FindAsync（清标记）才返回标记线程；第一次返回入站线程
        var calls = 0;
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(() =>
            {
                calls++;
                return calls == 1
                    ? new List<ConversationThread>
                    {
                        new() { ConversationId = 10, Type = ThreadType.Customer, State = ThreadState.Active, SourceType = SourceType.Telegram, From = ChatId, To = ChatId, MessageId = $"{ChatId}:42", CreatedAt = DateTime.UtcNow }
                    }
                    : new List<ConversationThread> { mark };
            });
        _mockCustomerRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Customer, bool>>>()))
            .ReturnsAsync(new List<Customer> { new() { Id = 3, Channel = "telegram", ChannelId = ChatId } });
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok("5000"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "客户已解除拉黑" }, 7);

        result.Sent.Should().BeTrue();
        mark.State.Should().Be(ThreadState.Deleted);
    }

    [Fact]
    public async Task ReplyAsync_ThreadPersistFails_ReportsSentButNotThreadCreated()
    {
        SetupConfigured();
        SetupConversationWithCustomer();
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok("5100"));
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>())).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _service.ReplyAsync(new TelegramReplyRequest { ConversationId = 10, Content = "hi" }, 7);

        result.Sent.Should().BeTrue();
        result.ThreadCreated.Should().BeFalse();
        result.MessageId.Should().Be("5100");
    }

    #endregion

    #region SetupWebhookAsync / RemoveWebhookAsync / ClearUnreachableMarkAsync / TestConnectionAsync

    [Fact]
    public async Task SetupWebhookAsync_WithoutConfiguredSecret_GeneratesAndEncryptsOne()
    {
        SetupConfigured(new TelegramOptions { Enabled = true, Mode = TelegramOptions.ModeWebhook, BotToken = BotToken });
        string? encryptedInput = null;
        _mockEncryptionService.Setup(e => e.Encrypt(It.IsAny<string>()))
            .Returns((string plain) =>
            {
                encryptedInput = plain;
                return "cipher_" + plain.Length;
            });
        _mockClient.Setup(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.SetupWebhookAsync(new TelegramWebhookSetupRequest { WebhookUrl = "https://css.example.com/api/telegram/webhook", DropPendingUpdates = true });

        result.Success.Should().BeTrue();
        // Telegram 无请求体签名，secret_token 是唯一来源校验 ⇒ 未配置就必须生成
        // 32 随机字节 → base64url 去掉 '=' 填充 = 43 字符（官方 secret_token 上限 256，够长且无填充歧义）
        encryptedInput.Should().NotBeNull().And.HaveLength(43);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == TelegramOptionKeys.WebhookSecretToken && o.Value.StartsWith("ENC:cipher_"))), Times.Once);
        _mockClient.Verify(c => c.SetWebhookAsync(
            "https://css.example.com/api/telegram/webhook",
            encryptedInput,
            40,
            It.IsAny<IReadOnlyList<string>?>(),
            true,
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetupWebhookAsync_NotConfigured_FailsFast()
    {
        SetupConfigured(new TelegramOptions { Enabled = true });

        var result = await _service.SetupWebhookAsync(null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("BotToken");
        _mockClient.Verify(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetupWebhookAsync_PassesCertificateAndIpAddressForLocalBotApiServer()
    {
        SetupConfigured(new TelegramOptions
        {
            Enabled = true,
            Mode = TelegramOptions.ModeWebhook,
            BotToken = BotToken,
            WebhookSecretToken = "registered-secret",
            WebhookCertificate = "-----BEGIN CERTIFICATE-----",
            WebhookIpAddress = "203.0.113.10",
            MaxWebhookConnections = 8
        });
        _mockClient.Setup(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.SetupWebhookAsync(new TelegramWebhookSetupRequest { WebhookUrl = "http://local-bot-api.internal:8081/telegram" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("setWebhook");
        _mockClient.Verify(c => c.SetWebhookAsync(
            "http://local-bot-api.internal:8081/telegram",
            "registered-secret",
            8,
            It.IsAny<IReadOnlyList<string>?>(),
            false,
            "-----BEGIN CERTIFICATE-----",
            "203.0.113.10",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveWebhookAsync_CallsDeleteWebhookWithDropPendingFalse()
    {
        SetupConfigured();
        _mockClient.Setup(c => c.DeleteWebhookAsync(false, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.RemoveWebhookAsync();

        result.Success.Should().BeTrue();
        result.BotUsername.Should().Be("css_bot");
        _mockClient.Verify(c => c.DeleteWebhookAsync(false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearUnreachableMarkAsync_WhenMarkExists_DeletesItAndSaves()
    {
        SetupConfigured();
        var mark = new ConversationThread { Id = 71, ConversationId = 12, MessageId = TelegramUnreachableState.MarkMessageId(12), Type = ThreadType.Note, State = ThreadState.Active };
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread> { mark });

        var cleared = await _service.ClearUnreachableMarkAsync(12);

        cleared.Should().BeTrue();
        mark.State.Should().Be(ThreadState.Deleted);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearUnreachableMarkAsync_WhenNoMark_ReturnsFalseWithoutSaving()
    {
        SetupConfigured();
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var cleared = await _service.ClearUnreachableMarkAsync(12);

        cleared.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task TestConnectionAsync_UsesGetMeAndReportsBotIdentity()
    {
        SetupConfigured();
        _mockClient.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramBotInfo { Id = 777, IsBot = true, Username = "css_bot", CanReadAllGroupMessages = true });

        var result = await _service.TestConnectionAsync();

        result.Success.Should().BeTrue();
        result.BotId.Should().Be(777);
        result.BotUsername.Should().Be("css_bot");
    }

    [Fact]
    public async Task TestConnectionAsync_NotConfigured_DoesNotProbe()
    {
        SetupConfigured(new TelegramOptions { Enabled = true });

        var result = await _service.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("BotToken");
        _mockClient.Verify(c => c.GetMeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TestConnectionAsync_NullGetMe_ExplainsTokenOrBaseUrlProblem()
    {
        SetupConfigured();
        _mockClient.Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>())).ReturnsAsync((TelegramBotInfo?)null);

        var result = await _service.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("getMe").And.Contain("ApiBaseUrl");
    }

    #endregion

    private void VerifySendNeverCalled()
        => _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
}
