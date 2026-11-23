using System.Linq.Expressions;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// IMessageIngestionService 单元测试 —— 覆盖 iMessage 审计 #11（「自动回复」死开关）与 #6（AMB close 身份未归一化）。
/// 无网络：IIMessageClient 用 Moq 替身；Conversations/Threads 用内存列表按表达式过滤，
/// 这样 close 路径的查询谓词会真实命中「入站消息建出来的那个作用域身份」，而不是被 mock 顺手放行。
/// 报文一律用 JsonNode 组装（手写括号出错会让解析层静默丢消息，全部用例退化成空结果且无异常）。
/// </summary>
public class IMessageIngestionServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IIMessageSettingsProvider> _mockSettingsProvider;
    private readonly Mock<IIMessageClient> _mockClient;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<IDistributedLockService> _mockLockService;
    private readonly Mock<ILogger<IMessageIngestionService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IHostEnvironment> _mockEnv;
    private readonly IMessageIngestionService _service;

    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<IMailboxRepository> _mockMailboxRepo;
    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    /// <summary>内存会话表 / 线程表：FindAsync 按传入表达式过滤，贴近真实仓储语义。</summary>
    private readonly List<Conversation> _conversations = new();

    private readonly List<ConversationThread> _threads = new();

    /// <summary>出站文本调用的实际目的地（用于断言与坐席回复同一套地址归一化）。</summary>
    private readonly List<(string Destination, string Body)> _sentText = new();

    private IMessageOptions _settings = new();
    private int _nextConversationId = 500;
    private string? _customerChannelId;

    public IMessageIngestionServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockSettingsProvider = new Mock<IIMessageSettingsProvider>();
        _mockClient = new Mock<IIMessageClient>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockLockService = new Mock<IDistributedLockService>();
        _mockLogger = new Mock<ILogger<IMessageIngestionService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockEnv = new Mock<IHostEnvironment>();

        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockAttachmentRepo = new Mock<IAttachmentRepository>();
        _mockMailboxRepo = new Mock<IMailboxRepository>();
        _mockSystemOptionRepo = new Mock<ISystemOptionRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        _mockConversationRepo.Setup(r => r.GetNextNumberAsync()).ReturnsAsync(1);
        // FindActiveIMessageConversationAsync 不设默认：Moq 松散替身返回 null ⇒ 默认按「新会话」走
        _mockConversationRepo.Setup(r => r.AddAsync(It.IsAny<Conversation>()))
            .ReturnsAsync((Conversation c) =>
            {
                c.Id = _nextConversationId++;
                _conversations.Add(c);
                return c;
            });
        _mockConversationRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Conversation, bool>>>()))
            .ReturnsAsync((Expression<Func<Conversation, bool>> expr) => _conversations.Where(expr.Compile()).ToList());

        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) =>
            {
                _threads.Add(t);
                return t;
            });
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync((Expression<Func<ConversationThread, bool>> expr) => _threads.Where(expr.Compile()).ToList());

        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string channel, string channelId, string? displayName) =>
            {
                // 记下入站消息实际落库的作用域身份，供 close / 自动回复做同源断言
                _customerChannelId = channelId;
                return new Customer { Id = 700, Channel = channel, ChannelId = channelId, FirstName = displayName ?? string.Empty };
            });

        _mockLockService
            .Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new Mock<IDistributedLockHandle>().Object);

        _mockClient
            .Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string destination, string body, string? idempotencyId) =>
            {
                _sentText.Add((destination, body));
                return "auto-reply-" + _sentText.Count;
            });

        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(() => _settings);

        _service = new IMessageIngestionService(
            _mockUnitOfWork.Object,
            _mockSettingsProvider.Object,
            _mockClient.Object,
            _mockWorkflowService.Object,
            _mockNotificationPublisher.Object,
            _mockLockService.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEnv.Object,
            Options.Create(new IngestionMatchOptions()));
    }

    // ------------------------------------------------------------------ 配置 / 报文构造

    private static IMessageOptions AmbMspOptions() => new()
    {
        Mode = IMessageModes.AmbMsp,
        Enabled = true,
        ApiBaseUrl = "https://msp.example.internal",
        BusinessIdentifier = "biz-0001",
        WebhookSecret = "webhook-secret",
        MailboxId = 1,
    };

    private static IMessageOptions BridgeOptions() => new()
    {
        Mode = IMessageModes.Bridge,
        Enabled = true,
        BaseUrl = "https://bb.example.internal:1234",
        Password = "bluebubbles-password",
        MailboxId = 1,
    };

    /// <summary>AMB 入站报文：身份只放在 Source-Id 头（close 的原始形态就在这里）。</summary>
    private static string AmbPayload(string type, string? text = null)
    {
        var root = new JsonObject { ["type"] = type };
        if (text != null) root["body"] = text;
        return root.ToJsonString();
    }

    private static string BridgeNewMessagePayload(string chatGuid, string handle, string text)
    {
        var data = new JsonObject
        {
            ["guid"] = "bb-" + Guid.NewGuid().ToString("N"),
            ["chatGuid"] = chatGuid,
            ["text"] = text,
            ["isFromMe"] = false,
            ["dateCreated"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        data["handle"] = new JsonObject { ["address"] = handle };
        return new JsonObject { ["type"] = "new-message", ["data"] = data }.ToJsonString();
    }

    // ------------------------------------------------------------------ #11 自动回复

    [Fact]
    public async Task ProcessAmbInboundAsync_NewConversationWithAutoReplyEnabled_SendsOnceViaOutboundAddressNormalization()
    {
        var options = AmbMspOptions();
        options.AutoReplyEnabled = true;
        options.AutoReplyMessage = "已收到，客服稍后回复";
        _settings = options;

        var result = await _service.ProcessAmbInboundAsync(
            AmbPayload("message", "你好"),
            sourceIdHeader: "tel:+15559000101",
            destinationIdHeader: "urn:apple:messages:chat:biz-0001",
            idHeader: "amb-guid-101");

        result.NewConversationCount.Should().Be(1);
        result.AutoReplyContent.Should().Be("已收到，客服稍后回复");

        // 只发一次，且目的地必须与坐席出站回复同一套归一化（AMB = 作用域 ChannelId 里的裸 externalId）
        _sentText.Should().HaveCount(1);
        var expectedDestination = IMessageIdentity.ExtractExternalId(_customerChannelId!);
        _sentText[0].Destination.Should().Be(expectedDestination);
        _sentText[0].Destination.Should().NotContain("tel:");
        _sentText[0].Body.Should().Be("已收到，客服稍后回复");

        // 问候语也要落一条系统线程，否则客服在会话里看不到已经回复过
        _threads.Should().ContainSingle(t => t.From == "imessage@system"
                                            && t.ConversationId == result.ConversationId
                                            && t.Body!.Contains("已收到，客服稍后回复"));
    }

    [Fact]
    public async Task ProcessAmbInboundAsync_AutoReplyDisabled_DoesNotSendAnything()
    {
        var options = AmbMspOptions();
        options.AutoReplyEnabled = false;
        options.AutoReplyMessage = "已收到，客服稍后回复";
        _settings = options;

        var result = await _service.ProcessAmbInboundAsync(
            AmbPayload("message", "你好"),
            "tel:+15559000102",
            "urn:apple:messages:chat:biz-0001",
            "amb-guid-102");

        result.NewConversationCount.Should().Be(1);
        result.AutoReplyContent.Should().BeNull();
        _sentText.Should().BeEmpty();
        _threads.Should().NotContain(t => t.From == "imessage@system");
    }

    [Fact]
    public async Task ProcessAmbInboundAsync_AutoReplyEnabledButMessageBlank_DoesNotSendAnything()
    {
        var options = AmbMspOptions();
        options.AutoReplyEnabled = true;
        options.AutoReplyMessage = "   ";
        _settings = options;

        var result = await _service.ProcessAmbInboundAsync(
            AmbPayload("message", "你好"),
            "tel:+15559000103",
            "urn:apple:messages:chat:biz-0001",
            "amb-guid-103");

        result.NewConversationCount.Should().Be(1);
        result.AutoReplyContent.Should().BeNull();
        _sentText.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAmbInboundAsync_AutoReplyOnExistingConversation_DoesNotSendAgain()
    {
        var existing = new Conversation
        {
            Id = 707,
            MailboxId = 1,
            SourceType = SourceType.IMessage,
            Status = ConversationStatus.Active,
            Customer = new Customer { Id = 700, Channel = "imessage", ChannelId = "amb_msp:+15559000104" },
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
        };
        _mockConversationRepo
            .Setup(r => r.FindActiveIMessageConversationAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(existing);

        var options = AmbMspOptions();
        options.AutoReplyEnabled = true;
        options.AutoReplyMessage = "已收到，客服稍后回复";
        _settings = options;

        var result = await _service.ProcessAmbInboundAsync(
            AmbPayload("message", "又发了一条"),
            "tel:+15559000104",
            "urn:apple:messages:chat:biz-0001",
            "amb-guid-104");

        result.ReplyCount.Should().Be(1);
        result.NewConversationCount.Should().Be(0);
        result.AutoReplyContent.Should().BeNull();
        _sentText.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAmbInboundAsync_AutoReplySendThrows_InboundResultUnaffected()
    {
        var options = AmbMspOptions();
        options.AutoReplyEnabled = true;
        options.AutoReplyMessage = "已收到，客服稍后回复";
        _settings = options;

        _mockClient
            .Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("bridge down"));

        var result = await _service.ProcessAmbInboundAsync(
            AmbPayload("message", "你好"),
            "tel:+15559000105",
            "urn:apple:messages:chat:biz-0001",
            "amb-guid-105");

        // 自动回复是尽力而为的附加动作：发送异常不得改变入站结果，也不得冒泡（与 Telegram 渠道同一口径）
        result.NewConversationCount.Should().Be(1);
        result.ReplyCount.Should().Be(0);
        result.ErrorMessage.Should().BeNull();
        _conversations.Should().ContainSingle();
        _threads.Should().NotContain(t => t.From == "imessage@system", "发送失败时不应把问候语记成已发出");
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_BridgeNewConversation_AutoReplyReusesInboundChatGuid()
    {
        var options = BridgeOptions();
        options.AutoReplyEnabled = true;
        options.AutoReplyMessage = "您好，这里是客服";
        _settings = options;

        const string chatGuid = "iMessage;-;+15559000202";

        var result = await _service.ProcessWebhookPayloadAsync(
            BridgeNewMessagePayload(chatGuid, "+15559000202", "hi"),
            modeHint: null);

        result.NewConversationCount.Should().Be(1);
        _sentText.Should().HaveCount(1);
        // 桥接出站必须沿用入站见过的 chatGuid（带服务前缀）：自己拼裸 handle 会降级成 SMS
        _sentText[0].Destination.Should().Be(chatGuid);
        _sentText[0].Destination.Should().Be(IMessageIdentity.BuildReplyGuid(_customerChannelId!, inboundService: null));
    }

    // ------------------------------------------------------------------ #6 close 身份归一化

    [Fact]
    public async Task ProcessAmbInboundAsync_CloseEvent_ClosesConversationFoundByNormalizedChannelId()
    {
        _settings = AmbMspOptions();

        // 第一条入站消息：Source-Id 是原始的 tel: 形态，落库时会被归一化成裸 handle
        var inbound = await _service.ProcessAmbInboundAsync(
            AmbPayload("message", "你好"),
            "tel:+15559000303",
            "urn:apple:messages:chat:biz-0001",
            "amb-guid-303");
        inbound.NewConversationCount.Should().Be(1);

        var normalizedChannelId = _customerChannelId!;
        normalizedChannelId.Should().StartWith("amb_msp:");
        normalizedChannelId.Should().NotContain("tel:");

        // 诱饵：ChannelId 是「未归一化」的原始形态，且 UpdatedAt 更新。
        // 修复前 close 用的正是这个原始身份 ⇒ 会关掉诱饵（或谁都关不掉）；修复后必须命中归一化那条。
        _conversations.Add(new Conversation
        {
            Id = 808,
            MailboxId = 1,
            SourceType = SourceType.IMessage,
            Status = ConversationStatus.Active,
            Customer = new Customer { Id = 709, Channel = "imessage", ChannelId = "amb_msp:tel:+15559000303" },
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow,
        });

        var closed = await _service.ProcessAmbInboundAsync(
            AmbPayload("close"),
            "tel:+15559000303",
            "urn:apple:messages:chat:biz-0001",
            "amb-guid-304");

        closed.IsStatus.Should().BeTrue();
        closed.ConversationId.Should().Be(inbound.ConversationId);
        closed.IgnoredCount.Should().Be(0);

        var target = _conversations.Single(c => c.Id == inbound.ConversationId);
        target.Status.Should().Be(ConversationStatus.Closed);
        target.Customer!.ChannelId.Should().Be(normalizedChannelId);

        var decoy = _conversations.Single(c => c.Id == 808);
        decoy.Status.Should().Be(ConversationStatus.Active);
    }

    [Fact]
    public async Task ProcessAmbInboundAsync_CloseEventWithUnknownIdentity_IsIgnoredWithoutTouchingSessions()
    {
        _settings = AmbMspOptions();

        var closed = await _service.ProcessAmbInboundAsync(
            AmbPayload("close"),
            "tel:+15559000313",
            "urn:apple:messages:chat:biz-0001",
            "amb-guid-313");

        closed.IsStatus.Should().BeFalse();
        closed.IgnoredCount.Should().Be(1);
        _conversations.Should().BeEmpty();
    }
}
