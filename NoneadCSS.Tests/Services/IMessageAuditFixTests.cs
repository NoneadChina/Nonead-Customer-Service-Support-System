using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Api.Controllers;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Services;
using NoneadCSS.Infrastructure.Services.IMessage;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// iMessage 渠道审计修复的回归测试（对应 docs/integration/imessage.md 的契约）：
/// 1) AMB(MSP) 入站：WebhookSecret 未配置必须 403，而不是"跳过验签直接放行"；
/// 2) Bridge 入站：配置了共享密钥后错误密钥 403、正确密钥 200；未配置时保持旧放行行为；
/// 3) AMB 出站：报文必须是文档规定的扁平信封（小写 type / v / id / sourceId / destinationId / locale / body 字符串）；
/// 4) 出站 410 必须归类为"永久失败"（IsTransient=false），队列据此终止重试。
/// </summary>
public class IMessageAuditFixTests
{
    // ------------------------------------------------------------
    // 1) AMB 入站：WebhookSecret 空 ⇒ 403（修复前是跳过验签、任意 Source-Id 都能建客户/会话）
    // ------------------------------------------------------------
    [Fact]
    public async Task AmbIngress_WhenWebhookSecretBlank_Returns403_AndNeverIngests()
    {
        var options = new IMessageOptions
        {
            Mode = IMessageModes.AmbMsp,
            Enabled = true,
            ApiBaseUrl = "https://msp.example.invalid/v1",
            BusinessIdentifier = "urn:biz:1111",
            WebhookSecret = null, // 关键：未配置验签密钥
        };

        var (controller, ingestion, _) = CreateController(options);
        SetBody(controller, "{\"id\":\"amb-1\",\"sourceId\":\"urn:mbid:forged\"}");

        var result = await controller.ReceiveAmbMessage("urn:mbid:forged", "urn:biz:1111", "amb-1", null);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        // 未通过鉴权的请求绝不能进入解析/落库路径
        ingestion.Verify(
            i => i.ProcessAmbInboundAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ------------------------------------------------------------
    // 2) Bridge 入站：错误共享密钥 403（修复前无任何校验）
    // ------------------------------------------------------------
    [Fact]
    public async Task BridgeIngress_WhenSecretConfigured_RejectsWrongSecret_With403()
    {
        var options = new IMessageOptions
        {
            Mode = IMessageModes.Bridge,
            Enabled = true,
            BridgeWebhookSecret = "expected-secret",
        };

        var (controller, ingestion, _) = CreateController(options);
        SetBody(controller, "{\"type\":\"new-message\",\"data\":{\"guid\":\"g-1\"}}");
        controller.Request.Headers["X-Webhook-Secret"] = "wrong-secret";

        var result = await controller.ReceiveBlueBubblesWebhook();

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        ingestion.Verify(
            i => i.ProcessWebhookPayloadAsync(It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    // ------------------------------------------------------------
    // 2b) Bridge 入站：正确共享密钥（header 或 ?secret=）放行，且保持既有响应语义（200）
    // ------------------------------------------------------------
    [Fact]
    public async Task BridgeIngress_WhenSecretConfigured_AcceptsCorrectSecretHeader_AndIngests()
    {
        var options = new IMessageOptions
        {
            Mode = IMessageModes.Bridge,
            Enabled = true,
            BridgeWebhookSecret = "expected-secret",
        };

        var (controller, ingestion, _) = CreateController(options);
        SetBody(controller, "{\"type\":\"new-message\",\"data\":{\"guid\":\"g-2\"}}");
        controller.Request.Headers["X-Webhook-Secret"] = "expected-secret";

        var result = await controller.ReceiveBlueBubblesWebhook();

        Assert.IsType<OkResult>(result);
        ingestion.Verify(
            i => i.ProcessWebhookPayloadAsync(It.IsAny<string?>(), IMessageModes.Bridge),
            Times.Once);
    }

    [Fact]
    public async Task BridgeIngress_WhenSecretConfigured_AcceptsQuerySecret_ForBridgesThatCannotSetHeaders()
    {
        var options = new IMessageOptions
        {
            Mode = IMessageModes.Bridge,
            Enabled = true,
            BridgeWebhookSecret = "expected-secret",
        };

        var (controller, ingestion, _) = CreateController(options);
        SetBody(controller, "{\"type\":\"new-message\",\"data\":{\"guid\":\"g-3\"}}");
        controller.Request.QueryString = new QueryString("?secret=expected-secret");

        var result = await controller.ReceiveBlueBubblesWebhook();

        Assert.IsType<OkResult>(result);
        ingestion.Verify(
            i => i.ProcessWebhookPayloadAsync(It.IsAny<string?>(), IMessageModes.Bridge),
            Times.Once);
    }

    // ------------------------------------------------------------
    // 2c) 兼容既有安装：未配置共享密钥时保持旧的放行行为（只告警一次），不硬失败
    // ------------------------------------------------------------
    [Fact]
    public async Task BridgeIngress_WhenSecretNotConfigured_KeepsLegacyAllowBehaviour()
    {
        var options = new IMessageOptions
        {
            Mode = IMessageModes.Bridge,
            Enabled = true,
            BridgeWebhookSecret = null,
        };

        var (controller, ingestion, _) = CreateController(options);
        SetBody(controller, "{\"type\":\"new-message\",\"data\":{\"guid\":\"g-4\"}}");

        var result = await controller.ReceiveBlueBubblesWebhook();

        Assert.IsType<OkResult>(result);
        ingestion.Verify(
            i => i.ProcessWebhookPayloadAsync(It.IsAny<string?>(), IMessageModes.Bridge),
            Times.Once);
    }

    // ------------------------------------------------------------
    // 3) AMB 出站：报文必须是文档 §3 line 32 的扁平信封，type 为小写 text
    // ------------------------------------------------------------
    [Fact]
    public async Task AmbOutbound_EnvelopeMatchesDocumentedShape()
    {
        var handler = new CapturingHandler();
        var client = CreateAmbClient(handler);

        var outcome = await client.SendTextMessageWithOutcomeAsync("urn:mbid:dest-1", "你好，iMessage");

        Assert.True(outcome.Success);
        Assert.NotNull(handler.RequestBody);

        using var doc = JsonDocument.Parse(handler.RequestBody!);
        var root = doc.RootElement;

        Assert.Equal("text", root.GetProperty("type").GetString());       // 文档要求小写，不是 "TEXT"
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.True(Guid.TryParse(root.GetProperty("id").GetString(), out _)); // id 必须是 UUID
        Assert.Equal("urn:biz:1111", root.GetProperty("sourceId").GetString());
        Assert.Equal("urn:mbid:dest-1", root.GetProperty("destinationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("locale").GetString()));
        Assert.Equal("你好，iMessage", root.GetProperty("body").GetString()); // body 是文本字符串

        // 旧实现的两个错误字段不应再出现
        Assert.False(root.TryGetProperty("text", out _));
    }

    // ------------------------------------------------------------
    // 3b) AMB 出站 id 幂等：AMB 把 id 当**客户端消息标识**，网关按它去重。
    //     同一条排队消息重试时必须复用同一个 id，否则网关视作新消息 ⇒ 客户收到两份。
    // ------------------------------------------------------------
    [Fact]
    public async Task AmbOutbound_SameClientMessageId_ReusesSameIdAcrossRetries()
    {
        var handler = new CapturingHandler();
        var client = CreateAmbClient(handler);

        var first = await client.SendTextMessageWithOutcomeAsync("urn:mbid:dest-6", "hello", "im-queue-4711");
        var firstBody = handler.RequestBody;
        var second = await client.SendTextMessageWithOutcomeAsync("urn:mbid:dest-6", "hello", "im-queue-4711");
        var secondBody = handler.RequestBody;

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotNull(firstBody);
        Assert.NotNull(secondBody);

        var firstId = ReadAmbId(firstBody!);
        var secondId = ReadAmbId(secondBody!);

        // 核心断言：同一幂等键两次发送 ⇒ body.id 完全相同（网关可据此去重）
        Assert.True(Guid.TryParse(firstId, out _));
        Assert.Equal(firstId, secondId);
        // 返回值与信封里的 id 一致（外部消息标识口径统一）
        Assert.Equal(firstId, first.ExternalMessageId);

        // 信封的其余契约字段不受影响：type/v/sourceId/destinationId/locale/body
        using var doc = JsonDocument.Parse(firstBody!);
        var root = doc.RootElement;
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.Equal("urn:biz:1111", root.GetProperty("sourceId").GetString());
        Assert.Equal("urn:mbid:dest-6", root.GetProperty("destinationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("locale").GetString()));
        Assert.Equal("hello", root.GetProperty("body").GetString());
    }

    [Fact]
    public async Task AmbOutbound_DifferentClientMessageIds_ProduceDifferentIds()
    {
        var handler = new CapturingHandler();
        var client = CreateAmbClient(handler);

        await client.SendTextMessageWithOutcomeAsync("urn:mbid:dest-7", "hello", "im-queue-1");
        var firstId = ReadAmbId(handler.RequestBody!);
        await client.SendTextMessageWithOutcomeAsync("urn:mbid:dest-7", "hello", "im-queue-2");
        var secondId = ReadAmbId(handler.RequestBody!);

        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task AmbOutbound_InteractiveSendWithoutClientMessageId_GetsFreshId()
    {
        var handler = new CapturingHandler();
        var client = CreateAmbClient(handler);

        // 交互式发送（坐席回复）没有队列行：两次发送是两条新消息，不能复用 id
        await client.SendTextMessageWithOutcomeAsync("urn:mbid:dest-8", "hello");
        var firstId = ReadAmbId(handler.RequestBody!);
        await client.SendTextMessageWithOutcomeAsync("urn:mbid:dest-8", "hello");
        var secondId = ReadAmbId(handler.RequestBody!);

        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void DeriveAmbMessageId_IsPureFunctionOfQueueItem()
    {
        var a = IMessageIdempotency.DeriveAmbMessageId(4711, 77);

        Assert.Equal(a, IMessageIdempotency.DeriveAmbMessageId(4711, 77));   // 同一队列行 ⇒ 恒等
        Assert.True(Guid.TryParse(a, out _));                               // AMB 要求 id 是 UUID
        Assert.NotEqual(a, IMessageIdempotency.DeriveAmbMessageId(4712, 77)); // 不同队列行 ⇒ 不同
        Assert.NotEqual(a, IMessageIdempotency.DeriveAmbMessageId(4711, 78)); // 不同会话 ⇒ 不同
    }

    // ------------------------------------------------------------
    // 3c) 端到端：同一条队列行重试两次（5xx 可重试），两次 AMB 请求体里的 body.id 必须相同
    // ------------------------------------------------------------
    [Fact]
    public async Task Queue_SameItemRetried_ReusesSameAmbBodyId()
    {
        // 网关 5xx ⇒ 可重试：第一次尝试落回 Pending，第二次仍是同一行（Id/ConversationId 不变）
        var handler = new CapturingHandler
        {
            StatusCode = HttpStatusCode.InternalServerError,
            ResponseBody = "{\"code\":\"TEMPORARY_UNAVAILABLE\"}",
        };
        var client = CreateAmbClient(handler);

        var log = new ImMessageLog
        {
            Id = 501,
            ConversationId = 77,
            CustomerId = 8,
            SourceType = SourceType.IMessage,
            ChannelId = "amb_msp:urn:mbid:dest-5",
            MessageText = "retry me",
            Status = ImMessageLogStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = 4,
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
        };

        var logRepo = new Mock<IImMessageLogRepository>();
        logRepo.Setup(r => r.ResetStaleSendingAsync(It.IsAny<TimeSpan>())).ReturnsAsync(0);
        logRepo.Setup(r => r.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(new List<ImMessageLog> { log });
        logRepo.Setup(r => r.GetByIdTrackedAsync(501)).ReturnsAsync(log);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.ImMessageLogs).Returns(logRepo.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var queue = new ImMessageQueueService(
            unitOfWork.Object, new Mock<IChatChannelMessageSender>().Object, client,
            NullLogger<ImMessageQueueService>.Instance);

        await queue.ProcessQueueAsync();   // 第 1 次尝试：5xx ⇒ Pending（等待重试）
        Assert.Equal(ImMessageLogStatus.Pending, log.Status);

        await queue.ProcessQueueAsync();   // 第 2 次尝试：同一队列行重试
        Assert.Equal(ImMessageLogStatus.Pending, log.Status);
        Assert.Equal(2, handler.RequestBodies.Count);

        var firstId = ReadAmbId(handler.RequestBodies[0]);
        var secondId = ReadAmbId(handler.RequestBodies[1]);

        Assert.Equal(IMessageIdempotency.DeriveAmbMessageId(501, 77), firstId);
        Assert.Equal(firstId, secondId);   // ← 核心缺陷回归：重试必须复用同一个 id
    }

    // ------------------------------------------------------------
    // 4a) 出站 410：必须归类为永久失败（不重试），并保留状态码/平台错误码
    // ------------------------------------------------------------
    [Fact]
    public async Task AmbOutbound_410_IsTerminal_WithStatusCodeAndProviderCode()
    {
        var handler = new CapturingHandler
        {
            StatusCode = HttpStatusCode.Gone,
            ResponseBody = "{\"code\":\"RECIPIENT_CLOSED\"}",
        };
        var client = CreateAmbClient(handler);

        var outcome = await client.SendTextMessageWithOutcomeAsync("urn:mbid:dest-2", "hi");

        Assert.False(outcome.Success);
        Assert.False(outcome.IsTransient);
        Assert.Equal(410, outcome.HttpStatusCode);
        Assert.Equal("RECIPIENT_CLOSED", outcome.ProviderErrorCode);
    }

    // ------------------------------------------------------------
    // 4b) 队列：iMessage 永久失败 ⇒ 直接 Failed（AttemptCount 触顶、清空 NextRetryAt），不再重试
    // ------------------------------------------------------------
    [Fact]
    public async Task Queue_WhenImessageReturns410_MarksLogTerminalFailed_WithoutRetry()
    {
        var log = new ImMessageLog
        {
            Id = 1,
            ConversationId = 10,
            CustomerId = 7,
            SourceType = SourceType.IMessage,
            ChannelId = "amb_msp:urn:mbid:dest-3",
            MessageText = "hi",
            Status = ImMessageLogStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = 4,
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
        };

        var logRepo = new Mock<IImMessageLogRepository>();
        logRepo.Setup(r => r.ResetStaleSendingAsync(It.IsAny<TimeSpan>())).ReturnsAsync(0);
        logRepo.Setup(r => r.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(new List<ImMessageLog> { log });
        logRepo.Setup(r => r.GetByIdTrackedAsync(1)).ReturnsAsync(log);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.ImMessageLogs).Returns(logRepo.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var imessageClient = new Mock<IIMessageClient>();
        imessageClient
            .Setup(c => c.SendTextMessageWithOutcomeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(IMessageSendOutcome.Terminal("用户已关闭会话（410 Gone）", 410, "RECIPIENT_CLOSED"));

        var chatSender = new Mock<IChatChannelMessageSender>();
        var queue = new ImMessageQueueService(
            unitOfWork.Object, chatSender.Object, imessageClient.Object, NullLogger<ImMessageQueueService>.Instance);

        await queue.ProcessQueueAsync();

        // 终态：Failed + AttemptCount 触顶 + 清空重试时间（等价"永不重试"）
        Assert.Equal(ImMessageLogStatus.Failed, log.Status);
        Assert.Equal(log.MaxAttempts, log.AttemptCount);
        Assert.Null(log.NextRetryAt);
        Assert.Contains("410", log.ErrorMessage);

        // 只发一次；且 iMessage 不再经由通用 sender 兜底（那条路径吞掉了状态码）
        // 第 3 个参数必须是该队列行派生的稳定幂等 id（重试复用，见 Queue_SameItemRetried_ReusesSameAmbBodyId）
        imessageClient.Verify(
            c => c.SendTextMessageWithOutcomeAsync(
                "urn:mbid:dest-3", "hi", IMessageIdempotency.DeriveAmbMessageId(log.Id, log.ConversationId)),
            Times.Once);
        chatSender.Verify(
            s => s.SendByChannelAsync(It.IsAny<SourceType>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    // ============================================================
    // helpers
    // ============================================================

    private static IMessageOptions AmbOptions() => new()
    {
        Mode = IMessageModes.AmbMsp,
        Enabled = true,
        ApiBaseUrl = "https://msp.example.invalid/v1",
        BusinessIdentifier = "urn:biz:1111",
        MspId = "msp-1111",
        MspSharedSecret = "msp-shared-secret",
    };

    private static (IMessageController controller, Mock<IIMessageIngestionService> ingestion, Mock<IIMessageSettingsProvider> provider)
        CreateController(IMessageOptions options)
    {
        var imessageService = new Mock<IIMessageService>();
        var ingestion = new Mock<IIMessageIngestionService>();
        ingestion
            .Setup(i => i.ProcessWebhookPayloadAsync(It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new IMessageIngestionResult());
        ingestion
            .Setup(i => i.ProcessAmbInboundAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new IMessageIngestionResult());

        var provider = new Mock<IIMessageSettingsProvider>();
        provider.Setup(p => p.GetCurrentAsync()).ReturnsAsync(options);
        provider.SetupGet(p => p.Current).Returns(options);

        var accessor = new Mock<ICurrentUserAccessor>();

        var controller = new IMessageController(
            imessageService.Object,
            ingestion.Object,
            provider.Object,
            accessor.Object,
            NullLogger<IMessageController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        return (controller, ingestion, provider);
    }

    private static void SetBody(IMessageController controller, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        controller.Request.Body = new MemoryStream(bytes);
        controller.Request.ContentLength = bytes.Length;
        controller.Request.ContentType = "application/json";
    }

    private static IMessageClient CreateAmbClient(CapturingHandler handler)
    {
        var options = AmbOptions();
        var provider = new Mock<IIMessageSettingsProvider>();
        provider.Setup(p => p.GetCurrentAsync()).ReturnsAsync(options);
        provider.SetupGet(p => p.Current).Returns(options);

        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        return new IMessageClient(
            httpClient,
            Options.Create(new HttpClientOptions { StandardTimeoutSeconds = 10 }),
            provider.Object,
            new IMessageRateLimiter(),
            NullLogger<IMessageClient>.Instance);
    }

    /// <summary>从 AMB 出站请求体里取出 body.id。</summary>
    private static string ReadAmbId(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>记录出站请求体/URI 的假 HttpMessageHandler（不访问网络）。</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = string.Empty;
        public string? RequestBody { get; private set; }
        public Uri? RequestUri { get; private set; }

        /// <summary>按顺序保存每次请求体（重试场景需要比对多次尝试）。</summary>
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            if (RequestBody is not null) RequestBodies.Add(RequestBody);

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
