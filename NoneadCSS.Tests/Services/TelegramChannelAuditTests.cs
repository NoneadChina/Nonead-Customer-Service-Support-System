using System.Linq.Expressions;
using System.Net;
using System.Text;
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
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Services.Telegram;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// Telegram 渠道审计修复的回归测试（OOM 下载 / 队列发送绕过 / 轮询游标丢失 / 论坛话题）。
/// 全程无网络：ITelegramClient 用 Moq 替身；需要验证真实报文的用例用本地 HttpMessageHandler 桩。
/// </summary>
public class TelegramChannelAuditTests
{
    private const string ChatId = "5551234567890";
    private const string BotToken = "123456:AAHsupersecrettoken";
    private const long FakeNowMs = 1_763_100_000_000;

    // ---------------- FIX 3：轮询游标只在处理成功/明确跳过后推进 ----------------

    [Fact]
    public async Task PollOnce_WhenProcessingThrows_DoesNotPersistOffset()
    {
        var deps = CreateIngestionDeps();
        deps.Settings.SetupGet(p => p.Current).Returns(PollingOptions());
        deps.SystemOptions.Setup(r => r.GetValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        deps.Client.Setup(c => c.GetUpdatesAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramUpdatesPage
            {
                Ok = true,
                Updates =
                [
                    new TelegramWebhookMessage
                    {
                        UpdateId = 100,
                        UpdateKind = "message",
                        MessageType = "text",
                        ChatId = 5551234567890,
                        MessageId = 9,
                        ChatType = "private",
                        TextBody = "hello"
                    }
                ]
            });

        // 拿到分布式锁的位置抛异常，等价于“这条 Update 处理过程中失败”
        deps.Lock.Setup(l => l.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("锁服务不可用"));

        var result = await deps.Service.PollOnceAsync();

        // 游标没有前进，也没有被持久化：下一轮 getUpdates 还能拿到 update_id=100
        result.NewOffset.Should().Be(0);
        result.ProcessedCount.Should().Be(0);
        deps.SystemOptions.Verify(r => r.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        deps.UnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task PollOnce_WhenUpdateSkippedAsNonMessage_AdvancesOffset()
    {
        var deps = CreateIngestionDeps();
        deps.Settings.SetupGet(p => p.Current).Returns(PollingOptions());
        deps.SystemOptions.Setup(r => r.GetValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        deps.UnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        deps.Client.Setup(c => c.GetUpdatesAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TelegramUpdatesPage
            {
                Ok = true,
                Updates =
                [
                    // 非消息类 Update 是“明确跳过”，游标必须推进，否则游标会永远卡在这一条
                    new TelegramWebhookMessage { UpdateId = 100, UpdateKind = "my_chat_member" }
                ]
            });

        var result = await deps.Service.PollOnceAsync();

        result.NewOffset.Should().Be(101);
        deps.SystemOptions.Verify(r => r.SetAsync("telegram.polling_offset_cursor", "101", It.IsAny<string?>()), Times.Once);
        deps.UnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    // ---------------- FIX 2：队列/工作流出站走与坐席回复同一条发送链路 ----------------

    [Fact]
    public async Task SendByChannelAsync_Telegram_LongText_SplitsIntoMessageSizeParts()
    {
        var text = new string('a', 9000);
        var sentParts = new List<string>();
        var client = new Mock<ITelegramClient>();
        client.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, CancellationToken, int?, string?>((_, part, _, _, _, _) => sentParts.Add(part))
            .ReturnsAsync(new TelegramSendResult { Success = true, MessageId = "m1" });

        var sender = CreateSender(client.Object, TelegramOptionsForSend(), new TelegramThrottle(1000, 1000, () => FakeNowMs));

        var result = await sender.SendByChannelAsync(SourceType.Telegram, ChatId, 5, text);

        // 修复前直连 ITelegramClient，9000 字会整条发出去（被 Telegram 以 400 拒绝）；
        // 现在必须复用坐席回复的分片逻辑：4096 一片，共 3 片，拼起来不丢字符。
        sentParts.Should().HaveCount(3);
        sentParts.Should().OnlyContain(p => p.Length <= 4096);
        string.Concat(sentParts).Should().Be(text);
        result.Success.Should().BeTrue();
        result.ExternalMessageId.Should().Be("m1");
    }

    [Fact]
    public async Task SendByChannelAsync_Telegram_403_MarksConversationUnreachable()
    {
        var client = new Mock<ITelegramClient>();
        client.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync(new TelegramSendResult
            {
                Success = false,
                ErrorCode = 403,
                Blocked = true,
                Description = "Forbidden: bot was blocked by the user",
                ErrorMessage = "Forbidden: bot was blocked by the user"
            });

        var addedThreads = new List<ConversationThread>();
        var threadRepo = new Mock<IThreadRepository>();
        threadRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync(Enumerable.Empty<ConversationThread>());
        threadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .Callback<ConversationThread>(addedThreads.Add)
            .ReturnsAsync((ConversationThread t) => t);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Threads).Returns(threadRepo.Object);
        unitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        var sender = CreateSender(client.Object, TelegramOptionsForSend(), new TelegramThrottle(1000, 1000, () => FakeNowMs), unitOfWork);

        var result = await sender.SendByChannelAsync(SourceType.Telegram, ChatId, 42, "hi");

        // 修复前队列分支只拼错误文本、不标记不可达，队列会对着已拉黑的客户无限重试
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不可达");
        addedThreads.Should().ContainSingle();
        addedThreads[0].MessageId.Should().Be(TelegramUnreachableState.MarkMessageId(42));
        unitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        threadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Once);
    }

    [Fact]
    public async Task SendByChannelAsync_Telegram_LocallyThrottled_ReturnsRetryAfterWithoutCallingApi()
    {
        // 把该 chat 登记成“退避 30 秒”，超过 MaxLocalBackoffSeconds=10 的本地等待上限
        var throttle = new TelegramThrottle(1000, 1000, () => FakeNowMs);
        throttle.RegisterRetryAfter(long.Parse(ChatId), 30, chatScoped: true);

        var client = new Mock<ITelegramClient>();

        var sender = CreateSender(client.Object, TelegramOptionsForSend(), throttle);

        var result = await sender.SendByChannelAsync(SourceType.Telegram, ChatId, 5, "hello");

        // 修复前队列分支完全不看 TelegramThrottle：即使已被 429/420 退避，也会继续打 API 把限流放大。
        // 现在与坐席回复共用同一条链路：等待时长超过本地上限即直接失败，交由队列延后重试。
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("速率限制");
        result.ErrorMessage.Should().Contain("30 秒");
        client.Verify(
            c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()),
            Times.Never);
    }

    // ---------------- FIX 4：论坛话题 message_thread_id 只在拿到正整数时出现 ----------------

    [Fact]
    public async Task SendTextMessageAsync_ForumTopic_EmitsMessageThreadIdOnlyWhenPositive()
    {
        var handler = new StubHttpMessageHandler(_ =>
            JsonResponse("{\"ok\":true,\"result\":{\"message_id\":77}}"));
        var client = CreateTelegramClient(handler);

        await client.SendTextMessageAsync(ChatId, "a", messageThreadId: 42);
        await client.SendTextMessageAsync(ChatId, "b", messageThreadId: null);
        await client.SendTextMessageAsync(ChatId, "c", messageThreadId: 0);

        handler.RequestBodies[0].Should().Contain("\"message_thread_id\":42");
        // 拿不到话题号 / 0 时不能写字段：0 不是合法话题号，Telegram 会直接 400
        handler.RequestBodies[1].Should().NotContain("message_thread_id");
        handler.RequestBodies[2].Should().NotContain("message_thread_id");
    }

    // ---------------- FIX 1：下载前先看大小信号，超限不落内存也不落盘 ----------------

    [Fact]
    public async Task DownloadFileToTempAsync_WhenGetFileReportsTooLarge_AbortsBeforeDownloading()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            "{\"ok\":true,\"result\":{\"file_id\":\"f1\",\"file_unique_id\":\"u1\",\"file_size\":5000,\"file_path\":\"documents/big.bin\"}}"));
        var client = CreateTelegramClient(handler);

        var result = await client.DownloadFileToTempAsync("f1", maxBytes: 1024);

        result.Status.Should().Be(TelegramFileDownloadStatus.TooLarge);
        result.ByteCount.Should().Be(5000);
        result.LocalPath.Should().BeNull();
        // 关键：只发了 getFile 这一次请求，没有去拿文件本体
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task DownloadFileToTempAsync_WhenWithinLimit_StreamsToTempFile()
    {
        var payload = Encoding.UTF8.GetBytes("hello telegram");
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/getFile", StringComparison.Ordinal))
            {
                return JsonResponse(
                    "{\"ok\":true,\"result\":{\"file_id\":\"f1\",\"file_unique_id\":\"u1\",\"file_size\":14,\"file_path\":\"documents/small.txt\"}}");
            }

            var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var client = CreateTelegramClient(handler);

        var result = await client.DownloadFileToTempAsync("f1", maxBytes: 1024);

        result.Status.Should().Be(TelegramFileDownloadStatus.Success);
        result.ByteCount.Should().Be(payload.Length);
        result.MimeType.Should().Be("text/plain");
        result.LocalPath.Should().NotBeNullOrEmpty();
        File.Exists(result.LocalPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(result.LocalPath!)).Should().Equal(payload);

        File.Delete(result.LocalPath!);
    }

    // ---------------- 构造辅助 ----------------

    private static TelegramOptions PollingOptions() => new()
    {
        Enabled = true,
        Mode = TelegramOptions.ModePolling,
        BotToken = BotToken,
        PollingLimit = 100,
        PollingTimeout = 25
    };

    private static TelegramOptions TelegramOptionsForSend() => new()
    {
        Enabled = true,
        Mode = TelegramOptions.ModePolling,
        BotToken = BotToken,
        MaxMessageChars = 4096,
        RateLimitGlobalPerSeconds = 1000,
        RateLimitPerChatPerSeconds = 1000,
        MaxLocalBackoffSeconds = 10
    };

    private sealed record IngestionDeps(
        TelegramIngestionService Service,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<ISystemOptionRepository> SystemOptions,
        Mock<ITelegramSettingsProvider> Settings,
        Mock<ITelegramClient> Client,
        Mock<IDistributedLockService> Lock);

    private static IngestionDeps CreateIngestionDeps()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var systemOptions = new Mock<ISystemOptionRepository>();
        var settings = new Mock<ITelegramSettingsProvider>();
        var client = new Mock<ITelegramClient>();
        var lockService = new Mock<IDistributedLockService>();

        unitOfWork.Setup(u => u.SystemOptions).Returns(systemOptions.Object);

        var service = new TelegramIngestionService(
            unitOfWork.Object,
            new Mock<IWorkflowService>().Object,
            settings.Object,
            client.Object,
            new Mock<INotificationPublisher>().Object,
            lockService.Object,
            new Mock<ILogger<TelegramIngestionService>>().Object,
            new Mock<IConfiguration>().Object,
            new Mock<IHostEnvironment>().Object,
            new Mock<IWebhookService>().Object);

        return new IngestionDeps(service, unitOfWork, systemOptions, settings, client, lockService);
    }

    private static ChatChannelMessageSender CreateSender(
        ITelegramClient client,
        TelegramOptions telegramOptions,
        TelegramThrottle throttle,
        Mock<IUnitOfWork>? unitOfWork = null)
    {
        var settings = new Mock<ITelegramSettingsProvider>();
        settings.Setup(p => p.GetCurrentAsync()).ReturnsAsync(telegramOptions);

        return new ChatChannelMessageSender(
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            new Mock<IWeChatClient>().Object,
            new Mock<IFeishuClient>().Object,
            new Mock<IWhatsAppClient>().Object,
            new Mock<IDingDingClient>().Object,
            new Mock<IDingDingSettingsProvider>().Object,
            new Mock<IMessengerClient>().Object,
            new Mock<IMessengerSettingsProvider>().Object,
            client,
            new Mock<ILineClient>().Object,
            new Mock<IIMessageClient>().Object,
            new Mock<ILogger<ChatChannelMessageSender>>().Object,
            settings.Object,
            throttle);
    }

    private static TelegramClient CreateTelegramClient(StubHttpMessageHandler handler)
    {
        var options = new TelegramOptions
        {
            Enabled = true,
            BotToken = BotToken,
            ApiBaseUrl = "https://api.telegram.org"
        };
        var settings = new Mock<ITelegramSettingsProvider>();
        settings.SetupGet(p => p.Current).Returns(options);
        settings.Setup(p => p.GetCurrentAsync()).ReturnsAsync(options);

        return new TelegramClient(
            new HttpClient(handler),
            Options.Create(new HttpClientOptions()),
            settings.Object,
            new Mock<ILogger<TelegramClient>>().Object);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return responder(request);
        }
    }
}
