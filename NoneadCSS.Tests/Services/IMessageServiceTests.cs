using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
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
using Xunit;
using ConversationEntity = NoneadCSS.Domain.Entities.Conversation;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// IMessageService 的行为测试：全部走 Mock（不访问网络/数据库）。
/// 重点覆盖渠道特有的守卫：未配置、会话来源不符、传输模式与身份模式不一致、
/// AMB 不允许冷启动私聊、bridge 回复沿用入站身份命名空间；以及版本下限判断。
/// </summary>
public class IMessageServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IIMessageSettingsProvider> _settingsProvider = new();
    private readonly Mock<IIMessageClient> _client = new();
    private readonly Mock<ICurrentUserAccessor> _currentUser = new();
    private readonly Mock<INotificationPublisher> _notifications = new();
    private readonly Mock<IConfiguration> _configuration = new();
    private readonly Mock<IHostEnvironment> _environment = new();
    private readonly Mock<IConversationRepository> _conversationRepo = new();
    private readonly Mock<ICustomerRepository> _customerRepo = new();
    private readonly Mock<IThreadRepository> _threadRepo = new();
    private readonly Mock<IAttachmentRepository> _attachmentRepo = new();
    private readonly Mock<IUnitOfWorkTransaction> _transaction = new();

    public IMessageServiceTests()
    {
        _unitOfWork.Setup(u => u.Conversations).Returns(_conversationRepo.Object);
        _unitOfWork.Setup(u => u.Customers).Returns(_customerRepo.Object);
        _unitOfWork.Setup(u => u.Threads).Returns(_threadRepo.Object);
        _unitOfWork.Setup(u => u.Attachments).Returns(_attachmentRepo.Object);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _unitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_transaction.Object);
        _environment.SetupGet(e => e.ContentRootPath).Returns(Path.Combine(AppContext.BaseDirectory, "app"));
    }

    private IMessageService CreateService(IMessageOptions options)
    {
        _settingsProvider.SetupGet(p => p.Current).Returns(options);
        _settingsProvider.Setup(p => p.GetCurrentAsync()).ReturnsAsync(options);
        return new IMessageService(
            _unitOfWork.Object,
            _encryption.Object,
            _settingsProvider.Object,
            _client.Object,
            _currentUser.Object,
            _notifications.Object,
            NullLogger<IMessageService>.Instance,
            _configuration.Object,
            _environment.Object);
    }

    private static IMessageOptions BridgeOptions(bool enabled = true) => new()
    {
        Mode = IMessageModes.Bridge,
        Enabled = enabled,
        BaseUrl = "http://127.0.0.1:1234",
        Password = "bridge-password",
        WebhookPath = "/api/imessage/bluebubbles/webhook",
        MinServerVersion = "1.0.0",
        StatusCheckTimeoutSeconds = 8,
    };

    private static IMessageOptions AmbOptions() => new()
    {
        Mode = IMessageModes.AmbMsp,
        Enabled = true,
        ApiBaseUrl = "https://msp.example.invalid/v1",
        BusinessIdentifier = "urn:biz:11111111-2222-3333-4444-555555555555",
        WebhookSecret = "msp-webhook-secret",
        StatusCheckTimeoutSeconds = 8,
    };

    private static ConversationEntity IMessageConversation(string scopedChannelId) => new()
    {
        Id = 100,
        Number = 100,
        SourceType = SourceType.IMessage,
        Type = ConversationType.Chat,
        Status = ConversationStatus.Active,
        CustomerId = 0,
        Customer = new Customer { Id = 7, Channel = "imessage", ChannelId = scopedChannelId },
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task ReplyAsync_WhenDisabledOrUnconfigured_ReturnsFriendlyError_AndSendsNothing()
    {
        var service = CreateService(BridgeOptions(enabled: false));

        var result = await service.ReplyAsync(new IMessageReplyRequest { ConversationId = 100, Content = "hi" }, userId: 1);

        Assert.False(result.Sent);
        Assert.Contains("未启用或未配置", result.ErrorMessage);
        _client.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_MissingContentAndAttachments_IsRejected()
    {
        var service = CreateService(BridgeOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest { ConversationId = 100 }, userId: 1);

        Assert.False(result.Sent);
        Assert.Contains("至少提供一项", result.ErrorMessage);
    }

    [Fact]
    public async Task ReplyAsync_ConversationNotFound_ReturnsError()
    {
        _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(404)).ReturnsAsync((ConversationEntity?)null);
        var service = CreateService(BridgeOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest { ConversationId = 404, Content = "hi" }, userId: 1);

        Assert.False(result.Sent);
        Assert.Contains("不存在", result.ErrorMessage);
    }

    [Fact]
    public async Task ReplyAsync_RejectsNonIMessageConversation()
    {
        var conversation = IMessageConversation("bridge:+15551234567");
        conversation.SourceType = SourceType.WhatsApp;
        _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(conversation.Id)).ReturnsAsync(conversation);
        var service = CreateService(BridgeOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest { ConversationId = conversation.Id, Content = "hi" }, userId: 1);

        Assert.False(result.Sent);
        Assert.Contains("不是 iMessage", result.ErrorMessage);
    }

    [Fact]
    public async Task ReplyAsync_ModeMismatch_WarnsOldConversationsCannotBeReplied()
    {
        // 身份里写的是 bridge 命名空间，当前配置却是 AMB ⇒ 不能拿 bridge 的 id 去发 AMB
        _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(100))
            .ReturnsAsync(IMessageConversation("bridge:+15551234567"));
        var service = CreateService(AmbOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest { ConversationId = 100, Content = "hi" }, userId: 1);

        Assert.False(result.Sent);
        Assert.Contains("不一致", result.ErrorMessage);
        _client.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_AmbMode_RefusesColdStartPrivateChat()
    {
        // Apple 规则：AMB 永不允许冷启动私聊 ⇒ 会话里没有客户消息就不发送
        _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(100))
            .ReturnsAsync(IMessageConversation("amb_msp:urn:mbid:opaque-1"));
        _threadRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(Array.Empty<ConversationThread>());
        var service = CreateService(AmbOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest { ConversationId = 100, Content = "hi" }, userId: 1);

        Assert.False(result.Sent);
        Assert.Contains("冷启动", result.ErrorMessage);
        _client.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_Bridge_UsesNormalizedHandleAsDestinationAndPersistsThread()
    {
        var conversation = IMessageConversation("bridge:+15551234567");
        _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(100)).ReturnsAsync(conversation);
        _client.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync("oc-abc123");
        _threadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .Callback<ConversationThread>(t => t.Id = 555)
            .ReturnsAsync((ConversationThread t) => t);
        var service = CreateService(BridgeOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest { ConversationId = 100, Content = "您好，正在为您查询" }, userId: 9);

        Assert.True(result.Sent);
        Assert.True(result.ThreadCreated);
        Assert.Equal(555, result.ThreadId);
        // bridge 的目的地必须是 chatGuid（保留服务前缀），不是裸 handle —— 裸 handle 会被桥接服务器拒收或降级
        _client.Verify(c => c.SendTextMessageAsync("iMessage;-;+15551234567", "您好，正在为您查询", It.IsAny<string?>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(), Times.Once);
        Assert.Equal(SourceVia.User, conversation.LastReplyFrom);
    }

    [Fact]
    public async Task ReplyAsync_BridgeSendFails_DoesNotPersistThread()
    {
        var conversation = IMessageConversation("bridge:+15551234567");
        _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(100)).ReturnsAsync(conversation);
        _client.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string?)null);
        var service = CreateService(BridgeOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest { ConversationId = 100, Content = "hi" }, userId: 9);

        Assert.False(result.Sent);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        _threadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    [Fact]
    public async Task GetStatusAsync_Unconfigured_SkipsConnectivityProbe()
    {
        var options = BridgeOptions();
        options.Password = null;
        var service = CreateService(options);

        var status = await service.GetStatusAsync();

        Assert.False(status.Configured);
        Assert.False(status.ApiReachable);
        _client.Verify(c => c.TestConnectionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatusAsync_Bridge_ExposesServerVersionAndRegisteredWebhook()
    {
        _client.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _client.Setup(c => c.GetServerVersionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("1.9.12");
        _client.Setup(c => c.ListRegisteredWebhooksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "https://css.example.invalid/api/imessage/bluebubbles/webhook" });
        var service = CreateService(BridgeOptions());

        var status = await service.GetStatusAsync();

        Assert.True(status.ApiReachable);
        Assert.Equal("1.9.12", status.ServerVersion);
        Assert.Contains("/api/imessage/bluebubbles/webhook", status.RegisteredWebhookUrl);
        Assert.Equal("bridge", status.Mode);
    }

    [Theory]
    [InlineData("0.3.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.9.12", "1.0.0", false)]
    [InlineData("1.9", "1.9.1", true)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("v2.0.0-beta1", "1.0.0", false)]
    public void IsVersionLower_ComparesNumericSegments(string? actual, string? required, bool expected)
    {
        Assert.Equal(expected, IMessageService.IsVersionLower(actual, required));
    }

    [Fact]
    public async Task TestConnectionAsync_AmbAppleDirect_MintsTokenButNeverCallsApple()
    {
        var options = new IMessageOptions
        {
            Mode = IMessageModes.AmbAppleDirect,
            ExperimentalAppleDirect = true,
            Enabled = true,
            GatewayBaseUrl = "https://mspgw.push.apple.com",
            MspId = "MSP-42",
            MspSharedSecret = "hs256-secret",
            BusinessId = "urn:biz:11111111-2222-3333-4444-555555555555",
            StatusCheckTimeoutSeconds = 8,
        };
        _client.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = CreateService(options);

        var result = await service.TestConnectionAsync(testHandle: "+15551234567");

        Assert.True(result.Success);
        // Apple 直连路线需要 MSP 资格：本系统只做参数/令牌自检，绝不向 Apple 网关发测试消息
        _client.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    // ── 审计 #5：附件发送结果必须反映真实送达情况（旧实现无论附件是否全部失败都返回 Sent=true）──

    private static Attachment AttachmentFixture(int id, string fileName, string storedFileName) => new()
    {
        Id = id,
        FileName = fileName,
        StoredFileName = storedFileName,
        FileDir = string.Empty,
        MimeType = "application/octet-stream",
        Size = 8,
    };

    [Fact]
    public async Task ReplyAsync_AllAttachmentsFailedWithNoContent_ReportsNotSent_AndPersistsNothing()
    {
        var conversation = IMessageConversation("bridge:+15551234567");
        _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(100)).ReturnsAsync(conversation);
        // 没有配置 Attachment:UploadPath ⇒ ResolveAttachmentPath 返回 null ⇒ 两个附件都发不出去
        _attachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment>
            {
                AttachmentFixture(11, "photo.png", "missing-11.png"),
                AttachmentFixture(12, "report.pdf", "missing-12.pdf"),
            });
        var service = CreateService(BridgeOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest
        {
            ConversationId = 100,
            AttachmentIds = [11, 12],
        }, userId: 9);

        Assert.False(result.Sent);
        Assert.False(result.ThreadCreated);
        Assert.Contains("附件全部发送失败，消息未发出：", result.ErrorMessage);
        Assert.NotNull(result.FailedAttachments);
        Assert.Equal(2, result.FailedAttachments!.Count);
        Assert.Contains("photo.png", result.FailedAttachments);
        Assert.Contains("report.pdf", result.FailedAttachments);
        // 什么都没送达：既不该调客户端，也不该在会话里留下一条「已发送」的记录
        _client.Verify(c => c.SendAttachmentAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _threadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
        _transaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ReplyAsync_AllAttachmentsFailedButContentSent_ReportsSentWithFailedAttachments()
    {
        var conversation = IMessageConversation("bridge:+15551234567");
        _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(100)).ReturnsAsync(conversation);
        _client.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync("oc-text-1");
        _attachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Attachment> { AttachmentFixture(21, "photo.png", "missing-21.png") });
        _threadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .Callback<ConversationThread>(t => t.Id = 556)
            .ReturnsAsync((ConversationThread t) => t);
        var service = CreateService(BridgeOptions());

        var result = await service.ReplyAsync(new IMessageReplyRequest
        {
            ConversationId = 100,
            Content = "附件稍后补发",
            AttachmentIds = [21],
        }, userId: 9);

        // 正文已送达 ⇒ 算成功，但失败清单必须回给调用方（不破坏原契约：新增可选字段）
        Assert.True(result.Sent);
        Assert.True(result.ThreadCreated);
        Assert.Equal(556, result.ThreadId);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.FailedAttachments);
        Assert.Single(result.FailedAttachments!);
        Assert.Equal("photo.png", result.FailedAttachments![0]);
        _threadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Once);
        _transaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task ReplyAsync_PartialAttachmentFailure_KeepsSentAndListsOnlyFailedFiles()
    {
        var uploadRoot = Path.Combine(Path.GetTempPath(), "imessage-attach-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(uploadRoot);
        try
        {
            const string storedGood = "stored-good.png";
            await File.WriteAllTextAsync(Path.Combine(uploadRoot, storedGood), "png-bytes");
            _configuration.Setup(c => c["Attachment:UploadPath"]).Returns(uploadRoot);

            var conversation = IMessageConversation("bridge:+15551234567");
            _conversationRepo.Setup(r => r.GetByIdForUpdateAsync(100)).ReturnsAsync(conversation);
            _attachmentRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
                .ReturnsAsync(new List<Attachment>
                {
                    AttachmentFixture(31, "good.png", storedGood),
                    AttachmentFixture(32, "gone.pdf", "stored-gone.pdf"),
                });
            _client.Setup(c => c.SendAttachmentAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("att-31");
            _threadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
                .Callback<ConversationThread>(t => t.Id = 557)
                .ReturnsAsync((ConversationThread t) => t);
            var service = CreateService(BridgeOptions());

            var result = await service.ReplyAsync(new IMessageReplyRequest
            {
                ConversationId = 100,
                AttachmentIds = [31, 32],
            }, userId: 9);

            Assert.True(result.Sent);
            Assert.True(result.ThreadCreated);
            Assert.NotNull(result.FailedAttachments);
            Assert.Single(result.FailedAttachments!);
            Assert.Equal("gone.pdf", result.FailedAttachments![0]);
            _client.Verify(c => c.SendAttachmentAsync("iMessage;-;+15551234567", It.IsAny<byte[]>(), "good.png", It.IsAny<string>()), Times.Once);
            _client.Verify(c => c.SendAttachmentAsync(It.IsAny<string>(), It.IsAny<byte[]>(), "gone.pdf", It.IsAny<string>()), Times.Never);
        }
        finally
        {
            Directory.Delete(uploadRoot, recursive: true);
        }
    }
}
