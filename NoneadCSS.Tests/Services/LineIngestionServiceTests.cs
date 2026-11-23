using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore; // 审计 #14：需要 DbUpdateException 模拟提交失败
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
using NoneadCSS.Domain.Services;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// LineIngestionService 单元测试（无网络、无数据库）。
///
/// 覆盖入站路径的六条硬约束：
/// 1) 验签后的载荷才会进入本服务；载荷形状不对 ⇒ 直接拒绝（验签本身由 LineWebhookParser + LineController 把关，
///    这里同时验证 ComputeSignature/VerifySignature 的正反样例，确保"未验签/被改包的载荷进不来"）。
/// 2) 客户身份：Customers.GetOrCreateByChannelAsync("line", LINE userId) —— Channel 字面量必须是 "line"。
/// 3) 幂等：LINE 会重投同一事件 ⇒ 分布式锁 + MessageId 两级去重。
/// 4) mode=standby（频道待机，由其他系统/人工应答）⇒ 事件仍入库，但禁止任何被动回复。
/// 5) 消息形状（text / sticker / location / image / postback）决定线程正文与分类，入站媒体必须即时转存。
/// 6) 续聊复用已有会话（FindActiveLineConversationByUserIdAsync 已实现，窗口取 IngestionMatchOptions.LineMatchWindowHours）。
/// </summary>
public class LineIngestionServiceTests : IDisposable
{
    private const string ChannelId = "@opencss-line-demo";
    private const string ChannelSecret = "channel_secret_value";
    private const string LineUserId = "U4af8f4c5c6b3a3ff3e0d1f2a9b8c7d65";
    private const long EventTimestamp = 1763000000000; // 2025-11-13T06:13:20Z

    private readonly string _uploadRoot = Path.Combine(Path.GetTempPath(), "line-ingest-" + Guid.NewGuid().ToString("N"));

    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<IWorkflowService> _mockWorkflowService = new();
    private readonly Mock<ILineSettingsProvider> _mockSettingsProvider = new();
    private readonly Mock<ILineClient> _mockClient = new();
    private readonly Mock<INotificationPublisher> _mockNotifier = new();
    private readonly Mock<IDistributedLockService> _mockLockService = new();
    private readonly Mock<ILogger<LineIngestionService>> _mockLogger = new();
    private readonly Mock<IConfiguration> _mockConfiguration = new();
    private readonly Mock<IHostEnvironment> _mockEnvironment = new();

    private readonly Mock<IConversationRepository> _mockConversationRepo = new();
    private readonly Mock<IThreadRepository> _mockThreadRepo = new();
    private readonly Mock<ICustomerRepository> _mockCustomerRepo = new();
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo = new();
    private readonly Mock<IMailboxRepository> _mockMailboxRepo = new();
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction = new();

    /// <summary>内存线程表：FindAsync 按传入表达式过滤，贴近真实仓储语义（去重与撤回都走它）</summary>
    private readonly List<ConversationThread> _threads = [];

    private readonly List<ConversationThread> _addedThreads = [];
    private readonly List<Attachment> _addedAttachments = [];
    private readonly List<Conversation> _addedConversations = [];

    private LineOptions _settings = new();

    public LineIngestionServiceTests()
    {
        Directory.CreateDirectory(_uploadRoot);

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockUnitOfWork.Setup(u => u.ClearChanges());

        _mockTransaction.Setup(t => t.CommitAsync()).Returns(Task.CompletedTask);
        _mockTransaction.Setup(t => t.RollbackAsync()).Returns(Task.CompletedTask);

        _mockThreadRepo
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync((Expression<Func<ConversationThread, bool>> expr) => _threads.Where(expr.Compile()).ToList());

        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) =>
            {
                t.Id = 9000 + _addedThreads.Count;
                _addedThreads.Add(t);
                _threads.Add(t);
                return t;
            });

        _mockConversationRepo.Setup(r => r.GetNextNumberAsync()).ReturnsAsync(4242);
        _mockConversationRepo.Setup(r => r.AddAsync(It.IsAny<Conversation>()))
            .ReturnsAsync((Conversation c) =>
            {
                c.Id = 7000 + _addedConversations.Count;
                _addedConversations.Add(c);
                return c;
            });

        // 默认「窗口内没有活跃会话」；一旦本测试内建过会话，后续事件就复用它（贴近真实续聊语义）
        _mockConversationRepo
            .Setup(r => r.FindActiveLineConversationByUserIdAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(() => _addedConversations.Count > 0 ? _addedConversations[^1] : null);

        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string channel, string channelId, string? _) => new Customer
            {
                Id = 3100,
                Channel = channel,
                ChannelId = channelId,
                FirstName = "LINE",
                LastName = "客户",
            });

        _mockAttachmentRepo.Setup(r => r.AddAsync(It.IsAny<Attachment>()))
            .ReturnsAsync((Attachment a) =>
            {
                a.Id = 8000 + _addedAttachments.Count;
                _addedAttachments.Add(a);
                return a;
            });

        _mockConfiguration.Setup(c => c["Attachment:UploadPath"]).Returns(_uploadRoot);
        _mockEnvironment.Setup(e => e.ContentRootPath).Returns(_uploadRoot);

        _mockLockService
            .Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new Mock<IDistributedLockHandle>().Object);

        _mockNotifier.Setup(n => n.NotifyMailboxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);

        SetupSettings(new LineOptions
        {
            Enabled = true,
            WebhookEnabled = true,
            ChannelId = ChannelId,
            ChannelSecret = ChannelSecret,
            ChannelAccessToken = "channel_access_token_value",
            MailboxId = 1,
            ProfileEnrichEnabled = false,
            MarkAsReadEnabled = false,
            AutoReplyEnabled = false,
            MaxInboundMediaBytes = 1024 * 1024,
        });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_uploadRoot))
            {
                Directory.Delete(_uploadRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言
        }

        GC.SuppressFinalize(this);
    }

    private LineIngestionService CreateService(int matchWindowHours = 48)
    {
        var matchOptions = Options.Create(new IngestionMatchOptions { LineMatchWindowHours = matchWindowHours });

        return new LineIngestionService(
            _mockUnitOfWork.Object,
            _mockWorkflowService.Object,
            _mockSettingsProvider.Object,
            _mockClient.Object,
            _mockNotifier.Object,
            _mockLockService.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            matchOptions,
            _mockEnvironment.Object);
    }

    private void SetupSettings(LineOptions options)
    {
        _settings = options;
        _mockSettingsProvider.Setup(s => s.Current).Returns(options);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(options);
    }

    private static LineWebhookEvent TextEvent(string messageId, string text, string mode = "active", string? replyToken = "rt-1")
        => new()
        {
            EventType = "message",
            Mode = mode,
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = messageId,
            ContentMessageId = messageId,
            MessageType = "text",
            TextBody = text,
            ReplyToken = replyToken,
            DedupeKey = messageId,
        };

    #region 载荷识别与验签闸门

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessWebhookPayloadAsync_NotALinePayload_IsRejected(string? payload)
    {
        var service = CreateService();

        var result = await service.ProcessWebhookPayloadAsync(payload);

        result.ErrorMessage.Should().Be("不是 LINE Webhook 载荷");
        result.EventCount.Should().Be(0);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_ForeignWebhookShape_IsRejected()
    {
        var service = CreateService();
        var whatsappLike = """{"object":"whatsapp_business_account","entry":[]}""";

        var result = await service.ProcessWebhookPayloadAsync(whatsappLike);

        result.ErrorMessage.Should().Be("不是 LINE Webhook 载荷");
        _mockLockService.Verify(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// 验签契约（Controller 在绑定 JSON 之前对原始字节做 HMAC-SHA256 常量时间比较）：
    /// 正确签名通过；改包体或换密钥都必须失败 ⇒ 未验签/被篡改的载荷不可能进入入库路径。
    /// </summary>
    [Fact]
    public void VerifySignature_SignedBody_Verifies_TamperedAndWrongSecret_DoNot()
    {
        var payload = BuildWebhookJson(TextEvent("msg_signed", "签名载荷"));
        var rawBody = Encoding.UTF8.GetBytes(payload);

        var signature = LineWebhookParser.ComputeSignature(rawBody, ChannelSecret);

        LineWebhookParser.VerifySignature(rawBody, ChannelSecret, signature).Should().BeTrue();
        LineWebhookParser.VerifySignature(Encoding.UTF8.GetBytes(payload + " "), ChannelSecret, signature).Should().BeFalse();
        LineWebhookParser.VerifySignature(rawBody, "other_secret", signature).Should().BeFalse();
        LineWebhookParser.VerifySignature(rawBody, ChannelSecret, null).Should().BeFalse();
        LineWebhookParser.VerifySignature(rawBody, ChannelSecret, " not-base64!! ").Should().BeFalse();
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_SignedPayload_ParsedCountAndDestination()
    {
        var service = CreateService();
        var payload = BuildWebhookJson(TextEvent("msg_a", "第一条"), TextEvent("msg_b", "第二条"));
        LineWebhookParser.IsLineWebhook(payload).Should().BeTrue();
        LineWebhookParser.ParseDestination(payload).Should().Be(ChannelId);

        var result = await service.ProcessWebhookPayloadAsync(payload);

        result.EventCount.Should().Be(2);
        result.NewConversationCount.Should().Be(1); // 第二条复用刚建的会话（同一 userId）
        result.ReplyCount.Should().Be(2);
        result.ErrorMessage.Should().BeNull();
        _addedThreads.Should().HaveCount(2);
    }

    #endregion

    #region 客户身份与建档

    [Fact]
    public async Task ProcessEventAsync_FirstTextMessage_CreatesCustomerWithChannelLine_AndThread()
    {
        var service = CreateService();

        var result = await service.ProcessEventAsync(TextEvent("msg_first", "你好，我要退款"), ChannelId);

        // Customer.Channel 必须是 "line"（与模块名/权限名一致），ChannelId = LINE userId
        _mockCustomerRepo.Verify(r => r.GetOrCreateByChannelAsync("line", LineUserId, null), Times.Once);

        result.NewConversationCount.Should().Be(1);
        result.ReplyCount.Should().Be(1);
        result.IsDuplicate.Should().BeFalse();
        result.ConversationId.Should().BeGreaterThan(0);

        var conversation = _addedConversations.Should().ContainSingle().Subject;
        conversation.SourceType.Should().Be(SourceType.Line);
        conversation.Type.Should().Be(ConversationType.Chat);
        conversation.Status.Should().Be(ConversationStatus.Active);
        conversation.MailboxId.Should().Be(1);
        conversation.Number.Should().Be(4242);
        conversation.Subject.Should().Be("LINE 客户：你好，我要退款");

        var thread = _addedThreads.Should().ContainSingle().Subject;
        thread.MessageId.Should().Be("msg_first");
        thread.From.Should().Be(LineUserId);
        thread.To.Should().Be(ChannelId);
        thread.SourceType.Should().Be(SourceType.Line);
        thread.Type.Should().Be(ThreadType.Customer);
        thread.SourceVia.Should().Be(SourceVia.Customer);
        thread.IsFirst.Should().BeFalse();
        thread.Body.Should().Contain("<div class=\"line-text\">");
        thread.Body.Should().Contain("你好，我要退款");
        thread.CreatedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(EventTimestamp).UtcDateTime);

        using var headers = JsonDocument.Parse(thread.Headers!);
        headers.RootElement.GetProperty("lineMessageId").GetString().Should().Be("msg_first");
        headers.RootElement.GetProperty("replyToken").GetString().Should().Be("rt-1");
        headers.RootElement.GetProperty("lineMode").GetString().Should().Be("active");

        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(conversation.Id, WorkflowConst.TriggerNewConversation), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(conversation.Id, WorkflowConst.TriggerNewReply), Times.Once);
        _mockNotifier.Verify(n => n.NotifyMailboxAsync("1", "conversation_updated", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task ProcessEventAsync_ExistingConversation_IsReusedWithoutNewConversation()
    {
        var existing = new Conversation
        {
            Id = 555,
            Number = 100,
            MailboxId = 1,
            SourceType = SourceType.Line,
            Type = ConversationType.Chat,
            Status = ConversationStatus.Active,
            CustomerId = 3100,
            CreatedByCustomerId = 3100,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
            LastReplyAt = DateTime.UtcNow.AddDays(-1),
        };
        _mockConversationRepo
            .Setup(r => r.FindActiveLineConversationByUserIdAsync(LineUserId, It.IsAny<TimeSpan>()))
            .ReturnsAsync(existing);
        var service = CreateService(matchWindowHours: 48);

        var result = await service.ProcessEventAsync(TextEvent("msg_followup", "还在吗"), ChannelId);

        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(1);
        result.ConversationId.Should().Be(555);
        _mockConversationRepo.Verify(r => r.AddAsync(It.IsAny<Conversation>()), Times.Never);
        _mockConversationRepo.Verify(r => r.FindActiveLineConversationByUserIdAsync(LineUserId, TimeSpan.FromHours(48)), Times.Once);
        existing.LastReplyFrom.Should().Be(SourceVia.Customer);
        _addedThreads.Should().ContainSingle();
        _addedThreads[0].ConversationId.Should().Be(555);
    }

    [Fact]
    public async Task ProcessEventAsync_CustomerCreationFails_ReportsErrorAndWritesNothing()
    {
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((Customer?)null);
        var service = CreateService();

        var result = await service.ProcessEventAsync(TextEvent("msg_nocust", "hi"), ChannelId);

        result.ErrorMessage.Should().Contain("客户创建失败");
        _addedThreads.Should().BeEmpty();
        _addedConversations.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessEventAsync_NoUsableMailbox_ReportsError()
    {
        SetupSettings(new LineOptions
        {
            Enabled = true,
            WebhookEnabled = true,
            ChannelId = ChannelId,
            ChannelSecret = ChannelSecret,
            ChannelAccessToken = "t",
            MailboxId = null,
        });
        _mockMailboxRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Mailbox, bool>>>()))
            .ReturnsAsync(new List<Mailbox>());
        var service = CreateService();

        var result = await service.ProcessEventAsync(TextEvent("msg_nomailbox", "hi"), ChannelId);

        result.ErrorMessage.Should().Contain("未找到可用邮箱");
        _addedConversations.Should().BeEmpty();
        _addedThreads.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessEventAsync_MissingUserId_ReportsError()
    {
        var service = CreateService();
        var evt = new LineWebhookEvent { EventType = "message", SourceType = "user", DedupeKey = "msg_noid", MessageId = "msg_noid", MessageType = "text", TextBody = "hi" };

        var result = await service.ProcessEventAsync(evt, ChannelId);

        result.ErrorMessage.Should().Contain("缺少可用的 userId/chatId");
        _addedThreads.Should().BeEmpty();
    }

    #endregion

    #region 幂等去重与并发

    [Fact]
    public async Task ProcessEventAsync_SameMessageIdTwice_SecondIsDuplicateAndRollsBack()
    {
        var service = CreateService();
        var first = await service.ProcessEventAsync(TextEvent("msg_dup", "重复投递前的原件"), ChannelId);
        first.IsDuplicate.Should().BeFalse();

        var second = await service.ProcessEventAsync(TextEvent("msg_dup", "重复投递前的原件"), ChannelId);

        second.IsDuplicate.Should().BeTrue();
        second.ReplyCount.Should().Be(0);
        _addedThreads.Should().ContainSingle(); // 只有第一条真的落库
        _mockUnitOfWork.Verify(u => u.ClearChanges(), Times.Once);
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once); // 只有第一次提交
    }

    [Fact]
    public async Task ProcessEventAsync_LockNotAcquired_IsDuplicateWithoutTouch()
    {
        _mockLockService.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((IDistributedLockHandle?)null);
        var service = CreateService();

        var result = await service.ProcessEventAsync(TextEvent("msg_locked", "并发重投"), ChannelId);

        result.IsDuplicate.Should().BeTrue();
        _mockLockService.Verify(s => s.TryAcquireAsync("line_ingest:msg_locked", 0), Times.Once);
        _addedThreads.Should().BeEmpty();
        _addedConversations.Should().BeEmpty();
        _mockCustomerRepo.Verify(r => r.GetOrCreateByChannelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ProcessEventAsync_EmptyDedupeKey_Skipped()
    {
        var service = CreateService();
        var evt = TextEvent("msg_nokey", "无键");
        evt.DedupeKey = string.Empty;

        var result = await service.ProcessEventAsync(evt, ChannelId);

        result.ErrorMessage.Should().Be("事件缺少去重键，已跳过");
        _mockLockService.Verify(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ProcessEventAsync_WebhookDisabledInSettings_ReportsGuard()
    {
        SetupSettings(new LineOptions
        {
            Enabled = true,
            WebhookEnabled = false,
            ChannelId = ChannelId,
            ChannelSecret = ChannelSecret,
            ChannelAccessToken = "t",
            MailboxId = 1,
        });
        var service = CreateService();

        var result = await service.ProcessEventAsync(TextEvent("msg_disabled", "hi"), ChannelId);

        result.ErrorMessage.Should().Be("LINE webhook 入站在设置中已关闭，事件未处理");
        _addedThreads.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessEventAsync_UnsendEvent_MarksStoredThreadDeletedWithoutNewConversation()
    {
        var service = CreateService();
        await service.ProcessEventAsync(TextEvent("msg_to_unsend", "这条会被撤回"), ChannelId);
        var originalBody = _addedThreads[0].Body;

        var unsend = new LineWebhookEvent
        {
            EventType = "unsend",
            SourceType = "user",
            UserId = LineUserId,
            DedupeKey = "unsend_msg_to_unsend",
            UnsentMessageIds = ["msg_to_unsend"],
            Timestamp = EventTimestamp + 1000,
        };
        var result = await service.ProcessEventAsync(unsend, ChannelId);

        result.ConversationId.Should().BeNull();
        _addedThreads.Should().HaveCount(1); // unsend 不新建线程
        var thread = _threads.Single(t => t.MessageId == "msg_to_unsend");
        thread.State.Should().Be(ThreadState.Deleted);
        thread.Body.Should().StartWith("<div class=\"line-unsend\">客户已撤回该消息</div>");
        thread.Body.Should().EndWith(originalBody);
    }

    #endregion

    #region standby 模式与自动回复

    [Fact]
    public async Task ProcessEventAsync_StandbyMode_SuppressesPassiveReply_ButStillStores()
    {
        SetupSettings(new LineOptions
        {
            Enabled = true,
            WebhookEnabled = true,
            ChannelId = ChannelId,
            ChannelSecret = ChannelSecret,
            ChannelAccessToken = "t",
            MailboxId = 1,
            AutoReplyEnabled = true,
            AutoReplyMessage = "您好，这里是客服",
        });
        var service = CreateService();

        var result = await service.ProcessEventAsync(TextEvent("msg_standbymode", "在吗", mode: "standby"), ChannelId);

        result.SkippedStandbyCount.Should().Be(1);
        result.AutoReplyContent.Should().BeNull();
        result.ReplyCount.Should().Be(1); // 事件仍入库，坐席仍能看到
        _addedThreads.Should().ContainSingle();
        _addedThreads[0].Headers!.Should().Contain("\"lineMode\":\"standby\"");
        _mockClient.Verify(c => c.SendTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessEventAsync_NewConversation_AutoReplyReusesReplyToken()
    {
        SetupSettings(new LineOptions
        {
            Enabled = true,
            WebhookEnabled = true,
            ChannelId = ChannelId,
            ChannelSecret = ChannelSecret,
            ChannelAccessToken = "t",
            MailboxId = 1,
            AutoReplyEnabled = true,
            AutoReplyMessage = "您好，客服已收到",
        });
        _mockClient.Setup(c => c.SendTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult { Success = true, Via = "reply", ChunkCount = 1 });
        var service = CreateService();

        var result = await service.ProcessEventAsync(TextEvent("msg_auto", "首条消息", replyToken: "rt-auto"), ChannelId);

        result.AutoReplyContent.Should().Be("您好，客服已收到");
        // reply 通道：复用一次性 replyToken，不消耗月度配额
        _mockClient.Verify(c => c.SendTextAsync("您好，客服已收到", "rt-auto", LineUserId, It.IsAny<CancellationToken>()), Times.Once);
        var autoThread = _addedThreads.Should().HaveCount(2).And.Subject.Last();
        autoThread.MessageId.Should().StartWith("line-auto-reply-");
        autoThread.Type.Should().Be(ThreadType.Message);
        autoThread.SourceVia.Should().Be(SourceVia.User);
        autoThread.SendStatus.Should().Be(SendStatus.Sent);
    }

    [Fact]
    public async Task ProcessEventAsync_AutoReplySkipped_WhileHumanTakeoverActive()
    {
        SetupSettings(new LineOptions
        {
            Enabled = true,
            WebhookEnabled = true,
            ChannelId = ChannelId,
            ChannelSecret = ChannelSecret,
            ChannelAccessToken = "t",
            MailboxId = 1,
            AutoReplyEnabled = true,
            AutoReplyMessage = "您好，客服已收到",
        });
        var conversation = new Conversation { Id = 555, Number = 1, MailboxId = 1, SourceType = SourceType.Line, Type = ConversationType.Chat, Status = ConversationStatus.Active, CustomerId = 3100 };
        _mockConversationRepo.Setup(r => r.FindActiveLineConversationByUserIdAsync(LineUserId, It.IsAny<TimeSpan>())).ReturnsAsync(conversation);
        _threads.Add(new ConversationThread
        {
            Id = 1,
            ConversationId = 555,
            Type = ThreadType.Note,
            IsInternal = true,
            SourceType = SourceType.Line,
            From = "line@system",
            Subject = "LINE 人工接管",
            MessageId = "line-takeover-555-fixed",
            Headers = JsonSerializer.Serialize(new Dictionary<string, string> { ["untilUtc"] = DateTime.UtcNow.AddMinutes(30).ToString("O") }),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10),
        });
        var service = CreateService();

        var result = await service.ProcessEventAsync(TextEvent("msg_takeover", "人工接管期间", replyToken: "rt-2"), ChannelId);

        result.AutoReplyContent.Should().BeNull();
        _mockClient.Verify(c => c.SendTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region 消息形状：sticker / location / image / postback

    [Fact]
    public async Task ProcessEventAsync_StickerMessage_ClassifiedAsUnknownMediaPlaceholder()
    {
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_sticker",
            MessageType = "sticker",
            TextBody = "[sticker 1/2]",
            ReplyToken = "rt-1",
            DedupeKey = "msg_sticker",
        };

        await service.ProcessEventAsync(evt, ChannelId);

        var thread = _addedThreads.Should().ContainSingle().Subject;
        thread.Body.Should().Contain("<div class=\"line-unknown\">[sticker 消息]</div>");
        _addedConversations.Should().ContainSingle().Subject.Subject.Should().Be("LINE 客户：[sticker] 消息");
    }

    [Fact]
    public async Task ProcessEventAsync_LocationMessage_CarriesCoordinatesAndAddress()
    {
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_loc",
            MessageType = "location",
            LocationAddress = "台北市中正区忠孝东路一段 5 号",
            Title = "地点",
            Latitude = 25.0409,
            Longitude = 121.5206,
            DedupeKey = "msg_loc",
        };

        await service.ProcessEventAsync(evt, ChannelId);

        var thread = _addedThreads.Should().ContainSingle().Subject;
        thread.Body.Should().Contain("line-location");
        thread.Body.Should().Contain("data-lat=\"25.0409\"");
        thread.Body.Should().Contain("data-lng=\"121.5206\"");
        thread.Body.Should().Contain("台北市中正区忠孝东路一段 5 号");
    }

    [Fact]
    public async Task ProcessEventAsync_ImageMessage_DownloadsImmediatelyAndAttachesToThread()
    {
        var service = CreateService();
        _mockClient.Setup(c => c.DownloadContentAsync("msg_image", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineContentResult
            {
                Content = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
                MimeType = "image/jpeg",
            });
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_image",
            MessageType = "image",
            DedupeKey = "msg_image",
        };

        var result = await service.ProcessEventAsync(evt, ChannelId);

        result.ReplyCount.Should().Be(1);
        var thread = _addedThreads.Should().ContainSingle().Subject;
        thread.Body.Should().Contain("<div class=\"line-media line-image\">图片</div>");
        thread.HasAttachments.Should().BeTrue();

        var attachment = _addedAttachments.Should().ContainSingle().Subject;
        attachment.ThreadId.Should().Be(thread.Id);
        attachment.Type.Should().Be(AttachmentType.Image);
        attachment.MimeType.Should().Be("image/jpeg");
        attachment.Size.Should().Be(4);
        attachment.Embedded.Should().BeFalse();
        attachment.FileName.Should().Be("image-msg_image.jpg");
        attachment.StoredFileName.Should().EndWith(".jpg");
        Path.Exists(Path.Combine(_uploadRoot, attachment.FileDir, attachment.StoredFileName)).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessEventAsync_ImageTooLargeForInboundLimit_SkipsTranscodeButKeepsPlaceholder()
    {
        _mockClient.Setup(c => c.DownloadContentAsync("msg_big", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineContentResult { Content = [], MimeType = "image/jpeg", TooLarge = true });
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_big",
            MessageType = "image",
            DedupeKey = "msg_big",
        };

        await service.ProcessEventAsync(evt, ChannelId);

        _addedAttachments.Should().BeEmpty();
        _addedThreads.Should().ContainSingle().Subject.HasAttachments.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessEventAsync_FileMessage_RecordsFileNameInPlaceholder()
    {
        _mockClient.Setup(c => c.DownloadContentAsync("msg_file", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineContentResult { Content = new byte[] { 1, 2, 3 }, MimeType = "application/pdf", FileName = "报价单.pdf" });
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_file",
            MessageType = "file",
            FileName = "报价单.pdf",
            FileSize = 3,
            DedupeKey = "msg_file",
        };

        await service.ProcessEventAsync(evt, ChannelId);

        _addedThreads.Should().ContainSingle().Subject.Body.Should().Contain("<div class=\"line-media line-file\">文件：报价单.pdf</div>");
        _addedAttachments.Should().ContainSingle().Subject.Type.Should().Be(AttachmentType.Other);
    }

    // ── 审计 #14：入站附件的落盘动作不能留在数据库事务内 ──────────────────────────────
    // 旧实现把 File.WriteAllBytesAsync 放在事务里，提交失败只回滚数据库 ⇒ 磁盘上留下没有附件行指向的孤儿文件。
    // 现在的口径：事务内只写进 .line-inbound-staging，提交后才 rename 到正式目录；
    // 回滚 ⇒ 删暂存文件（磁盘全空）；提交后 rename 失败 ⇒ 字节留在暂存目录并留 warn 日志（不静默）。

    /// <summary>提交时撞唯一约束走回滚：暂存文件必须一起丢掉，UploadRoot 下不留任何文件</summary>
    [Fact]
    public async Task ProcessEventAsync_CommitFailsWithUniqueViolation_DiscardsStagedMediaAndLeavesNoOrphanFile()
    {
        _mockClient.Setup(c => c.DownloadContentAsync("msg_img_rollback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineContentResult { Content = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, MimeType = "image/jpeg" });
        _mockTransaction.Setup(t => t.CommitAsync())
            .ThrowsAsync(new DbUpdateException(
                "duplicate key value violates unique constraint \"IX_ConversationThreads_MessageId\"",
                new Exception("UNIQUE constraint failed")));
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_img_rollback",
            MessageType = "image",
            DedupeKey = "msg_img_rollback",
        };

        var result = await service.ProcessEventAsync(evt, ChannelId);

        result.IsDuplicate.Should().BeTrue();
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        // 附件行曾经加进 ChangeTracker（回滚只回数据库），但字节必须已经删除：
        // 正式目录与暂存目录都不应有任何文件，否则就是"库里没有、磁盘上有"的孤儿
        Directory.GetFiles(_uploadRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>提交时抛非唯一约束异常：异常照原样上抛，同样不留暂存文件</summary>
    [Fact]
    public async Task ProcessEventAsync_CommitFailsWithOtherError_RethrowsAndLeavesNoStagedFile()
    {
        _mockClient.Setup(c => c.DownloadContentAsync("msg_img_fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineContentResult { Content = new byte[] { 1, 2, 3 }, MimeType = "image/png" });
        _mockTransaction.Setup(t => t.CommitAsync()).ThrowsAsync(new DbUpdateException("数据库写入失败"));
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_img_fail",
            MessageType = "image",
            DedupeKey = "msg_img_fail",
        };

        await Assert.ThrowsAsync<DbUpdateException>(() => service.ProcessEventAsync(evt, ChannelId));

        Directory.GetFiles(_uploadRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>提交后落位失败（目标目录名被占住）：附件行已入库，字节留在暂存目录并留 warn 日志</summary>
    [Fact]
    public async Task ProcessEventAsync_PostCommitMoveFails_KeepsBytesInStagingAndWarnsInsteadOfSilentlyDropping()
    {
        _mockClient.Setup(c => c.DownloadContentAsync("msg_img_move", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineContentResult { Content = new byte[] { 9, 8, 7, 6 }, MimeType = "image/jpeg" });
        // 在提交这一刻把"当天的正式目录"换成同名空文件：Directory.CreateDirectory 必然失败 ⇒ rename 走 catch。
        // 目录名取自附件行自己的 FileDir，因此不受测试与服务的日期计算差异影响。
        _mockTransaction.Setup(t => t.CommitAsync()).Returns(() =>
        {
            var dayDir = Path.Combine(_uploadRoot, _addedAttachments[0].FileDir);
            Directory.CreateDirectory(Path.GetDirectoryName(dayDir)!);
            File.WriteAllText(dayDir, string.Empty);
            return Task.CompletedTask;
        });
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_img_move",
            MessageType = "image",
            DedupeKey = "msg_img_move",
        };

        await service.ProcessEventAsync(evt, ChannelId);

        var attachment = _addedAttachments.Should().ContainSingle().Subject;
        // 正式路径上没有文件（rename 失败），但字节仍在暂存目录里等人工处置
        File.Exists(Path.Combine(_uploadRoot, attachment.FileDir, attachment.StoredFileName)).Should().BeFalse();
        var stagingFiles = Directory.GetFiles(
            Path.Combine(_uploadRoot, LineIngestionService.StagingDirectoryName), "*", SearchOption.AllDirectories);
        stagingFiles.Should().ContainSingle().Which.Should().EndWith(attachment.StoredFileName);
        // 失败不能静默：必须有 warn 痕迹
        _mockLogger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessEventAsync_PostbackEvent_StoresClickDataWithoutAutoReply()
    {
        SetupSettings(new LineOptions
        {
            Enabled = true,
            WebhookEnabled = true,
            ChannelId = ChannelId,
            ChannelSecret = ChannelSecret,
            ChannelAccessToken = "t",
            MailboxId = 1,
            AutoReplyEnabled = true,
            AutoReplyMessage = "欢迎",
        });
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "postback",
            MessageType = "postback",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            PostbackData = "action=pricing",
            TextBody = "action=pricing",
            ReplyToken = "rt-pb",
            DedupeKey = "postback_user_rt",
        };

        var result = await service.ProcessEventAsync(evt, ChannelId);

        result.ReplyCount.Should().Be(1);
        // postback 不是客户消息 ⇒ 不触发自动回复（IsCustomerMessage 为 false）
        result.AutoReplyContent.Should().BeNull();
        _addedThreads.Should().ContainSingle();
        _addedThreads[0].Body.Should().Contain("line-postback");
        _addedThreads[0].Body.Should().Contain("data-data=\"action=pricing\"");
        _addedConversations.Should().ContainSingle().Subject.Subject.Should().Be("LINE 客户：action=pricing");
    }

    [Fact]
    public async Task ProcessEventAsync_GroupMessage_UsesChatIdAndGroupSubjectPrefix()
    {
        var service = CreateService();
        var evt = new LineWebhookEvent
        {
            EventType = "message",
            SourceType = "group",
            ChatId = "C1234567890abcdef1234567890abcd",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            MessageId = "msg_group",
            MessageType = "text",
            TextBody = "群里提问",
            DedupeKey = "msg_group",
        };

        await service.ProcessEventAsync(evt, ChannelId);

        _mockCustomerRepo.Verify(r => r.GetOrCreateByChannelAsync("line", "C1234567890abcdef1234567890abcd", null), Times.Once);
        _addedConversations.Should().ContainSingle().Subject.Subject.Should().StartWith("LINE 群聊：");
        var thread = _addedThreads.Should().ContainSingle().Subject;
        thread.From.Should().Be("C1234567890abcdef1234567890abcd");
        thread.Headers!.Should().Contain("lineChatId");
    }

    [Fact]
    public async Task ProcessEventAsync_FollowEventWithExistingConversation_WritesInternalNote()
    {
        var conversation = new Conversation { Id = 555, Number = 1, MailboxId = 1, SourceType = SourceType.Line, Type = ConversationType.Chat, Status = ConversationStatus.Active, CustomerId = 3100 };
        _mockConversationRepo.Setup(r => r.FindActiveLineConversationByUserIdAsync(LineUserId, It.IsAny<TimeSpan>())).ReturnsAsync(conversation);
        var service = CreateService();
        var follow = new LineWebhookEvent
        {
            EventType = "follow",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            ReplyToken = "rt-follow",
            DedupeKey = "follow_U4af_1763000000000",
        };

        var result = await service.ProcessEventAsync(follow, ChannelId);

        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        var note = _addedThreads.Should().ContainSingle().Subject;
        note.Type.Should().Be(ThreadType.Note);
        note.IsInternal.Should().BeTrue();
        note.Subject.Should().Be("LINE follow");
        note.Body.Should().Contain("客户已添加机器人为好友（此后 push 可达）");
        note.MessageId.Should().Be("follow_U4af_1763000000000");
    }

    [Fact]
    public async Task ProcessEventAsync_FollowEventWithoutConversation_WritesNothing()
    {
        var service = CreateService();
        var unfollow = new LineWebhookEvent
        {
            EventType = "unfollow",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            DedupeKey = "unfollow_U4af_1763000000000",
        };

        await service.ProcessEventAsync(unfollow, ChannelId);

        _addedThreads.Should().BeEmpty();
        _addedConversations.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessEventAsync_UnhandledEventType_ReportsError()
    {
        var service = CreateService();
        var join = new LineWebhookEvent
        {
            EventType = "link",
            SourceType = "user",
            UserId = LineUserId,
            Timestamp = EventTimestamp,
            DedupeKey = "link_U4af_1763000000000",
        };

        var result = await service.ProcessEventAsync(join, ChannelId);

        result.ErrorMessage.Should().Be("暂不处理的 LINE 事件类型：link");
        _addedThreads.Should().BeEmpty();
    }

    #endregion

    #region 载荷解析（与入库路径衔接的最小形状）

    [Fact]
    public void ParseEvents_RealLinePayload_MapsEachShapeToExpectedEventFields()
    {
        var payload = BuildWebhookJson(
            MessageEventJson("msg_t", "\"type\":\"text\",\"text\":\"你好\\n第二行\""),
            MessageEventJson("msg_s", "\"type\":\"sticker\",\"packageId\":\"1\",\"stickerId\":\"2\""),
            MessageEventJson("msg_l", "\"type\":\"location\",\"title\":\"地点\",\"address\":\"某路 1 号\",\"latitude\":25.03,\"longitude\":121.5"),
            MessageEventJson("msg_i", "\"type\":\"image\""));

        var events = LineWebhookParser.ParseEvents(payload);

        events.Should().HaveCount(4);
        events[0].MessageType.Should().Be("text");
        events[0].TextBody.Should().Be("你好\n第二行");
        events[0].DedupeKey.Should().Be("msg_t");
        events[0].ReplyToken.Should().Be("rt-1");
        events[1].MessageType.Should().Be("sticker");
        events[1].TextBody.Should().Be("[sticker 1/2]");
        events[2].MessageType.Should().Be("location");
        events[2].LocationAddress.Should().Be("某路 1 号");
        events[2].Latitude.Should().Be(25.03);
        events[2].Longitude.Should().Be(121.5);
        events[3].MessageType.Should().Be("image");
        events[3].MessageId.Should().Be("msg_i");
    }

    [Fact]
    public void ParseEvents_MissingMessageId_SyntheticDedupeKeyAndBatchNotAborted()
    {
        var payload = BuildWebhookJson(
            """{"type":"message","replyToken":"rt","mode":"active","timestamp":1763000000000,"source":{"type":"user","userId":"U1"},"message":{"type":"text","text":"无 id"}}""",
            MessageEventJson("msg_ok", "\"type\":\"text\",\"text\":\"有 id\""));

        var events = LineWebhookParser.ParseEvents(payload);

        // 缺 message.id 只削弱幂等键（退化为 msg_{userId}_{timestamp}），事件本身仍要解析、整批不得中断；
        // 被丢弃的是「message 对象缺失」的事件（见 LineWebhookParserTests.ParseEvents_MessageObjectMissing_EventSkipped），
        // 与 LineWebhookParserTests.ParseEvents_MessageWithoutId_UsesSyntheticDedupeKey 的判据保持一致。
        events.Should().HaveCount(2);
        events[0].MessageId.Should().BeNull();
        events[0].DedupeKey.Should().Be("msg_U1_1763000000000");
        events[1].MessageId.Should().Be("msg_ok");
        events[1].TextBody.Should().Be("有 id");
    }

    #endregion

    // ── 载荷构造辅助 ────────────────────────────────────────────────────────────

    private static string BuildWebhookJson(params string[] eventJsons)
        => $$"""{"destination":"{{ChannelId}}","events":[{{string.Join(",", eventJsons)}}]}""";

    private static string MessageEventJson(string messageId, string messageInner, string mode = "active", long timestamp = EventTimestamp)
        => $$$"""{"type":"message","replyToken":"rt-1","mode":"{{{mode}}}","timestamp":{{{timestamp}}},"source":{"type":"user","userId":"{{{LineUserId}}}"},"message":{"id":"{{{messageId}}}",{{{messageInner}}}}}""";

    private static string BuildWebhookJson(params LineWebhookEvent[] events)
    {
        var jsonEvents = events.Select(e =>
        {
            // MessageEventJson 已经把片段拼进 "message":{ … } 内部 ⇒ 这里只能给属性列表；
            // 带花括号会生成 "message":{"id":"x",{"type":"text",…}} ⇒ 整段 JSON 非法 ⇒ IsLineWebhook 直接 False。
            var messageInner = e.MessageType switch
            {
                "text" => $"\"type\":\"text\",\"text\":{JsonSerializer.Serialize(e.TextBody ?? string.Empty)}",
                "image" => "\"type\":\"image\"",
                _ => $"\"type\":\"{e.MessageType}\"",
            };
            return MessageEventJson(e.MessageId ?? e.DedupeKey, messageInner, e.Mode, e.Timestamp);
        });

        return $$"""{"destination":"{{ChannelId}}","events":[{{string.Join(",", jsonEvents)}}]}""";
    }
}
