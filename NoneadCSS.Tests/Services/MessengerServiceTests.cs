using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
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
/// MessengerService 单元测试 — 配置读写（脱敏 + ENC: 加密）、状态查询、坐席回复（24h 窗口 / message_tag / 分片 / 错误码）。
/// 无网络：IMessengerClient 全部用 Moq 替身，LastError 用于模拟 Graph 错误码。
/// </summary>
public class MessengerServiceTests
{
    private const string PageId = "1055123456789012";
    private const string Psid = "1234567890123456";

    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IEncryptionService> _mockEncryptionService;
    private readonly Mock<IMessengerSettingsProvider> _mockSettingsProvider;
    private readonly Mock<IMessengerClient> _mockClient;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<ILogger<MessengerService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly MessengerService _service;

    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<IMailboxRepository> _mockMailboxRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    public MessengerServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockEncryptionService = new Mock<IEncryptionService>();
        _mockSettingsProvider = new Mock<IMessengerSettingsProvider>();
        _mockClient = new Mock<IMessengerClient>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockLogger = new Mock<ILogger<MessengerService>>();
        _mockConfiguration = new Mock<IConfiguration>();

        _mockSystemOptionRepo = new Mock<ISystemOptionRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockAttachmentRepo = new Mock<IAttachmentRepository>();
        _mockMailboxRepo = new Mock<IMailboxRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _mockConfiguration.Setup(c => c["App:BaseUrl"]).Returns("https://css.example.com");
        _mockConfiguration.Setup(c => c["App:FrontendUrl"]).Returns("https://css.example.com");
        _mockCurrentUserAccessor.Setup(u => u.UserId).Returns(1);
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.IsAny<string>()))
            .ReturnsAsync((SystemOption?)null);

        _service = new MessengerService(
            _mockUnitOfWork.Object,
            _mockEncryptionService.Object,
            _mockSettingsProvider.Object,
            _mockClient.Object,
            _mockCurrentUserAccessor.Object,
            _mockNotificationPublisher.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            new Mock<IHostEnvironment>().Object);
    }

    private void SetupConfigured(MessengerOptions? overrideOptions = null)
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(overrideOptions ?? new MessengerOptions
        {
            PageId = PageId,
            PageAccessToken = "page_token",
            AppSecret = "app_secret",
            VerifyToken = "verify_token",
            Enabled = true,
            MailboxId = 1
        });
    }

    private void SetupCustomerThread(DateTime createdAt, string from = Psid)
    {
        var thread = new ConversationThread
        {
            Id = 5,
            ConversationId = 10,
            Type = ThreadType.Customer,
            State = ThreadState.Active,
            SourceType = SourceType.Messenger,
            CreatedAt = createdAt,
            MessageId = "m_customer_1",
            From = from
        };

        _mockThreadRepo
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread> { thread });
    }

    private Conversation BuildConversation(int id = 10) => new()
    {
        Id = id,
        CustomerId = 1,
        SourceType = SourceType.Messenger,
        Status = ConversationStatus.Active,
        MailboxId = 1
    };

    #region GetSettingsAsync

    [Fact]
    public async Task GetSettingsAsync_FullConfig_ReturnsDesensitizedSettings()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new MessengerOptions
        {
            AppId = "1234567890",
            PageId = PageId,
            PageUsername = "my-page",
            PageAccessToken = "super_secret_token",
            AppSecret = "super_secret_app",
            VerifyToken = "super_verify",
            ApiVersion = "v25.0",
            MailboxId = 7,
            TextChunkChars = 1500,
            WindowHours = 24,
            MessageTagPolicy = "auto",
            MessageTag = "HUMAN_AGENT",
            AutoReplyEnabled = true,
            AutoReplyMessage = "已收到",
            Enabled = true
        });

        var result = await _service.GetSettingsAsync();

        result.AppId.Should().Be("1234567890");
        result.PageId.Should().Be(PageId);
        result.PageUsername.Should().Be("my-page");
        result.ApiVersion.Should().Be("v25.0");
        result.MailboxId.Should().Be(7);
        result.TextChunkChars.Should().Be(1500);
        result.WindowHours.Should().Be(24);
        result.MessageTagPolicy.Should().Be("auto");
        result.AutoReplyEnabled.Should().BeTrue();
        result.Enabled.Should().BeTrue();
        result.CallbackUrl.Should().Be("https://css.example.com/api/messenger");

        // 密钥只回报是否存在，不回显原值
        result.HasPageAccessToken.Should().BeTrue();
        result.HasAppSecret.Should().BeTrue();
        result.HasVerifyToken.Should().BeTrue();
    }

    [Fact]
    public async Task GetSettingsAsync_EmptyConfig_FlagsAreFalse()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new MessengerOptions());

        var result = await _service.GetSettingsAsync();

        result.HasPageAccessToken.Should().BeFalse();
        result.HasAppSecret.Should().BeFalse();
        result.HasVerifyToken.Should().BeFalse();
        result.Enabled.Should().BeFalse();
        // Graph 版本必须是配置值，默认 v25.0（不得硬编码 v24.0）
        result.ApiVersion.Should().Be("v25.0");
    }

    #endregion

    #region UpdateSettingsAsync

    [Fact]
    public async Task UpdateSettingsAsync_WithSecrets_EncryptsWithEncPrefix()
    {
        SetupConfigured();
        _mockEncryptionService.Setup(e => e.Encrypt("plain_token")).Returns("cipher_token");
        _mockEncryptionService.Setup(e => e.Encrypt("plain_secret")).Returns("cipher_secret");

        var request = new UpdateMessengerSettingsRequest
        {
            PageId = PageId,
            PageAccessToken = "plain_token",
            AppSecret = "plain_secret",
            Enabled = true
        };

        var result = await _service.UpdateSettingsAsync(request);

        _mockEncryptionService.Verify(e => e.Encrypt("plain_token"), Times.Once);
        _mockEncryptionService.Verify(e => e.Encrypt("plain_secret"), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == MessengerOptionKeys.PageAccessToken && o.Value == "ENC:cipher_token" && o.Category == "messenger")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == MessengerOptionKeys.AppSecret && o.Value == "ENC:cipher_secret")), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockSettingsProvider.Verify(s => s.RefreshAsync(), Times.Once);
        result.HasPageAccessToken.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettingsAsync_NullSecrets_DoesNotEncrypt()
    {
        SetupConfigured(new MessengerOptions { PageId = PageId, PageAccessToken = "old", AppSecret = "old", VerifyToken = "old", Enabled = true });

        var request = new UpdateMessengerSettingsRequest { PageId = PageId, PageAccessToken = null, AppSecret = null, Enabled = true };

        await _service.UpdateSettingsAsync(request);

        _mockEncryptionService.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSettingsAsync_ExistingOption_UpdatesTrackedEntity()
    {
        SetupConfigured();
        var existing = new SystemOption { Id = 10, Name = MessengerOptionKeys.PageId, Value = "old_page" };
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(MessengerOptionKeys.PageId)).ReturnsAsync(existing);

        await _service.UpdateSettingsAsync(new UpdateMessengerSettingsRequest { PageId = "new_page" });

        existing.Value.Should().Be("new_page");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateSettingsAsync_NumericAndBoolOptions_SavedAsInvariantStrings()
    {
        SetupConfigured();

        await _service.UpdateSettingsAsync(new UpdateMessengerSettingsRequest
        {
            TextChunkChars = 2000,
            WindowHours = 48,
            TypingIndicator = false,
            MarkSeen = true
        });

        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == MessengerOptionKeys.TextChunkChars && o.Value == "2000")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == MessengerOptionKeys.WindowHours && o.Value == "48")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == MessengerOptionKeys.TypingIndicator && o.Value == "false")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o =>
            o.Name == MessengerOptionKeys.MarkSeen && o.Value == "true")), Times.Once);
    }

    #endregion

    #region GetStatusAsync

    [Fact]
    public async Task GetStatusAsync_ConfiguredAndReachable_ReturnsOkWindow()
    {
        SetupConfigured();
        SetupCustomerThread(DateTime.UtcNow.AddHours(-2));
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.GetStatusAsync();

        result.Configured.Should().BeTrue();
        result.Enabled.Should().BeTrue();
        result.ApiReachable.Should().BeTrue();
        result.PageId.Should().Be(PageId);
        result.ApiVersion.Should().Be("v25.0");
        result.WindowStatus.Should().Be("ok");
        result.WindowRemainingHours.Should().Be(22);
        result.LastCustomerMessageAt.Should().NotBeNull();
        result.CallbackUrl.Should().Contain("/api/messenger");
    }

    [Fact]
    public async Task GetStatusAsync_OldCustomerThread_ReportsExpiredWindow()
    {
        SetupConfigured();
        SetupCustomerThread(DateTime.UtcNow.AddHours(-30));
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.GetStatusAsync();

        result.WindowStatus.Should().Be("expired");
        result.WindowRemainingHours.Should().Be(0);
    }

    [Fact]
    public async Task GetStatusAsync_NearWindowEnd_ReportsWarning()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, WindowHours = 24, WarningThresholdHours = 4
        });
        SetupCustomerThread(DateTime.UtcNow.AddHours(-22));
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.GetStatusAsync();

        result.WindowStatus.Should().Be("warning");
        result.WindowRemainingHours.Should().Be(2);
    }

    [Fact]
    public async Task GetStatusAsync_Unconfigured_NotReachableEvenIfClientWouldSucceed()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new MessengerOptions());
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.GetStatusAsync();

        result.Configured.Should().BeFalse();
        result.ApiReachable.Should().BeFalse();
        _mockClient.Verify(c => c.TestConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatusAsync_GraphProbeTimesOut_ReportsUnreachable()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, StatusCheckTimeoutSeconds = 1
        });
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return true;
            });

        var result = await _service.GetStatusAsync();

        result.ApiReachable.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_TokenInvalidDiagnostics_AreSurfaced()
    {
        SetupConfigured();
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var invalidAt = new DateTime(2026, 11, 15, 8, 30, 0, DateTimeKind.Utc);
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(MessengerOptionKeys.LastTokenInvalidAt))
            .ReturnsAsync(invalidAt.ToString("O"));
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(MessengerOptionKeys.LastErrorCode)).ReturnsAsync("190");
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(MessengerOptionKeys.LastErrorFbtraceId)).ReturnsAsync("ABC123");

        var result = await _service.GetStatusAsync();

        result.TokenInvalidAt.Should().Be(invalidAt);
        result.LastError.Should().NotBeNull();
        result.LastError!.Code.Should().Be(190);
        result.LastError.FbtraceId.Should().Be("ABC123");
    }

    [Fact]
    public async Task GetStatusAsync_NoCustomerThread_WindowStaysUnknown()
    {
        SetupConfigured();
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.GetStatusAsync();

        result.WindowStatus.Should().Be("unknown");
        result.LastCustomerMessageAt.Should().BeNull();
    }

    #endregion

    #region ReplyAsync

    [Fact]
    public async Task ReplyAsync_Disabled_DoesNotCallGraph()
    {
        SetupConfigured(new MessengerOptions { PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v", Enabled = false });

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "hi" }, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("未启用");
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_NotConfigured_DoesNotCallGraph()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new MessengerOptions { Enabled = true });

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "hi" }, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("未配置");
    }

    [Fact]
    public async Task ReplyAsync_EmptyContentAndNoAttachments_IsRejected()
    {
        SetupConfigured();

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "   " }, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不能同时为空");
    }

    [Fact]
    public async Task ReplyAsync_ConversationNotFound_IsRejected()
    {
        SetupConfigured();
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999)).ReturnsAsync((Conversation?)null);

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 999, Content = "hi" }, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("会话不存在");
    }

    [Fact]
    public async Task ReplyAsync_WithinWindow_SendsWithoutMessageTag()
    {
        SetupConfigured();
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendTextMessageAsync(Psid, "窗口内回复", null, null)).ReturnsAsync("m_reply_1");
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 200; return t; });

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "窗口内回复" }, 1);

        result.Sent.Should().BeTrue();
        result.ThreadCreated.Should().BeTrue();
        result.ThreadId.Should().Be(200);
        result.MessageId.Should().Be("m_reply_1");
        result.ChunkCount.Should().Be(1);
        result.MessageTagApplied.Should().BeFalse();
        _mockClient.Verify(c => c.SendSenderActionAsync(Psid, "typing_on"), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_OutsideWindow_AutoPolicy_AppliesHumanAgentTag()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, WindowHours = 24, MessageTagPolicy = "auto", MessageTag = "HUMAN_AGENT", TypingIndicator = false
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-25));
        _mockClient.Setup(c => c.SendTextMessageAsync(Psid, It.IsAny<string>(), "HUMAN_AGENT", null)).ReturnsAsync("m_tagged");
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 201; return t; });

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "窗口外主动回复" }, 1);

        result.Sent.Should().BeTrue();
        result.MessageTagApplied.Should().BeTrue();
        _mockClient.Verify(c => c.SendTextMessageAsync(Psid, "窗口外主动回复", "HUMAN_AGENT", null), Times.Once);
        _mockClient.Verify(c => c.SendSenderActionAsync(Psid, "typing_on"), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_OutsideWindow_NonePolicy_RejectsAndSkipsGraph()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, WindowHours = 24, MessageTagPolicy = "none", TypingIndicator = false
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-25));

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "窗口外且不带 tag" }, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("none");
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_AlwaysPolicy_TagsEvenInsideWindow()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, MessageTagPolicy = "always", TypingIndicator = false
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync("m_always");
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 202; return t; });

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "hi" }, 1);

        result.MessageTagApplied.Should().BeTrue();
        _mockClient.Verify(c => c.SendTextMessageAsync(Psid, "hi", "HUMAN_AGENT", null), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_LongText_ChunksAtTextChunkChars_Serially()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, TextChunkChars = 2000, TypingIndicator = false
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));

        var sentChunks = new List<string>();
        _mockClient.Setup(c => c.SendTextMessageAsync(Psid, It.IsAny<string>(), null, null))
            .ReturnsAsync((string _, string body, string? _, string? _) =>
            {
                sentChunks.Add(body);
                return $"m_chunk_{sentChunks.Count}";
            });
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 203; return t; });

        var longText = string.Join("\n", Enumerable.Repeat(new string('a', 1999), 3));
        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = longText }, 1);

        result.Sent.Should().BeTrue();
        result.ChunkCount.Should().Be(3);
        sentChunks.Should().HaveCount(3);
        sentChunks.Should().OnlyContain(s => s.Length <= 2000);
        // 分片必须逐字符还原输入：Graph 按 TextChunkChars 只是长度上限，不会补回被切掉的分隔符，
        // 所以「丢掉换行再拼接」的写法与 MessengerService.SplitText 的分片单元测试
        // （chunks[0].EndWith("\n") / string.Concat(chunks).Be(text)）自相矛盾。这里收紧为原样等值。
        string.Concat(sentChunks).Should().Be(longText);
    }

    [Fact]
    public async Task ReplyAsync_ReplyToMessageId_IsForwardedToGraph()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, TypingIndicator = false
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendTextMessageAsync(Psid, "引用回复", null, "m_customer_1")).ReturnsAsync("m_ref");
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 204; return t; });

        var result = await _service.ReplyAsync(new MessengerReplyRequest
        {
            ConversationId = 10,
            Content = "引用回复",
            ReplyToMessageId = "m_customer_1"
        }, 1);

        result.Sent.Should().BeTrue();
        _mockClient.Verify(c => c.SendTextMessageAsync(Psid, "引用回复", null, "m_customer_1"), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_Graph551_FailsAndPersistsDiagnostics()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, TypingIndicator = false, RateLimitBackoffTries = 0
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((string?)null);
        _mockClient.SetupGet(c => c.LastError).Returns(new MessengerApiError
        {
            Code = 551, Message = "Recipient unreachable", Subcode = 0, FbtraceId = "TRACE551", HttpStatus = 400
        });

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "hi" }, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("551").And.Contain("TRACE551");
        // 551 不可投递：只发一次，不重试
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => o.Name == MessengerOptionKeys.LastErrorCode && o.Value == "551")), Times.Once);
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => o.Name == MessengerOptionKeys.LastErrorFbtraceId && o.Value == "TRACE551")), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_Graph190_RecordsTokenInvalidAt()
    {
        SetupConfigured();
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((string?)null);
        _mockClient.SetupGet(c => c.LastError).Returns(new MessengerApiError { Code = 190, Message = "Error validating access token", HttpStatus = 400 });

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "hi" }, 1);

        result.Sent.Should().BeFalse();
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => o.Name == MessengerOptionKeys.LastTokenInvalidAt)), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_Graph613_RetriesWithBackoffThenSucceeds()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, TypingIndicator = false, RateLimitBackoffTries = 2
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));

        var calls = 0;
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((string _, string _, string? _, string? _) => ++calls == 1 ? null : "m_after_retry");
        _mockClient.SetupGet(c => c.LastError).Returns(() => calls == 1
            ? new MessengerApiError { Code = 613, Message = "Calls to this graph api are rate limited", HttpStatus = 400 }
            : null!);
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 205; return t; });

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "hi" }, 1);

        result.Sent.Should().BeTrue();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ReplyAsync_NoCustomerThread_CannotResolvePsid()
    {
        SetupConfigured();
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "hi" }, 1);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("PSID");
    }

    [Fact]
    public async Task ReplyAsync_MissingAttachment_IsReportedInErrorMessageButStillSent()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, TypingIndicator = false
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync("m_ok");
        _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<Attachment>());
        // ReplyAsync 落库前还会用 GetByIdsTrackedAsync 回填附件 ThreadId（MessengerService.cs:469）；
        // 不写这条 Setup 时 Moq 返回 null，会在 try 里抛 NRE 走进「线程记录失败」分支，
        // 断言的附件错误文案（含 404）根本不会被回报。
        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<Attachment>());
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) => { t.Id = 206; return t; });

        var result = await _service.ReplyAsync(new MessengerReplyRequest
        {
            ConversationId = 10,
            Content = "带不存在的附件",
            AttachmentIds = new List<int> { 404 }
        }, 1);

        result.Sent.Should().BeTrue();
        result.ErrorMessage.Should().Contain("404");
    }

    [Fact]
    public async Task ReplyAsync_ThreadPersistThrows_ReportsSentButNotThreadCreated()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, TypingIndicator = false
        });
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(10)).ReturnsAsync(BuildConversation());
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync("m_sent_only");
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>())).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _service.ReplyAsync(new MessengerReplyRequest { ConversationId = 10, Content = "hi" }, 1);

        result.Sent.Should().BeTrue();
        result.ThreadCreated.Should().BeFalse();
        result.MessageId.Should().Be("m_sent_only");
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
    }

    #endregion

    #region SendSenderActionAsync

    [Theory]
    [InlineData("typing_on")]
    [InlineData("typing_off")]
    [InlineData("mark_seen")]
    public async Task SendSenderActionAsync_ValidAction_ForwardsToGraph(string action)
    {
        SetupConfigured();
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendSenderActionAsync(Psid, action)).ReturnsAsync(true);

        (await _service.SendSenderActionAsync(10, action)).Should().BeTrue();
    }

    [Fact]
    public async Task SendSenderActionAsync_UnknownAction_ReturnsFalse()
    {
        SetupConfigured();

        (await _service.SendSenderActionAsync(10, "hack")).Should().BeFalse();
        _mockClient.Verify(c => c.SendSenderActionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendSenderActionAsync_NotConfigured_ReturnsFalse()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new MessengerOptions { Enabled = true });

        (await _service.SendSenderActionAsync(10, "typing_on")).Should().BeFalse();
    }

    [Fact]
    public async Task SendSenderActionAsync_GraphThrows_ReturnsFalse()
    {
        SetupConfigured();
        SetupCustomerThread(DateTime.UtcNow.AddHours(-1));
        _mockClient.Setup(c => c.SendSenderActionAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("network"));

        (await _service.SendSenderActionAsync(10, "typing_on")).Should().BeFalse();
    }

    #endregion

    #region SubscribeFieldsAsync / TestConnectionAsync

    [Fact]
    public async Task SubscribeFieldsAsync_NotConfigured_FailsFast()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new MessengerOptions { Enabled = true });

        var result = await _service.SubscribeFieldsAsync("messages");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("未配置");
    }

    [Fact]
    public async Task SubscribeFieldsAsync_WithoutOverride_UsesConfiguredFields()
    {
        SetupConfigured(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, SubscribedFields = "messages,message_reads"
        });
        _mockClient.Setup(c => c.SubscribeFieldsAsync("messages,message_reads")).ReturnsAsync(true);

        var result = await _service.SubscribeFieldsAsync();

        result.Success.Should().BeTrue();
        result.Fields.Should().Be("messages,message_reads");
    }

    [Fact]
    public async Task SubscribeFieldsAsync_GraphFails_ReportsPermissionHint()
    {
        SetupConfigured();
        _mockClient.Setup(c => c.SubscribeFieldsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _mockClient.SetupGet(c => c.LastError).Returns(new MessengerApiError { Code = 10, Message = "Missing permission", HttpStatus = 400 });

        var result = await _service.SubscribeFieldsAsync("messages");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pages_manage_metadata");
    }

    [Fact]
    public async Task TestConnectionAsync_NotConfigured_ReturnsGuidance()
    {
        _mockSettingsProvider.Setup(s => s.Current).Returns(new MessengerOptions { Enabled = true });

        var result = await _service.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Page Access Token");
        _mockClient.Verify(c => c.TestConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TestConnectionAsync_Reachable_ReturnsPageAndVersion()
    {
        SetupConfigured();
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.TestConnectionAsync();

        result.Success.Should().BeTrue();
        result.PageId.Should().Be(PageId);
        result.ApiVersion.Should().Be("v25.0");
    }

    [Fact]
    public async Task TestConnectionAsync_TokenExpired_ExplainsNoRefreshEndpoint()
    {
        SetupConfigured();
        _mockClient.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _mockClient.SetupGet(c => c.LastError).Returns(new MessengerApiError { Code = 190, Message = "Invalid token", HttpStatus = 400 });

        var result = await _service.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("190").And.Contain("重新换发");
    }

    #endregion

    #region SplitText（Graph 文本 2000 上限）

    [Fact]
    public void SplitText_ShortText_SingleChunk()
        => MessengerService.SplitText("短消息", 2000).Should().Equal("短消息");

    [Fact]
    public void SplitText_EmptyOrNull_ReturnsNoChunks()
    {
        MessengerService.SplitText(string.Empty, 2000).Should().BeEmpty();
        MessengerService.SplitText("", 100).Should().BeEmpty();
    }

    [Fact]
    public void SplitText_RespectsChunkSize()
    {
        var text = new string('x', 5000);

        var chunks = MessengerService.SplitText(text, 2000);

        chunks.Should().HaveCount(3);
        chunks.Should().OnlyContain(c => c.Length <= 2000);
        string.Concat(chunks).Should().HaveLength(5000);
    }

    [Fact]
    public void SplitText_PrefersNewlineBoundary()
    {
        var text = new string('a', 1500) + "\n" + new string('b', 1500);

        var chunks = MessengerService.SplitText(text, 2000);

        chunks.Should().HaveCount(2);
        chunks[0].Should().EndWith("\n");
    }

    [Fact]
    public void SplitText_PrefersSpaceBoundary()
    {
        var text = string.Concat(Enumerable.Repeat("word ", 500));

        var chunks = MessengerService.SplitText(text, 2000);

        chunks.Should().OnlyContain(c => c.Length <= 2000);
        chunks[0].Should().EndWith(" ");
    }

    [Fact]
    public void SplitText_DoesNotSplitSurrogatePair()
    {
        // 表情（UTF-16 代理对）正好落在切点上时，须整体挪到下一片
        var text = new string('a', 1999) + "😀" + new string('b', 100);

        var chunks = MessengerService.SplitText(text, 2000);

        chunks.Should().HaveCount(2);
        chunks[0].Length.Should().Be(1999);
        chunks[1].Should().StartWith("😀");
        string.Concat(chunks).Should().Be(text);
    }

    [Fact]
    public void SplitText_ClampsTooSmallChunkSize()
    {
        var text = new string('y', 600);

        var chunks = MessengerService.SplitText(text, 50);

        chunks.Should().HaveCount(3);
        chunks.Should().OnlyContain(c => c.Length <= 200);
    }

    #endregion
}
