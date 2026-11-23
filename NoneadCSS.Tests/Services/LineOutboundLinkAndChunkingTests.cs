using System.Linq.Expressions;
using System.Net;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
using NoneadCSS.Infrastructure.Services;
using NoneadCSS.Infrastructure.Services.Line;
using NoneadCSS.Tests.Helpers;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// LINE 出站侧的两条硬约束补强测试（与 LineServiceTests.cs 互补，不重复它的用例）：
///
/// A) 附件直链必须「签得出去、验得回来」：LINE 没有聊天媒体上传接口，出站附件只能引用本部署的公网 HTTPS 直链
///    （GET /api/line/media/{id}?exp=&lt;unix&gt;&amp;sig=&lt;hmac&gt;，见 NoneadCSS.Api/Controllers/LineMediaController.cs:58）。
///    因此 LineService 生成的 URL 必须能被 LineMediaLinkSigner.Validate 用同一个 ChannelSecret 验通，
///    换密钥 / 过期 / 改 attachmentId 都必须验不过。
///
/// B) 文本分片不得切断代理对（emoji），且优先在换行处断开、单请求消息数 ≤ MaxMessagesPerRequest。
///    判据来自 NoneadCSS.Infrastructure/Services/Line/LineClient.cs:804 SplitText。
/// </summary>
public class LineOutboundLinkAndChunkingTests : IDisposable
{
    private const int ConversationId = 4210;
    private const string LineUserId = "U4af8f4c5c6b3a3ff3e0d1f2a9b8c7d65";
    private const string ChannelSecret = "channel_secret_value";
    private const string PublicBaseUrl = "https://css.example.com";

    private readonly string _uploadRoot = Path.Combine(Path.GetTempPath(), "line-outbound-" + Guid.NewGuid().ToString("N"));

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
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction = new();

    private readonly LineOptions _options = new()
    {
        Enabled = true,
        ChannelId = "@opencss-line-demo",
        ChannelSecret = ChannelSecret,
        ChannelAccessToken = "channel_access_token_value",
        AttachmentPublicBaseUrl = PublicBaseUrl,
        AttachmentPublicUrlTtlMinutes = 120,
        TextChunkChars = 5000,
        MaxMessagesPerRequest = 5,
        HumanHandoverMuteMinutes = 0, // 不写接管备注，保持断言聚焦
        LoadingIndicatorEnabled = false,
        MarkAsReadEnabled = false,
    };

    private IReadOnlyList<LineMessageObject> _sentMessages = [];
    private string? _sentReplyToken;
    private string? _sentToUserId;

    public LineOutboundLinkAndChunkingTests()
    {
        Directory.CreateDirectory(_uploadRoot);

        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(new Mock<ISystemOptionRepository>().Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockUnitOfWork.Setup(u => u.ClearChanges());

        _mockTransaction.Setup(t => t.CommitAsync()).Returns(Task.CompletedTask);
        _mockTransaction.Setup(t => t.RollbackAsync()).Returns(Task.CompletedTask);

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(ConversationId)).ReturnsAsync(new Conversation
        {
            Id = ConversationId,
            Number = 4210,
            CustomerId = 3100,
            MailboxId = 1,
            SourceType = SourceType.Line,
            Type = ConversationType.Chat,
            Status = ConversationStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
        });

        _mockCustomerRepo.Setup(r => r.GetByIdAsync(3100)).ReturnsAsync(new Customer
        {
            Id = 3100,
            Channel = "line", // LineIngestionService.LineChannelConst
            ChannelId = LineUserId,
            FirstName = "LINE",
            LastName = "客户",
        });

        // 无客户线程 ⇒ replyToken 不可用 ⇒ 走 push（本组用例只关心消息体本身）
        _mockThreadRepo.Setup(r => r.GetLatestCustomerThreadAsync(ConversationId)).ReturnsAsync((ConversationThread?)null);
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) =>
            {
                t.Id = 9100;
                return t;
            });
        _mockThreadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(new List<ConversationThread>());

        _mockAttachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync((IEnumerable<int> ids) => ids.Select(id => new Attachment
            {
                Id = id,
                FileName = "发票.png",
                MimeType = "image/png",
                Size = 1024,
                Type = AttachmentType.Image,
                FileDir = "2026/11/16",
                StoredFileName = $"stored-{id}.png",
            }).ToList());

        _mockConfiguration.Setup(c => c["Attachment:UploadPath"]).Returns(_uploadRoot);
        _mockConfiguration.Setup(c => c["App:BaseUrl"]).Returns(PublicBaseUrl);

        _mockEncryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string s) => "cipher:" + s);
        _mockEncryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.StartsWith("cipher:") ? s[7..] : s);

        _mockNotifier.Setup(n => n.NotifyMailboxAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);

        // 附件实体文件必须存在（LineService 会 File.Exists 校验后才签直链）
        var dayDir = Path.Combine(_uploadRoot, "2026/11/16");
        Directory.CreateDirectory(dayDir);
        File.WriteAllBytes(Path.Combine(dayDir, "stored-5.png"), new byte[1024]);

        _mockSettingsProvider.Setup(s => s.Current).Returns(_options);
        _mockSettingsProvider.Setup(s => s.GetCurrentAsync()).ReturnsAsync(_options);
        _mockSettingsProvider.Setup(s => s.RefreshAsync()).Returns(Task.CompletedTask);

        _mockClient.Setup(c => c.SendMessagesAsync(It.IsAny<IReadOnlyList<LineMessageObject>>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<LineMessageObject> messages, string? replyToken, string? toUserId, CancellationToken _) =>
            {
                _sentMessages = messages;
                _sentReplyToken = replyToken;
                _sentToUserId = toUserId;
            })
            .ReturnsAsync(new LineSendResult { Success = true, Via = "push", ExternalMessageId = "line-out-1", ChunkCount = 1 });
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

    private LineService CreateLineService()
        => new(
            _mockUnitOfWork.Object,
            _mockEncryption.Object,
            _mockSettingsProvider.Object,
            _mockClient.Object,
            _mockCurrentUser.Object,
            _mockNotifier.Object,
            new Mock<ILogger<LineService>>().Object,
            _mockConfiguration.Object,
            Mock.Of<IHostEnvironment>(e => e.ContentRootPath == _uploadRoot),
            null);

    #region A) 附件直链：签出去 Must 能验回来

    [Fact]
    public async Task ReplyAsync_ImageAttachment_SignedLink_RoundTripsThroughValidator()
    {
        var service = CreateLineService();

        var result = await service.ReplyAsync(new LineReplyRequest
        {
            ConversationId = ConversationId,
            Content = "这是发票",
            AttachmentIds = [5],
        }, 7);

        result.Sent.Should().BeTrue();
        result.MediaErrors.Should().BeEmpty();
        _sentMessages.Should().HaveCount(2); // text + image
        _sentToUserId.Should().Be(LineUserId);
        _sentReplyToken.Should().BeNull();

        var image = _sentMessages.Single(m => m.Type == "image");
        image.OriginalContentUrl.Should().NotBeNullOrWhiteSpace();
        image.OriginalContentUrl!.Should().StartWith($"{PublicBaseUrl}/api/line/media/5?");

        var now = DateTimeOffset.UtcNow;
        var (exp, sig) = ReadLinkParams(image.OriginalContentUrl!);

        // 同一密钥 + 未过期 ⇒ 必须验通（这正是 LineMediaController.GetAttachment 的判据）
        LineMediaLinkSigner.Validate(5, exp, sig, ChannelSecret, now).Should().BeTrue();

        // 越界与篡改必须验不过
        LineMediaLinkSigner.Validate(6, exp, sig, ChannelSecret, now).Should().BeFalse("直链绑定了 attachmentId");
        LineMediaLinkSigner.Validate(5, exp, sig, "other_channel_secret", now).Should().BeFalse();
        LineMediaLinkSigner.Validate(5, exp, sig + "ab", ChannelSecret, now).Should().BeFalse();
        LineMediaLinkSigner.Validate(5, exp, null, ChannelSecret, now).Should().BeFalse();

        // 过期 ⇒ 拒绝；但在 TTL 内（含 60s 时钟偏移容差）仍然接受
        LineMediaLinkSigner.Validate(5, exp, sig, ChannelSecret, now.AddMinutes(125)).Should().BeFalse();
        LineMediaLinkSigner.Validate(5, exp, sig, ChannelSecret, now.AddMinutes(115)).Should().BeTrue();

        // TTL 取 AttachmentPublicUrlTtlMinutes=120 ⇒ exp 应落在 119~121 分钟之后
        exp.Should().BeGreaterThan(now.ToUnixTimeSeconds() + 118 * 60);
        exp.Should().BeLessThan(now.ToUnixTimeSeconds() + 122 * 60);

        // preview 直链同 id 同签名（本仓库不做缩放，只多一个 /preview 路径）
        image.PreviewImageUrl.Should().NotBeNullOrWhiteSpace();
        image.PreviewImageUrl!.Should().StartWith($"{PublicBaseUrl}/api/line/media/5/preview?");
        var (previewExp, previewSig) = ReadLinkParams(image.PreviewImageUrl!);
        LineMediaLinkSigner.Validate(5, previewExp, previewSig, ChannelSecret, now).Should().BeTrue();

        var text = _sentMessages.Single(m => m.Type == "text");
        text.Text.Should().Be("这是发票");
    }

    [Fact]
    public void Signer_BuildUrlAndValidate_RoundTripWithKnownSecret()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);
        var url = LineMediaLinkSigner.BuildUrl("https://cdn.example.com/", 42, expires, ChannelSecret);

        url.Should().StartWith("https://cdn.example.com/api/line/media/42?exp="); // base 尾部斜杠被规整
        var (exp, sig) = ReadLinkParams(url);
        exp.Should().Be(expires.ToUnixTimeSeconds());
        sig.Should().MatchRegex("^[0-9a-f]{64}$", "HMAC-SHA256 十六进制小写");

        LineMediaLinkSigner.Validate(42, exp, sig, ChannelSecret, DateTimeOffset.UtcNow).Should().BeTrue();
        LineMediaLinkSigner.Validate(42, exp, sig.ToUpperInvariant(), ChannelSecret, DateTimeOffset.UtcNow)
            .Should().BeTrue("大小写不敏感不影响定长比较语义");
        LineMediaLinkSigner.Validate(42, expires.AddSeconds(-61).ToUnixTimeSeconds(), sig, ChannelSecret, DateTimeOffset.UtcNow)
            .Should().BeFalse("签名绑定了 exp，改 exp 即不匹配");
        LineMediaLinkSigner.Validate(42, 0, sig, ChannelSecret, DateTimeOffset.UtcNow).Should().BeFalse();
    }

    #endregion

    #region B) 文本分片：代理对与换行

    [Fact]
    public async Task SendTextAsync_EmojiHeavyText_NeverSplitsSurrogatePair()
    {
        // 1 个 BMP 字符 + 若干 emoji（每个 2 个 UTF-16 单元）⇒ 5000 的切点必然落在代理对中间，SplitText 必须退一格
        var text = "a" + string.Concat(Enumerable.Repeat("🙂", 4000));
        text.Length.Should().BeGreaterThan(5000);

        var stub = new StubLineHttpHandler { RespondWith = _ => (HttpStatusCode.OK, """{"sentMessages":[{"id":"m1","quoteToken":"q"}]}""") };
        var client = CreateLineClient(stub, chunkChars: 5000, maxMessages: 5);

        var result = await client.SendTextAsync(text, replyToken: "rt-1", toUserId: null);

        result.Success.Should().BeTrue();
        var pieces = AllTextPieces(stub);

        pieces.Should().NotBeEmpty();
        pieces.Should().OnlyContain(p => p.Length <= 5000);
        string.Concat(pieces).Should().Be(text, "分片不得增删内容");
        pieces.Should().OnlyContain(p => !char.IsHighSurrogate(p[p.Length - 1]), "分片末尾不能是孤立高代理项");
        pieces.Skip(1).Should().OnlyContain(p => !char.IsLowSurrogate(p[0]), "分片开头不能是孤立低代理项");
    }

    [Fact]
    public async Task SendTextAsync_LongTextWithNewlines_PrefersLineBreakOverHardCut()
    {
        var text = new string('a', 4000) + "\n" + new string('b', 4000);
        var stub = new StubLineHttpHandler { RespondWith = _ => (HttpStatusCode.OK, """{"sentMessages":[{"id":"m1","quoteToken":"q"}]}""") };
        var client = CreateLineClient(stub, chunkChars: 5000, maxMessages: 5);

        await client.SendTextAsync(text, replyToken: "rt-2", toUserId: null);

        var pieces = AllTextPieces(stub);

        pieces.Should().HaveCountGreaterThan(1);
        pieces[0].Should().Be(new string('a', 4000), "换行位置过半 ⇒ 优先在换行处断开，而不是硬切 5000");
        string.Concat(pieces).Should().Be(text);
        pieces.Should().OnlyContain(p => p.Length <= 5000);
    }

    [Fact]
    public async Task SendTextAsync_ManyPieces_KeepsEveryRequestWithinMaxMessagesPerRequest()
    {
        var stub = new StubLineHttpHandler { RespondWith = _ => (HttpStatusCode.OK, """{"sentMessages":[{"id":"m1","quoteToken":"q"}]}""") };
        var client = CreateLineClient(stub, chunkChars: 100, maxMessages: 2);

        var result = await client.SendTextAsync(new string('\u263a', 300), replyToken: null, toUserId: LineUserId);

        result.Success.Should().BeTrue();

        // 300 字符 / 每片 100 ⇒ 3 片；单请求上限 2 条 ⇒ 2 个请求（2+1）。
        // 分片数 ≠ 请求数：MaxMessagesPerRequest 是单请求的上限，不是"每片一个请求"。
        AllTextPieces(stub).Should().HaveCount(3, "300 字符按 100 切 ⇒ 3 片");
        stub.Requests.Should().HaveCount(2);
        stub.Requests.Should().OnlyContain(r => r.Url.EndsWith("/v2/bot/message/push"));
        foreach (var request in stub.Requests)
        {
            JsonDocument.Parse(request.Body!).RootElement.GetProperty("messages").GetArrayLength().Should().BeLessThanOrEqualTo(2);
        }
    }

    #endregion

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>按请求顺序摊平所有 text 分片（一次请求可携带多条 messages[]，只取 messages[0] 会漏片）</summary>
    private static List<string> AllTextPieces(StubLineHttpHandler stub) =>
        [.. stub.Requests
            .SelectMany(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("messages").EnumerateArray()
                .Select(m => m.GetProperty("text").GetString()!))];

    private static (long Exp, string Sig) ReadLinkParams(string url)
    {
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        var exp = long.Parse(query["exp"]!);
        var sig = query["sig"]!;
        return (exp, sig);
    }

    private static LineClient CreateLineClient(StubLineHttpHandler stub, int chunkChars, int maxMessages)
    {
        var options = new LineOptions
        {
            Enabled = true,
            ChannelId = "@opencss-line-demo",
            ChannelSecret = ChannelSecret,
            ChannelAccessToken = "channel_access_token_value",
            TextChunkChars = chunkChars,
            MaxMessagesPerRequest = maxMessages,
            RetryMaxAttempts = 0,
        };

        var provider = new Mock<ILineSettingsProvider>();
        provider.Setup(s => s.Current).Returns(options);

        return new LineClient(new HttpClient(stub), Options.Create(new HttpClientOptions()), provider.Object, new Mock<ILogger<LineClient>>().Object);
    }
}
