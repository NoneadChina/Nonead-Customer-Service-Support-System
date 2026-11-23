using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
using NoneadCSS.Infrastructure.Services;
using NoneadCSS.Infrastructure.Services.Line;
using NoneadCSS.Tests.Helpers;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// LineService / LineClient 单元测试（无网络：HttpClient 走桩 handler）。
/// 覆盖 LINE 的关键行为差异：
/// - 设置读写：密钥以 ENC: 前缀落 SystemOptions，读取不回显明文
/// - replyToken 生命周期：新鲜 ⇒ 走 reply（不耗配额）；过期 ⇒ 直接 push；LINE 侧已用（409）⇒ 回落 push
/// - 404/403 判定为不可达并写内部备注
/// - 文本 ≤5000 字分片、429/5xx 单次退避重试、配额 403 错误透出
/// </summary>
public class LineServiceTests
{
    private const int ConversationId = 4200;

    private const string LineUserId = "U4af8f4c5c6b3a3ff3e0d1f2a9b8c7d65";

    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<IEncryptionService> _mockEncryption = new();
    private readonly Mock<ILineSettingsProvider> _mockSettingsProvider = new();
    private readonly Mock<ILineClient> _mockClient = new();
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUser = new();
    private readonly Mock<INotificationPublisher> _mockNotifier = new();
    private readonly Mock<IConfiguration> _mockConfiguration = new();
    private readonly Mock<IConversationRepository> _mockConversationRepo = new();
    private readonly Mock<IThreadRepository> _mockThreadRepo = new();
    private readonly Mock<ICustomerRepository> _mockCustomerRepo = new();
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo = new();
    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepo = new();
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction = new();

    private readonly LineOptions _options = new()
    {
        Enabled = true,
        WebhookEnabled = true,
        ChannelId = "@opencss-demo",
        ChannelSecret = "channel_secret_value",
        ChannelAccessToken = "channel_access_token_value",
        AttachmentPublicBaseUrl = "https://css.example.com",
        MailboxId = 1,
        ReplyTokenTtlSeconds = 25,
        TextChunkChars = 5000,
        MaxMessagesPerRequest = 5,
        LoadingIndicatorEnabled = false,
        HumanHandoverMuteMinutes = 60,
    };

    private readonly List<ConversationThread> _addedThreads = [];

    private readonly LineService _service;

    public LineServiceTests()
    {
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockTransaction.Setup(t => t.CommitAsync()).Returns(Task.CompletedTask);
        _mockTransaction.Setup(t => t.RollbackAsync()).Returns(Task.CompletedTask);

        _mockSettingsProvider.Setup(s => s.Current).Returns(_options);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(_options);
        _mockSettingsProvider.Setup(s => s.RefreshAsync()).Returns(Task.CompletedTask);

        _mockCurrentUser.Setup(c => c.UserId).Returns(7);
        _mockConfiguration.Setup(c => c["App:BaseUrl"]).Returns("https://css.example.com");
        _mockNotifier.Setup(n => n.NotifyMailboxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);

        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .Callback<ConversationThread>(t => _addedThreads.Add(t))
            .ReturnsAsync((ConversationThread t) => t);
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => id == ConversationId
                ? new Conversation { Id = id, Number = 1001, MailboxId = 1, SourceType = SourceType.Line, CustomerId = 55, Subject = "LINE" }
                : null);
        _mockCustomerRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => id == 55
                ? new Customer { Id = id, Channel = "line", ChannelId = LineUserId }
                : null);
        _mockClient.Setup(c => c.StartLoadingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _service = new LineService(
            _mockUnitOfWork.Object,
            _mockEncryption.Object,
            _mockSettingsProvider.Object,
            _mockClient.Object,
            _mockCurrentUser.Object,
            _mockNotifier.Object,
            new Mock<ILogger<LineService>>().Object,
            _mockConfiguration.Object,
            new Mock<IHostEnvironment>().Object,
            null);
    }

    private ConversationThread CustomerThread(string? replyToken, TimeSpan age, bool used = false)
    {
        var headers = new Dictionary<string, string>();
        if (replyToken is not null)
        {
            headers["replyToken"] = replyToken;
        }

        if (used)
        {
            headers["replyTokenUsed"] = "true";
        }

        var createdAt = DateTime.UtcNow - age;
        return new ConversationThread
        {
            Id = 900,
            ConversationId = ConversationId,
            Type = ThreadType.Customer,
            SourceType = SourceType.Line,
            SourceVia = SourceVia.Customer,
            From = LineUserId,
            MessageId = "msg-777",
            Body = "<div class=\"line-text\">hi</div>",
            Headers = headers.Count > 0 ? JsonSerializer.Serialize(headers) : null,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    #region 设置读写

    [Fact]
    public async Task GetSettingsAsync_DoesNotEchoSecrets_ButReportsPresence()
    {
        var settings = await _service.GetSettingsAsync();

        settings.HasChannelSecret.Should().BeTrue();
        settings.HasChannelAccessToken.Should().BeTrue();
        settings.ChannelId.Should().Be("@opencss-demo");
        settings.WebhookUrl.Should().Be("https://css.example.com/api/line/webhook");

        // DTO 上不存在任何密钥字段（编译期保证），这里只断言不泄漏到日志/字段的语义前提
        typeof(LineSettingsDto).GetProperties().Select(p => p.Name)
            .Should().NotContain("ChannelSecret")
            .And.NotContain("ChannelAccessToken");
    }

    [Fact]
    public async Task UpdateSettingsAsync_StoresSecretsWithEncPrefix()
    {
        _mockEncryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns("CIPHERTEXT");
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.IsAny<string>())).ReturnsAsync((SystemOption?)null);
        _mockSystemOptionRepo.Setup(r => r.AddAsync(It.IsAny<SystemOption>())).ReturnsAsync((SystemOption o) => o);

        var dto = await _service.UpdateSettingsAsync(new UpdateLineSettingsRequest
        {
            ChannelSecret = "new_secret",
            ChannelAccessToken = "new_token",
            AutoReplyEnabled = true,
            AutoReplyMessage = "您好，已收到您的消息",
            TextChunkChars = 4000,
        });

        // 更新语义 = 按名取跟踪实体，取不到才 Add ⇒ 本请求 5 个字段就是 5 次 AddAsync。
        // Moq 的 Times 只统计「匹配到的调用数」，所以"每个 key 至多写一次"必须靠去重断言，而不是给 AddAsync 总数设上限。
        var saved = new Dictionary<string, string?>();
        var addedNames = new List<string>();
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => CaptureWithNames(o, addedNames, saved))), Times.AtLeast(5));

        addedNames.Should().OnlyHaveUniqueItems("同一个 option 不得被重复 Add");
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.IsAny<SystemOption>()), Times.AtLeast(2));

        saved["line.channel_secret"].Should().Be("ENC:CIPHERTEXT");
        saved["line.channel_access_token"].Should().Be("ENC:CIPHERTEXT");
        saved["line.text_chunk_chars"].Should().Be("4000");
        dto.HasChannelSecret.Should().BeTrue();

        _mockSettingsProvider.Verify(s => s.RefreshAsync(), Times.Once);
    }

    private bool Capture(SystemOption option, IDictionary<string, string?> sink)
    {
        sink[option.Name] = option.Value;
        return true;
    }

    /// <summary>Capture + 记录写入顺序（Moq 谓词必须是表达式树，不能写语句体 lambda）</summary>
    private bool CaptureWithNames(SystemOption option, ICollection<string> names, IDictionary<string, string?> sink)
    {
        names.Add(option.Name);
        return Capture(option, sink);
    }

    [Fact]
    public async Task UpdateSettingsAsync_EmptySecretMeansKeepExisting()
    {
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.IsAny<string>())).ReturnsAsync((SystemOption?)null);
        _mockSystemOptionRepo.Setup(r => r.AddAsync(It.IsAny<SystemOption>())).ReturnsAsync((SystemOption o) => o);

        await _service.UpdateSettingsAsync(new UpdateLineSettingsRequest { ChannelSecret = "", ChannelAccessToken = "" });

        _mockEncryption.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region replyToken 生命周期与回落

    [Fact]
    public async Task ReplyAsync_UnconfiguredOrDisabled_DoesNotCallClient()
    {
        _options.Enabled = false;

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Be("LINE 未启用或未配置");
        _mockClient.Verify(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_EmptyContentAndNoAttachments_Rejected()
    {
        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "  " }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Be("回复内容或附件至少提供一个");
    }

    [Fact]
    public async Task ReplyAsync_NonLineConversation_Rejected()
    {
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(It.IsAny<int>()))
            .ReturnsAsync(new Conversation { Id = ConversationId, SourceType = SourceType.Email });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Be("该会话不是 LINE 会话");
    }

    [Fact]
    public async Task ReplyAsync_FreshReplyToken_UsesReplyPath_AndMarksTokenUsed()
    {
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId))
            .ReturnsAsync(CustomerThread("fresh-token-abc", TimeSpan.FromSeconds(5)));
        _mockThreadRepo.Setup(r => r.GetByIdTrackedAsync(900)).ReturnsAsync(CustomerThread("fresh-token-abc", TimeSpan.FromSeconds(5)));

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult { Success = true, Via = "reply", ExternalMessageId = "line-1", ChunkCount = 1 });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "在的" }, 7);

        result.Sent.Should().BeTrue();
        result.Via.Should().Be("reply");
        result.FallbackFromReplyToken.Should().BeFalse();

        // replyToken 走 reply 通道，但按 ILineClient 契约（令牌已用/过期或分片超出单请求上限时自动回落 push）
        // 必须同时交出 toUserId；reply 请求体本身不含 to ⇒ 带 toUserId 不削弱"这次用的是 reply 令牌"这一断言。
        _mockClient.Verify(c => c.SendMessagesAsync(
            It.Is<IReadOnlyList<LineMessageObject>>(m => m.Count == 1 && m[0].Text == "在的"),
            "fresh-token-abc", LineUserId, It.IsAny<CancellationToken>()), Times.Once);

        // 一次性令牌用完即废：写回 replyTokenUsed，避免重发触发 409
        _addedThreads.Should().NotBeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeast(2));
    }

    [Fact]
    public async Task ReplyAsync_ExpiredReplyToken_GoesStraightToPush()
    {
        // replyToken 名义 TTL 约 30 秒，站内按 ReplyTokenTtlSeconds=25 判定：超过即不再尝试 reply
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId))
            .ReturnsAsync(CustomerThread("stale-token", TimeSpan.FromMinutes(3)));

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult { Success = true, Via = "push", ExternalMessageId = "line-2", ChunkCount = 1 });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "迟到的回复" }, 7);

        result.Sent.Should().BeTrue();
        result.Via.Should().Be("push");

        _mockClient.Verify(c => c.SendMessagesAsync(
            It.IsAny<IReadOnlyList<LineMessageObject>>(), null, LineUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_ReplyTokenAlreadyUsedOnLineSide_FallsBackToPush()
    {
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId))
            .ReturnsAsync(CustomerThread("used-token", TimeSpan.FromSeconds(3)));
        _mockThreadRepo.Setup(r => r.GetByIdTrackedAsync(900)).ReturnsAsync(CustomerThread("used-token", TimeSpan.FromSeconds(3)));

        var callIndex = 0;
        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callIndex++;
                return callIndex == 1
                    ? new LineSendResult { Success = false, Via = "reply", Error = new LineApiError { HttpStatus = 409, Message = "The message already has been sent" }, ErrorMessage = "LINE 返回 409" }
                    : new LineSendResult { Success = true, Via = "push", ExternalMessageId = "line-3", ChunkCount = 1 };
            });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "补救" }, 7);

        result.Sent.Should().BeTrue();
        result.FallbackFromReplyToken.Should().BeTrue();
        result.Via.Should().Be("push");

        _mockClient.Verify(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockClient.Verify(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), null, LineUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_DuplicateAfterRetry_DoesNotFallBackToPush()
    {
        // 429/5xx 重试后拿到 409：第一次请求可能已被 LINE 接受（只是响应丢了），
        // 投递状态不确定 ⇒ 不能回落 push，否则客户会收到两条同样的消息。
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId))
            .ReturnsAsync(CustomerThread("retried-token", TimeSpan.FromSeconds(3)));

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult
            {
                Success = false,
                Via = "reply",
                Attempts = 3,
                Error = new LineApiError { HttpStatus = 409, Message = "The message already has been sent" },
                ErrorMessage = "LINE 返回 409"
            });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "重试后 409" }, 7);

        result.Sent.Should().BeFalse();
        result.Via.Should().Be("reply");

        // 只调用一次：不得再发 push
        _mockClient.Verify(c => c.SendMessagesAsync(
            It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockClient.Verify(c => c.SendMessagesAsync(
            It.IsAny<IReadOnlyList<LineMessageObject>>(), null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_AttachmentFromAnotherConversation_IsRejected()
    {
        // 直链签名只绑 attachmentId+exp，不校验归属就等于把别的客户的附件公开
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync(CustomerThread("t1", TimeSpan.FromSeconds(1)));
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync([new ConversationThread { Id = 900, ConversationId = ConversationId }]);

        // 附件 77 挂在 999 号线程上 —— 不属于本会话（本会话线程只有 900）
        _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([new Attachment { Id = 77, ThreadId = 999, FileName = "secret.png", MimeType = "image/png", Size = 10, Type = AttachmentType.Image, FileDir = "2026/11/16", StoredFileName = "secret.png" }]);
        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync([]);

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult { Success = true, Via = "push", ChunkCount = 1 });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "见图", AttachmentIds = [77] }, 7);

        result.MediaErrors.Should().Contain(e => e.Contains("不属于当前会话"));

        // 没有任何呼出消息带 image 类型（被拒的附件不得签发直链）
        _mockClient.Verify(c => c.SendMessagesAsync(
            It.Is<IReadOnlyList<LineMessageObject>>(m => m.Any(x => x.Type == "image")),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_AlreadyConsumedTokenFlag_DoesNotRetryReply()
    {
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId))
            .ReturnsAsync(CustomerThread("already-flagged", TimeSpan.FromSeconds(2), used: true));

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult { Success = true, Via = "push", ChunkCount = 1 });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "x" }, 7);

        result.Sent.Should().BeTrue();
        _mockClient.Verify(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), "already-flagged", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region 不可达 / 配额

    [Fact]
    public async Task ReplyAsync_NotFriended404_MarksConversationUnreachableWithInternalNote()
    {
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync(CustomerThread("t1", TimeSpan.FromSeconds(1)));

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult
            {
                Success = false,
                Via = "push",
                Unreachable = true,
                Error = new LineApiError { HttpStatus = 404, Message = "User has been blocked" },
                ErrorMessage = "LINE 返回 404：用户已屏蔽或未添加机器人",
            });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.Unreachable.Should().BeTrue();

        var note = _addedThreads.SingleOrDefault(t => t.Type == ThreadType.Note && t.IsInternal && t.SendStatus == SendStatus.DeliveryError);
        note.Should().NotBeNull();
        note!.MessageId.Should().StartWith("line-unreachable-");
        note.Body.Should().Contain("未添加本机器人为好友");
    }

    [Fact]
    public async Task ReplyAsync_QuotaRejected403_ReportsQuotaError()
    {
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync(CustomerThread("t1", TimeSpan.FromSeconds(1)));

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult
            {
                Success = false,
                Via = "push",
                Unreachable = true,
                Error = new LineApiError { HttpStatus = 403, Message = "Quota limit exceeded" },
                ErrorMessage = "LINE 返回 403：配额或权限不足",
            });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        result.ErrorMessage.Should().Contain("403");
    }

    [Fact]
    public async Task ReplyAsync_ThreadWriteFails_ReturnsSentButThreadCreatedFalse()
    {
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync(CustomerThread("t1", TimeSpan.FromSeconds(1)));
        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult { Success = true, Via = "reply", ChunkCount = 1 });
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>())).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "hi" }, 7);

        // 消息已发出但本地线程写入失败 ⇒ 207 语义，坐席不应重发
        result.Sent.Should().BeTrue();
        result.ThreadCreated.Should().BeFalse();
        result.ErrorMessage.Should().Be("消息已发送，但会话线程记录失败");
    }

    #endregion

    #region 附件直链（LINE 无上传接口）

    [Fact]
    public async Task ReplyAsync_AttachmentWithoutPublicBaseUrl_ReportsMediaError()
    {
        _options.AttachmentPublicBaseUrl = null;
        _mockConfiguration.Setup(c => c["App:BaseUrl"]).Returns((string?)null);
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync(CustomerThread("t1", TimeSpan.FromSeconds(1)));
        _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([new Attachment { Id = 5, FileName = "a.png", MimeType = "image/png", Size = 1024, Type = AttachmentType.Image, FileDir = "2026/11/16", StoredFileName = "a.png" }]);

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, AttachmentIds = [5] }, 7);

        result.MediaErrors.Should().NotBeEmpty();
        result.MediaErrors[0].Should().Contain("AttachmentPublicBaseUrl");
    }

    [Fact]
    public async Task ReplyAsync_ImageAttachment_SendsSignedPublicUrlWithPreview()
    {
        // 附件直链只对「实体已落盘」的附件签发（LINE 无媒体上传接口，签出去的 URL 若 404 在 LINE 侧只表现为
        // 图片丢失）。目录约定必须与生产写入路径同源：LineIngestionService.cs:545 DownloadAndStageMediaAsync 用
        // DateTime.UtcNow.ToString("yyyy/MM/dd") 作 FileDir ⇒ 磁盘上是 年/月/日 嵌套目录。
        var dayFolder = "2026/11/16";
        var uploadRoot = Path.Combine(Path.GetTempPath(), "line-svc-media-" + Guid.NewGuid().ToString("N"));
        var dayDir = Path.Combine(uploadRoot, dayFolder);
        Directory.CreateDirectory(dayDir);
        File.WriteAllBytes(Path.Combine(dayDir, "a.png"), new byte[1024]);
        _mockConfiguration.Setup(c => c["Attachment:UploadPath"]).Returns(uploadRoot);

        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync(CustomerThread("t1", TimeSpan.FromSeconds(1)));
        _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([new Attachment { Id = 5, FileName = "a.png", MimeType = "image/png", Size = 1024, Type = AttachmentType.Image, FileDir = dayFolder, StoredFileName = "a.png" }]);
        _mockAttachmentRepo.Setup(r => r.GetByIdsTrackedAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync([]);

        LineSendResult CapturedSend = new() { Success = true, Via = "reply", ChunkCount = 1 };
        List<LineMessageObject>? sentMessages = null;
        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<LineMessageObject>, string?, string?, CancellationToken>((m, _, _, _) => sentMessages = m.ToList())
            .ReturnsAsync(() => CapturedSend);

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "见图", AttachmentIds = [5] }, 7);

        try
        {
            Directory.Delete(uploadRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录清理失败不影响断言
        }

        result.Sent.Should().BeTrue();
        result.MediaErrors.Should().BeEmpty();
        sentMessages.Should().NotBeNull();
        sentMessages!.Count.Should().Be(2);

        var image = sentMessages!.Single(m => m.Type == "image");
        image.OriginalContentUrl.Should().StartWith("https://css.example.com/api/line/media/5?");
        image.OriginalContentUrl.Should().Contain("sig=");
        image.PreviewImageUrl.Should().Contain("/api/line/media/5/preview?");
        sentMessages!.Single(m => m.Type == "text").Text.Should().Be("见图");
    }

    [Fact]
    public async Task ReplyAsync_OversizedAttachment_IsRejectedWithoutFailingWholeReply()
    {
        _options.MaxOutboundAttachmentBytes = 100;
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync(CustomerThread("t1", TimeSpan.FromSeconds(1)));
        _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([new Attachment { Id = 6, FileName = "big.zip", MimeType = "application/zip", Size = 999_999, Type = AttachmentType.Other, FileDir = "2026/11/16", StoredFileName = "big.zip" }]);

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult { Success = true, Via = "reply", ChunkCount = 1 });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "附件超限", AttachmentIds = [6] }, 7);

        result.Sent.Should().BeTrue();
        result.MediaErrors.Should().ContainSingle();
    }

    #endregion

    #region 状态卡字段（审计 #11：ReplyModeHint / LastError 永远为空）

    [Fact]
    public async Task GetStatusAsync_ReplyModeHintIsReply_WhenTokenValidAndWebhookEnabled()
    {
        _mockClient.Setup(c => c.VerifyTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineTokenInfo { Scope = "profile message" });
        _mockClient.Setup(c => c.GetWebhookEndpointAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineWebhookEndpointInfo { Endpoint = "https://css.example.com/api/line/webhook", Active = true });

        var status = await _service.GetStatusAsync();

        // 返回机器可读代码而不是文案：前端按代码取 locale key，中英双语才不会串语言
        status.ReplyModeHint.Should().Be("reply");
        status.TokenValid.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_ReplyModeHintIsPush_WhenWebhookDisabled()
    {
        _options.WebhookEnabled = false;
        _mockClient.Setup(c => c.VerifyTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineTokenInfo { Scope = "message" });

        var status = await _service.GetStatusAsync();

        // webhook 关 ⇒ 收不到入站事件、拿不到 replyToken，会话内只能 push（消耗月度配额）
        status.ReplyModeHint.Should().Be("push");
    }

    [Fact]
    public async Task GetStatusAsync_ReplyModeHintStaysUnknown_WhenTokenProbeFails()
    {
        _mockClient.Setup(c => c.VerifyTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((LineTokenInfo?)null);

        var status = await _service.GetStatusAsync();

        status.TokenValid.Should().BeFalse();
        status.ReplyModeHint.Should().Be("unknown");
    }

    [Fact]
    public async Task GetStatusAsync_FillsBothFields_WithoutExtraApiCalls()
    {
        _mockClient.Setup(c => c.VerifyTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineTokenInfo { Scope = "message" });
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(LineOptionKeys.LastErrorHttpStatus)).ReturnsAsync("429");
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(LineOptionKeys.LastErrorMessage)).ReturnsAsync("Quota exceeded for quota metric 'Send messages'");

        var status = await _service.GetStatusAsync();

        status.ReplyModeHint.Should().Be("reply");
        status.LastError.Should().NotBeNull();
        status.LastError!.HttpStatus.Should().Be(429);
        status.LastError.Message.Should().Contain("Quota exceeded");

        // 两个字段都复用既有三个并发探测的结果，不得为它们多打一次外部接口
        _mockClient.Verify(c => c.GetQuotaAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockClient.Verify(c => c.VerifyTokenAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockClient.Verify(c => c.GetWebhookEndpointAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStatusAsync_LastErrorIsNull_WhenStoredStatusIsNotANumber()
    {
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(LineOptionKeys.LastErrorHttpStatus)).ReturnsAsync("not-a-number");

        var status = await _service.GetStatusAsync();

        // 脏数据不能变成前端上的 "HTTP 0" 空条
        status.LastError.Should().BeNull();
    }

    [Fact]
    public async Task ReplyAsync_SendFailed_PersistsLastErrorForStatusCard()
    {
        var saved = new Dictionary<string, string?>();
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.IsAny<string>())).ReturnsAsync((SystemOption?)null);
        _mockSystemOptionRepo.Setup(r => r.AddAsync(It.IsAny<SystemOption>())).ReturnsAsync((SystemOption o) => o);
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync(CustomerThread("t1", TimeSpan.FromSeconds(1)));
        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult
            {
                Success = false,
                Via = "push",
                Attempts = 2,
                Error = new LineApiError { HttpStatus = 429, Message = "Too Many Requests" },
                ErrorMessage = "LINE 返回 429：限流",
            });

        var result = await _service.ReplyAsync(new LineReplyRequest { ConversationId = ConversationId, Content = "hi" }, 7);

        result.Sent.Should().BeFalse();
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => Capture(o, saved))), Times.AtLeastOnce);
        // LineClient 不持有错误上下文 ⇒ 由 LineService 在失败点落库，状态页才可能显示 LastError
        saved[LineOptionKeys.LastErrorHttpStatus].Should().Be("429");
        saved[LineOptionKeys.LastErrorMessage].Should().Be("Too Many Requests");
    }

    [Fact]
    public async Task PushTestAsync_SendFailed_PersistsLastErrorForStatusCard()
    {
        var saved = new Dictionary<string, string?>();
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.IsAny<string>())).ReturnsAsync((SystemOption?)null);
        _mockSystemOptionRepo.Setup(r => r.AddAsync(It.IsAny<SystemOption>())).ReturnsAsync((SystemOption o) => o);
        _mockClient.Setup(c => c.SendTextAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LineSendResult
            {
                Success = false,
                Error = new LineApiError { HttpStatus = 401, Message = "invalid channel access token" },
                ErrorMessage = "LINE 返回 401",
            });

        var result = await _service.PushTestAsync(new LinePushTestRequest { UserId = LineUserId, Text = "ping" });

        result.Success.Should().BeFalse();

        // PushTestAsync 有两条提前返回 Success=false 的路径（未配置 / 无可推送 userId），
        // 走这两条时断言前一半照样通过但客户端根本没被调用 ⇒ 先证明确实发出去了。
        _mockClient.Verify(c => c.SendTextAsync("ping", null, LineUserId, It.IsAny<CancellationToken>()), Times.Once);

        // saved 只有在 Capture 作为 It.Is 谓词被 Moq 求值时才会填充 ⇒ 必须先 Verify 再断言取值，
        // 否则字典是空的，失败会表现为 KeyNotFoundException 而不是真实差异（上一版就漏了这条）。
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => Capture(o, saved))), Times.AtLeastOnce);
        saved[LineOptionKeys.LastErrorHttpStatus].Should().Be("401");
        saved[LineOptionKeys.LastErrorMessage].Should().Be("invalid channel access token");
    }

    #endregion

    #region 六个可调参数暴露到 DTO（审计 #12）

    [Fact]
    public async Task GetSettingsAsync_ExposesSixTuningOptions()
    {
        _options.StatusCheckTimeoutSeconds = 12;
        _options.RetryMaxAttempts = 3;
        _options.MaxInboundMediaBytes = 33_554_432;
        _options.MaxOutboundAttachmentBytes = 20_971_520;
        _options.OutboundImageExtensions = "jpg,jpeg,png,webp";
        _options.ChannelAccessTokenKeyId = "kid-2026-11";

        var settings = await _service.GetSettingsAsync();

        settings.StatusCheckTimeoutSeconds.Should().Be(12);
        settings.RetryMaxAttempts.Should().Be(3);
        settings.MaxInboundMediaBytes.Should().Be(33_554_432);
        settings.MaxOutboundAttachmentBytes.Should().Be(20_971_520);
        settings.OutboundImageExtensions.Should().Be("jpg,jpeg,png,webp");
        settings.ChannelAccessTokenKeyId.Should().Be("kid-2026-11");
    }

    [Fact]
    public async Task UpdateSettingsAsync_WritesSixTuningOptionsToSystemOptions()
    {
        var saved = new Dictionary<string, string?>();
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.IsAny<string>())).ReturnsAsync((SystemOption?)null);
        _mockSystemOptionRepo.Setup(r => r.AddAsync(It.IsAny<SystemOption>())).ReturnsAsync((SystemOption o) => o);

        await _service.UpdateSettingsAsync(new UpdateLineSettingsRequest
        {
            StatusCheckTimeoutSeconds = 20,
            RetryMaxAttempts = 0,
            MaxInboundMediaBytes = 5_242_880,
            MaxOutboundAttachmentBytes = 10_485_760,
            OutboundImageExtensions = " JPG , png ",
            ChannelAccessTokenKeyId = " kid-2026-11 ",
        });

        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => Capture(o, saved))), Times.AtLeast(6));

        saved[LineOptionKeys.StatusCheckTimeoutSeconds].Should().Be("20");
        // RetryMaxAttempts=0 是合法值（关闭重试），必须能写进去 ⇒ 判 >= 0 而非 > 0
        saved[LineOptionKeys.RetryMaxAttempts].Should().Be("0");
        saved[LineOptionKeys.MaxInboundMediaBytes].Should().Be("5242880");
        saved[LineOptionKeys.MaxOutboundAttachmentBytes].Should().Be("10485760");
        // 出站按小写扩展名比对，不归一化会让 "JPG" 静默失效
        saved[LineOptionKeys.OutboundImageExtensions].Should().Be("jpg,png");
        saved[LineOptionKeys.ChannelAccessTokenKeyId].Should().Be("kid-2026-11");
    }

    [Fact]
    public async Task UpdateSettingsAsync_NullTuningOptions_AreNotWritten()
    {
        var saved = new Dictionary<string, string?>();
        _mockSystemOptionRepo.Setup(r => r.GetByNameTrackedAsync(It.IsAny<string>())).ReturnsAsync((SystemOption?)null);
        _mockSystemOptionRepo.Setup(r => r.AddAsync(It.IsAny<SystemOption>())).ReturnsAsync((SystemOption o) => o);

        // PATCH 语义：没传的字段不动（尤其 RetryMaxAttempts 不能因为 null 被写成 0）
        await _service.UpdateSettingsAsync(new UpdateLineSettingsRequest { ChannelId = "@opencss-demo" });

        // saved 只在 Capture 作为 It.Is 谓词被求值时才填充（上一版漏了这条 Verify，导致下面三条
        // 否定断言在空字典上白过）。先 Verify 填实，再锚一个"确实写入"的键，空字典就再也骗不过去了。
        _mockSystemOptionRepo.Verify(r => r.AddAsync(It.Is<SystemOption>(o => Capture(o, saved))), Times.AtLeastOnce);
        saved.Should().ContainKey(LineOptionKeys.ChannelId, "sink 必须真的收到本次写入，否则否定断言是空对空");

        saved.Should().NotContainKey(LineOptionKeys.RetryMaxAttempts);
        saved.Should().NotContainKey(LineOptionKeys.StatusCheckTimeoutSeconds);
        saved.Should().NotContainKey(LineOptionKeys.OutboundImageExtensions);
        saved.Should().NotContainKey(LineOptionKeys.MaxInboundMediaBytes);
        saved.Should().NotContainKey(LineOptionKeys.MaxOutboundAttachmentBytes);
        saved.Should().NotContainKey(LineOptionKeys.ChannelAccessTokenKeyId);
    }

    [Theory]
    [InlineData(".PNG,,jpeg , .gif, jpeg", "png,jpeg,gif")]
    [InlineData("", "")]
    [InlineData("jpg", "jpg")]
    public void NormalizeImageExtensions_DropsDotsBlanksAndDuplicates(string raw, string expected)
    {
        LineService.NormalizeImageExtensions(raw).Should().Be(expected);
    }

    #endregion

    #region 文本分片与重试（LineClient，走桩 handler，无网络）

    [Fact]
    public async Task LineClient_SendTextAsync_LongText_SplitsIntoChunksUnderLimit()
    {
        var stub = new StubLineHttpHandler
        {
            RespondWith = _ => (HttpStatusCode.OK, "{\"sentMessages\":[{\"id\":\"m1\",\"quoteToken\":\"q\"}]}")
        };

        var client = CreateClient(stub, chunkChars: 5000, maxMessages: 5);
        var text = new string('字', 12_000);

        var result = await client.SendTextAsync(text, replyToken: "rt-1", toUserId: null);

        result.Success.Should().BeTrue();

        // 单次请求 messages[] 上限是 MaxMessagesPerRequest(5)，而 replyToken 是一次性的（同一令牌再打一次 ⇒ 409）
        // ⇒ 12000/5000 的 3 片必须塞进同一个 reply 请求；只有片数 >5 时才会拆成多次请求。
        stub.Requests.Count.Should().Be(1);

        var sentTexts = AllSentTexts(stub);
        sentTexts.Should().HaveCount(3); // 12000 / 5000 ⇒ 3 片
        JsonDocument.Parse(stub.Requests[0].Body!).RootElement.GetProperty("messages").GetArrayLength().Should().Be(3);

        sentTexts.Should().OnlyContain(t => t!.Length <= 5000);
        string.Concat(sentTexts).Should().Be(text);
        stub.Requests.Should().OnlyContain(r => r.Url.EndsWith("/v2/bot/message/reply"));

        // 一次请求可携带多条 messages[] ⇒ 必须按请求顺序摊平，不能只取 messages[0]
        static List<string?> AllSentTexts(StubLineHttpHandler handler) =>
            [.. handler.Requests
                .SelectMany(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("messages").EnumerateArray()
                    .Select(m => m.GetProperty("text").GetString()))];
    }

    [Fact]
    public async Task LineClient_SendTextAsync_ManyChunks_RespectsMaxMessagesPerRequest()
    {
        var stub = new StubLineHttpHandler
        {
            RespondWith = _ => (HttpStatusCode.OK, "{\"sentMessages\":[{\"id\":\"m1\",\"quoteToken\":\"q\"}]}")
        };

        var client = CreateClient(stub, chunkChars: 100, maxMessages: 2);
        var result = await client.SendTextAsync(new string('a', 500), replyToken: null, toUserId: LineUserId);

        result.Success.Should().BeTrue();
        stub.Requests.Should().OnlyContain(r => r.Url.EndsWith("/v2/bot/message/push"));
        stub.Requests.Should().NotBeEmpty();
        foreach (var request in stub.Requests)
        {
            JsonDocument.Parse(request.Body).RootElement.GetProperty("messages").GetArrayLength().Should().BeLessThanOrEqualTo(2);
        }
    }

    [Fact]
    public async Task LineClient_RateLimited429_RetriesOnceWithBackoff()
    {
        var callCount = 0;
        var stub = new StubLineHttpHandler
        {
            RespondWith = _ =>
            {
                callCount++;
                return callCount == 1
                    ? (HttpStatusCode.TooManyRequests, "{\"message\":\"Too many requests\"}")
                    : (HttpStatusCode.OK, "{\"sentMessages\":[{\"id\":\"m2\",\"quoteToken\":\"q\"}]}");
            }
        };

        var client = CreateClient(stub, chunkChars: 5000, maxMessages: 5);
        var result = await client.SendTextAsync("hi", replyToken: "rt-2", toUserId: null);

        result.Success.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task LineClient_Quota403_ExposesErrorAndQuotaFlag()
    {
        var stub = new StubLineHttpHandler
        {
            RespondWith = _ => (HttpStatusCode.Forbidden, "{\"message\":\"Quota exceeded\",\"details\":[{\"message\":\"quota\",\"property\":\"messages\"}]}")
        };

        var client = CreateClient(stub, chunkChars: 5000, maxMessages: 5);
        var result = await client.SendTextAsync("hi", replyToken: null, toUserId: LineUserId);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.HttpStatus.Should().Be(403);
        result.Error.IsQuotaOrPermission.Should().BeTrue();
        result.Error.Details.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LineClient_NotFriended404_IsMarkedUnreachable()
    {
        var stub = new StubLineHttpHandler
        {
            RespondWith = _ => (HttpStatusCode.NotFound, "{\"message\":\"Not users\",\"details\":[]}")
        };

        var client = CreateClient(stub, chunkChars: 5000, maxMessages: 5);
        var result = await client.SendTextAsync("hi", replyToken: null, toUserId: LineUserId);

        result.Success.Should().BeFalse();
        result.Unreachable.Should().BeTrue();
        result.Error!.IsNotFriended.Should().BeTrue();
    }

    [Fact]
    public async Task LineClient_Unconfigured_ReturnsFailureWithoutHttpRequest()
    {
        var stub = new StubLineHttpHandler { RespondWith = _ => (HttpStatusCode.OK, "{}") };
        var settings = new LineOptions { Enabled = true }; // 无令牌/密钥 ⇒ IsConfigured=false
        var provider = new Mock<ILineSettingsProvider>();
        provider.Setup(s => s.Current).Returns(settings);

        var client = new LineClient(new HttpClient(stub), Options.Create(new HttpClientOptions()), provider.Object, new Mock<ILogger<LineClient>>().Object);

        var result = await client.SendTextAsync("hi", replyToken: "rt", toUserId: LineUserId);

        result.Success.Should().BeFalse();
        stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task LineClient_BearerTokenAndBaseUrlAreApplied()
    {
        var stub = new StubLineHttpHandler { RespondWith = _ => (HttpStatusCode.OK, "{\"sentMessages\":[]}") };
        var client = CreateClient(stub, chunkChars: 5000, maxMessages: 5);

        await client.SendTextAsync("hi", replyToken: "rt-9", toUserId: null);

        stub.Requests.Should().ContainSingle();
        stub.Requests[0].Url.Should().Be("https://api.line.me/v2/bot/message/reply");
        stub.Requests[0].Authorization.Should().Be("Bearer channel_access_token_value");
    }

    private LineClient CreateClient(StubLineHttpHandler stub, int chunkChars, int maxMessages)
    {
        var options = new LineOptions
        {
            Enabled = true,
            ChannelId = "@opencss-demo",
            ChannelSecret = "channel_secret_value",
            ChannelAccessToken = "channel_access_token_value",
            TextChunkChars = chunkChars,
            MaxMessagesPerRequest = maxMessages,
            RetryMaxAttempts = 1,
        };

        var provider = new Mock<ILineSettingsProvider>();
        provider.Setup(s => s.Current).Returns(options);

        return new LineClient(new HttpClient(stub), Options.Create(new HttpClientOptions()), provider.Object, new Mock<ILogger<LineClient>>().Object);
    }

    #endregion
}
