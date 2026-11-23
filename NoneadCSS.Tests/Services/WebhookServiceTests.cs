using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Exceptions;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Domain.Services;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// WebhookService 回归测试
/// 覆盖 Webhook 的 CRUD、回收站、乐观锁、概览统计、日志查询、
/// 触发派发与 SSRF/URL 校验边界
/// </summary>
public class WebhookServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IWebhookRepository> _mockRepo;
    private readonly Mock<IWebhookLogRepository> _mockLogRepo;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<IEncryptionService> _mockEncryptionService;
    private readonly Mock<ILogger<WebhookService>> _mockLogger;
    private readonly WebhookService _service;

    public WebhookServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockRepo = new Mock<IWebhookRepository>();
        _mockLogRepo = new Mock<IWebhookLogRepository>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockEncryptionService = new Mock<IEncryptionService>();
        _mockLogger = new Mock<ILogger<WebhookService>>();

        _mockUnitOfWork.Setup(u => u.Webhooks).Returns(_mockRepo.Object);
        _mockUnitOfWork.Setup(u => u.WebhookLogs).Returns(_mockLogRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>())).ReturnsAsync(true);

        var httpOptions = Options.Create(new HttpClientOptions { WebhookTimeoutSeconds = 10 });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _service = new WebhookService(
            _mockUnitOfWork.Object,
            _mockEncryptionService.Object,
            _mockCurrentUserAccessor.Object,
            httpOptions,
            config,
            _mockLogger.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock Webhook DbSet
    /// </summary>
    private void SetupQueryable(params Webhook[] items)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(items.ToList());
        _mockRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    private static Webhook NewWebhook(int id = 1, string name = "Webhook A",
        string url = "https://8.8.8.8/hook", string method = "POST", string events = "conversation_created",
        bool isActive = true, int? mailboxId = null, byte[]? rowVersion = null)
    {
        return new Webhook
        {
            Id = id,
            Name = name,
            Url = url,
            Method = method,
            Events = events,
            IsActive = isActive,
            MailboxId = mailboxId,
            RowVersion = rowVersion,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private static WebhookLog NewLog(int id = 1, int webhookId = 1, string eventType = "conversation_created",
        bool isSuccess = true, string? errorMessage = null, int durationMs = 50)
    {
        return new WebhookLog
        {
            Id = id,
            WebhookId = webhookId,
            EventType = eventType,
            Url = "https://8.8.8.8/hook",
            Method = "POST",
            IsSuccess = isSuccess,
            ErrorMessage = errorMessage,
            DurationMs = durationMs,
            CreatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// 构建 WebhookService，可选放开 SSRF 内网/回环地址校验（Webhook:AllowPrivateUrl）。
    /// 用于覆盖真实 HTTP 发送成功路径（默认 _service 禁止内网地址，无法连接本地测试服务器）。
    /// </summary>
    private WebhookService BuildService(bool allowPrivateUrl = false)
    {
        var httpOptions = Options.Create(new HttpClientOptions { WebhookTimeoutSeconds = 10 });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Webhook:AllowPrivateUrl"] = allowPrivateUrl ? "true" : "false",
            })
            .Build();
        return new WebhookService(
            _mockUnitOfWork.Object,
            _mockEncryptionService.Object,
            _mockCurrentUserAccessor.Object,
            httpOptions,
            config,
            _mockLogger.Object);
    }

    /// <summary>
    /// 在回环地址启动一个最小 HTTP 服务器（普通 TCP Socket，无需 URL ACL 权限），
    /// 对所有请求返回指定状态码与响应体，用于覆盖 Webhook 真实发送成功/非成功路径。
    /// </summary>
    private static string StartLocalServer(int statusCode, string body, out TcpListener listener, out Task serverTask)
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var baseUrl = $"http://127.0.0.1:{port}";
        var serverListener = listener;
        serverTask = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await serverListener.AcceptTcpClientAsync();
                    _ = RespondAsync(client, statusCode, body);
                }
            }
            catch (ObjectDisposedException)
            {
                // listener 在测试结束时关闭
            }
            catch (SocketException)
            {
                // listener.Stop() 会中止挂起的 Accept，属正常关闭
            }
        });
        return baseUrl;
    }

    private static async Task RespondAsync(TcpClient client, int statusCode, string body)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            var buffer = new byte[8192];
            try
            {
                var headers = new StringBuilder();
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0) break;
                    headers.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    if (headers.ToString().Contains("\r\n\r\n")) break;
                }
            }
            catch (Exception)
            {
                return;
            }

            var reason = statusCode switch
            {
                200 => "OK",
                204 => "No Content",
                404 => "Not Found",
                500 => "Internal Server Error",
                _ => "OK",
            };
            var response = $"HTTP/1.1 {statusCode} {reason}\r\n" +
                           "Content-Type: application/json\r\n" +
                           $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                           "Connection: close\r\n\r\n" +
                           body;
            var bytes = Encoding.UTF8.GetBytes(response);
            try
            {
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();
            }
            catch (Exception)
            {
                // 客户端可能提前断开，忽略
            }
        }
    }

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        SetupQueryable();
        _mockRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Webhook?)null);

        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WhenNoMailboxAccess_ShouldReturnNull()
    {
        // Arrange
        var webhook = NewWebhook(id: 10, mailboxId: 42);
        SetupQueryable(webhook);
        _mockRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(webhook);
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.Is<int?>(m => m == 42)))
            .ReturnsAsync(false);

        // Act
        var result = await _service.GetByIdAsync(10);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WhenFound_ShouldReturnMappedDto()
    {
        // Arrange
        var rowVersion = new byte[] { 1, 2, 3, 4 };
        var webhook = NewWebhook(id: 1, name: "创建会话", events: "conversation_created,customer_created",
            mailboxId: 5, rowVersion: rowVersion);
        webhook.HeadersJson = "{\"Authorization\":\"Bearer x\"}";
        webhook.Secret = "topsecret";
        webhook.Mailbox = new Mailbox { Id = 5, Name = "主邮箱" };
        webhook.TriggerCount = 3;
        webhook.SuccessCount = 2;
        webhook.FailCount = 1;
        webhook.LastTriggeredAt = new DateTime(2025, 1, 1);
        SetupQueryable(webhook);
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(webhook);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("创建会话");
        result.Events.Should().Equal("conversation_created", "customer_created");
        result.Headers.Should().Contain("Authorization", "Bearer x");
        result.HasSecret.Should().BeTrue();
        result.MailboxId.Should().Be(5);
        result.MailboxName.Should().Be("主邮箱");
        result.TriggerCount.Should().Be(3);
        result.SuccessCount.Should().Be(2);
        result.FailCount.Should().Be(1);
        result.RowVersion.Should().Be(Convert.ToBase64String(rowVersion));
    }

    #endregion

    #region GetPagedAsync

    [Fact]
    public async Task GetPagedAsync_ShouldReturnMappedItemsAndTotal()
    {
        // Arrange
        var webhooks = new List<Webhook>
        {
            NewWebhook(id: 1, name: "Hook1", events: "conversation_created"),
            NewWebhook(id: 2, name: "Hook2", events: "customer_created"),
        };
        _mockRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null, null))
            .ReturnsAsync((webhooks, 2));

        var query = new WebhookQuery { Page = 1, PageSize = 20 };

        // Act
        var (items, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(2);
        items.Should().HaveCount(2);
        items.ElementAt(0).Name.Should().Be("Hook1");
        items.ElementAt(1).Name.Should().Be("Hook2");
    }

    [Fact]
    public async Task GetPagedAsync_WithFilters_ShouldPassFiltersToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetPagedAsync(2, 10, 5, true, "key", "conversation_created"))
            .ReturnsAsync((new List<Webhook>(), 0));

        var query = new WebhookQuery
        {
            Page = 2,
            PageSize = 10,
            MailboxId = 5,
            IsActive = true,
            Keyword = "key",
            EventType = "conversation_created"
        };

        // Act
        var (items, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
        _mockRepo.Verify(r => r.GetPagedAsync(2, 10, 5, true, "key", "conversation_created"), Times.Once);
    }

    #endregion

    #region GetOverviewAsync

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnCorrectOverview()
    {
        // Arrange
        var stats = new Dictionary<string, int>
        {
            ["total"] = 5,
            ["active"] = 3,
            ["inactive"] = 2,
            ["global"] = 1,
            ["mailboxSpecific"] = 4,
            ["totalTriggers"] = 100,
            ["totalSuccess"] = 80,
            ["totalFail"] = 20
        };
        _mockRepo.Setup(r => r.GetStatsAsync()).ReturnsAsync(stats);

        var topWebhooks = new List<Webhook> { NewWebhook(id: 1, name: "TopHook", events: "conversation_created") };
        _mockRepo.Setup(r => r.GetPagedAsync(1, 5, null, true, null, null))
            .ReturnsAsync((topWebhooks, 1));

        var recentLogs = new List<WebhookLog>
        {
            NewLog(id: 1, webhookId: 1, eventType: "conversation_created"),
            NewLog(id: 2, webhookId: 1, eventType: "customer_created", isSuccess: false, errorMessage: "boom")
        };
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 10, null, null, null))
            .ReturnsAsync((recentLogs, 2));

        var allWebhooks = new List<Webhook>
        {
            NewWebhook(id: 1, name: "TopHook", events: "conversation_created,customer_created"),
            NewWebhook(id: 2, name: "Second", events: "conversation_created"),
        };
        _mockRepo.Setup(r => r.GetAllWithMailboxAsync()).ReturnsAsync(allWebhooks);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalWebhooks.Should().Be(5);
        result.ActiveWebhooks.Should().Be(3);
        result.InactiveWebhooks.Should().Be(2);
        result.GlobalWebhooks.Should().Be(1);
        result.MailboxWebhooks.Should().Be(4);
        result.TotalTriggers.Should().Be(100);
        result.TotalSuccess.Should().Be(80);
        result.TotalFail.Should().Be(20);
        result.SuccessRate.Should().Be(80.00m);
        result.TopWebhooks.Should().HaveCount(1);
        result.TopWebhooks[0].Name.Should().Be("TopHook");
        result.RecentLogs.Should().HaveCount(2);
        result.RecentLogs[0].WebhookName.Should().Be("TopHook");
        result.RecentLogs[1].IsSuccess.Should().BeFalse();
        result.EventsCount.Should().HaveCount(2);
        result.EventsCount["conversation_created"].Should().Be(2);
        result.EventsCount["customer_created"].Should().Be(1);
    }

    [Fact]
    public async Task GetOverviewAsync_WithEmptyData_ShouldReturnZeros()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetStatsAsync())
            .ReturnsAsync(new Dictionary<string, int>());
        _mockRepo.Setup(r => r.GetPagedAsync(1, 5, null, true, null, null))
            .ReturnsAsync((new List<Webhook>(), 0));
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 10, null, null, null))
            .ReturnsAsync((new List<WebhookLog>(), 0));
        _mockRepo.Setup(r => r.GetAllWithMailboxAsync())
            .ReturnsAsync(new List<Webhook>());

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.TotalWebhooks.Should().Be(0);
        result.SuccessRate.Should().Be(0);
        result.TopWebhooks.Should().BeEmpty();
        result.RecentLogs.Should().BeEmpty();
        result.EventsCount.Should().BeEmpty();
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_WithValidRequest_ShouldCreateAndReturnDto()
    {
        // Arrange
        Webhook? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Webhook>()))
            .Callback<Webhook>(w => captured = w)
            .ReturnsAsync((Webhook w) => w);

        var request = new CreateWebhookRequest
        {
            Name = "新回调",
            Url = "https://8.8.8.8/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created", "customer_created" },
            Headers = new Dictionary<string, string> { { "Authorization", "Bearer x" } },
            Secret = "s3cret",
            IsActive = true,
            MailboxId = 5,
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("新回调");
        result.Url.Should().Be("https://8.8.8.8/hook");
        result.Events.Should().Equal("conversation_created", "customer_created");
        result.HasSecret.Should().BeTrue();
        result.IsActive.Should().BeTrue();
        result.MailboxId.Should().Be(5);

        captured.Should().NotBeNull();
        captured!.HeadersJson.Should().Contain("Authorization");
        captured.Secret.Should().Be("s3cret");
        captured.Events.Should().Be("conversation_created,customer_created");
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Webhook>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyHeaders_ShouldSetHeadersJsonNull()
    {
        // Arrange
        Webhook? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Webhook>()))
            .Callback<Webhook>(w => captured = w)
            .ReturnsAsync((Webhook w) => w);

        var request = new CreateWebhookRequest
        {
            Name = "空头",
            Url = "https://8.8.8.8/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        // Act
        await _service.CreateAsync(request);

        // Assert
        captured.Should().NotBeNull();
        captured!.HeadersJson.Should().BeNull();
        captured.Secret.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WithLoopbackUrl_ShouldThrowAndNotSave()
    {
        // Arrange
        var request = new CreateWebhookRequest
        {
            Name = "回环",
            Url = "http://localhost/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*回环地址*");
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Webhook>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithPrivateIpUrl_ShouldThrowAndNotSave()
    {
        // Arrange
        var request = new CreateWebhookRequest
        {
            Name = "内网",
            Url = "http://10.0.0.1/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*内网地址*");
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Webhook>()), Times.Never);
    }

    /// <summary>
    /// 通过 CreateAsync 校验 URL 是否符合 SSRF / 格式规则，断言抛错且不保存。
    /// </summary>
    private async Task AssertCreateRejectsUrlAsync(string? url, string expectedMessageFragment)
    {
        var request = new CreateWebhookRequest
        {
            Name = "SSRF",
            Url = url!,
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        var act = () => _service.CreateAsync(request);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{expectedMessageFragment}*");
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Webhook>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Theory]
    [InlineData(null, "URL 不能为空")]
    [InlineData("", "URL 不能为空")]
    [InlineData("   ", "URL 不能为空")]
    [InlineData("not-a-url", "URL 格式无效")]
    [InlineData("ftp://8.8.8.8/file", "URL 必须以 http:// 或 https:// 开头")]
    [InlineData("http://127.0.0.1/hook", "URL 不能指向本地回环地址")]
    [InlineData("http://localhost/hook", "URL 不能指向本地回环地址")]
    [InlineData("http://10.0.0.1/hook", "URL 不能指向 10.x.x.x 内网地址")]
    [InlineData("http://172.16.0.1/hook", "URL 不能指向 172.16-31.x.x 内网地址")]
    [InlineData("http://172.31.255.1/hook", "URL 不能指向 172.16-31.x.x 内网地址")]
    [InlineData("http://192.168.1.1/hook", "URL 不能指向 192.168.x.x 内网地址")]
    [InlineData("http://169.254.169.254/hook", "URL 不能指向云元数据服务地址")]
    [InlineData("http://169.254.0.1/hook", "URL 不能指向链路本地地址")]
    [InlineData("http://0.0.0.1/hook", "URL 不能指向 0.x.x.x 地址")]
    [InlineData("http://224.0.0.1/hook", "URL 不能指向组播或保留地址")]
    [InlineData("http://240.0.0.1/hook", "URL 不能指向组播或保留地址")]
    [InlineData("http://8.8.8.8:0/hook", "URL 端口无效")]
    [InlineData("http://8.8.8.8:65536/hook", "URL 格式无效")]
    [InlineData("http://nonexistent-domain.invalid/hook", "URL 域名解析失败")]
    public async Task CreateAsync_WithInvalidUrl_ShouldThrowAndNotSave(string? url, string expectedMessageFragment)
    {
        await AssertCreateRejectsUrlAsync(url, expectedMessageFragment);
    }

    [Fact]
    public async Task CreateAsync_WithLoopbackUrl_AllowPrivate_ShouldSucceed()
    {
        // Arrange
        var service = BuildService(allowPrivateUrl: true);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Webhook>()))
            .ReturnsAsync((Webhook w) => w);

        var request = new CreateWebhookRequest
        {
            Name = "内网",
            Url = "http://127.0.0.1/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        // Act
        var dto = await service.CreateAsync(request);

        // Assert
        dto.Should().NotBeNull();
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Webhook>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    /// <summary>
    /// 通过 CreateAsync 校验 URL 合法/允许，断言保存成功。
    /// </summary>
    private async Task AssertCreateAcceptsUrlAsync(string url, bool allowPrivate)
    {
        var service = BuildService(allowPrivateUrl: allowPrivate);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Webhook>()))
            .ReturnsAsync((Webhook w) => w);

        var request = new CreateWebhookRequest
        {
            Name = "验证",
            Url = url,
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        var dto = await service.CreateAsync(request);

        dto.Should().NotBeNull();
        dto.Url.Should().Be(url);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Webhook>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Theory]
    [InlineData("http://8.8.8.8/hook", false)]
    [InlineData("http://8.8.8.8:1/hook", false)]
    [InlineData("http://8.8.8.8:8080/hook", false)]
    [InlineData("http://8.8.8.8:65535/hook", false)]
    [InlineData("https://8.8.8.8/hook", false)]
    [InlineData("http://1.1.1.1/hook", false)]
    [InlineData("http://172.15.0.1/hook", false)]
    [InlineData("http://10.0.0.1/hook", true)]
    [InlineData("http://192.168.1.1/hook", true)]
    [InlineData("http://172.31.255.1/hook", true)]
    [InlineData("http://localhost/hook", true)]
    public async Task CreateAsync_WithValidOrAllowedUrl_ShouldSucceed(string url, bool allowPrivate)
    {
        await AssertCreateAcceptsUrlAsync(url, allowPrivate);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        SetupQueryable();
        var request = new UpdateWebhookRequest
        {
            Id = 999,
            Name = "x",
            Url = "https://8.8.8.8/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenNoMailboxAccess_ShouldReturnNull()
    {
        // Arrange
        var webhook = NewWebhook(id: 10, mailboxId: 42);
        SetupQueryable(webhook);
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.Is<int?>(m => m == 42)))
            .ReturnsAsync(false);

        var request = new UpdateWebhookRequest
        {
            Id = 10,
            Name = "x",
            Url = "https://8.8.8.8/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithConflictingIfMatch_ShouldThrowConcurrencyException()
    {
        // Arrange
        var webhook = NewWebhook(id: 1, rowVersion: new byte[] { 1, 2, 3 });
        SetupQueryable(webhook);

        var request = new UpdateWebhookRequest
        {
            Id = 1,
            Name = "x",
            Url = "https://8.8.8.8/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created" },
            IfMatch = Convert.ToBase64String(new byte[] { 9, 9, 9 }),
        };

        // Act
        var act = () => _service.UpdateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ConcurrencyException>();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_ShouldUpdateAndReturnDto()
    {
        // Arrange
        var webhook = NewWebhook(id: 1, name: "旧名", events: "conversation_created", mailboxId: 5);
        SetupQueryable(webhook);

        var request = new UpdateWebhookRequest
        {
            Id = 1,
            Name = "新名",
            Url = "https://8.8.8.8/hook",
            Method = "PUT",
            Events = new List<string> { "customer_created" },
            Headers = new Dictionary<string, string> { { "X-Custom", "v" } },
            Secret = "updated",
            IsActive = false,
            MailboxId = 5,
        };

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("新名");
        result.Method.Should().Be("PUT");
        result.Events.Should().Equal("customer_created");
        result.Headers.Should().Contain("X-Custom", "v");
        result.HasSecret.Should().BeTrue();
        result.IsActive.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithInvalidUrl_ShouldThrowAndNotSave()
    {
        // Arrange
        var webhook = NewWebhook(id: 1, url: "https://8.8.8.8/hook");
        SetupQueryable(webhook);

        var request = new UpdateWebhookRequest
        {
            Id = 1,
            Name = "x",
            Url = "http://localhost/hook",
            Method = "POST",
            Events = new List<string> { "conversation_created" },
        };

        // Act
        var act = () => _service.UpdateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*回环地址*");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ShouldReturnFalse()
    {
        // Arrange
        SetupQueryable();

        // Act
        var result = await _service.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenNoMailboxAccess_ShouldReturnFalse()
    {
        // Arrange
        var webhook = NewWebhook(id: 10, mailboxId: 42);
        SetupQueryable(webhook);
        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.Is<int?>(m => m == 42)))
            .ReturnsAsync(false);

        // Act
        var result = await _service.DeleteAsync(10);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenFound_ShouldSoftDeleteAndReturnTrue()
    {
        // Arrange
        var webhook = NewWebhook(id: 1);
        SetupQueryable(webhook);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        webhook.DeletedAt.Should().NotBeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region Trash

    [Fact]
    public async Task GetTrashPagedAsync_ShouldReturnMappedItemsAndTotal()
    {
        // Arrange
        var deleted = new List<Webhook>
        {
            NewWebhook(id: 1, name: "已删Hook"),
        };
        deleted[0].DeletedAt = DateTime.UtcNow;
        _mockRepo.Setup(r => r.GetDeletedPagedAsync(1, 20, "key"))
            .ReturnsAsync((deleted, 1));

        // Act
        var (items, total) = await _service.GetTrashPagedAsync(1, 20, "key");

        // Assert
        total.Should().Be(1);
        items.Should().HaveCount(1);
        items[0].Name.Should().Be("已删Hook");
    }

    [Fact]
    public async Task RestoreAsync_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetDeletedByIdAsync(999)).ReturnsAsync((Webhook?)null);

        // Act
        var result = await _service.RestoreAsync(999);

        // Assert
        result.Should().BeNull();
        _mockRepo.Verify(r => r.RestoreAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_WhenFound_ShouldRestoreAndReturnDto()
    {
        // Arrange
        var webhook = NewWebhook(id: 1, name: "恢复Hook");
        SetupQueryable(webhook);
        _mockRepo.Setup(r => r.GetDeletedByIdAsync(1)).ReturnsAsync(webhook);
        _mockRepo.Setup(r => r.RestoreAsync(1)).Returns(Task.CompletedTask);
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(webhook);

        // Act
        var result = await _service.RestoreAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("恢复Hook");
        _mockRepo.Verify(r => r.RestoreAsync(1), Times.Once);
    }

    [Fact]
    public async Task HardDeleteAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAsync(1)).ReturnsAsync(true);

        // Act
        var result = await _service.HardDeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        _mockRepo.Verify(r => r.HardDeleteAsync(1), Times.Once);
    }

    [Fact]
    public async Task ClearTrashAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAllDeletedAsync()).ReturnsAsync(3);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(3);
        _mockRepo.Verify(r => r.HardDeleteAllDeletedAsync(), Times.Once);
    }

    #endregion

    #region ToggleActiveAsync

    [Fact]
    public async Task ToggleActiveAsync_WhenNotFound_ShouldReturnFalse()
    {
        // Arrange
        SetupQueryable();

        // Act
        var result = await _service.ToggleActiveAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ToggleActiveAsync_ShouldToggleStateAndSave()
    {
        // Arrange
        var webhook = NewWebhook(id: 1, isActive: true);
        SetupQueryable(webhook);

        // Act
        var result = await _service.ToggleActiveAsync(1);

        // Assert
        result.Should().BeTrue();
        webhook.IsActive.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ToggleActiveAsync_WithConflictingIfMatch_ShouldThrow()
    {
        // Arrange
        var webhook = NewWebhook(id: 1, isActive: true, rowVersion: new byte[] { 1, 2, 3 });
        SetupQueryable(webhook);

        // Act
        var act = () => _service.ToggleActiveAsync(1, Convert.ToBase64String(new byte[] { 9, 9 }));

        // Assert
        await act.Should().ThrowAsync<ConcurrencyException>();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region TestAsync

    [Fact]
    public async Task TestAsync_WhenWebhookNotFound_ShouldReturnFailure()
    {
        // Arrange
        SetupQueryable();

        // Act
        var result = await _service.TestAsync(999, new TestWebhookRequest());

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Webhook 不存在");
        _mockLogRepo.Verify(r => r.AddAsync(It.IsAny<WebhookLog>()), Times.Never);
    }

    [Fact]
    public async Task TestAsync_WithInvalidUrl_ShouldReturnFailureAndLog()
    {
        // Arrange
        var webhook = NewWebhook(id: 1, url: "http://localhost/hook");
        SetupQueryable(webhook);
        WebhookLog? captured = null;
        _mockLogRepo.Setup(r => r.AddAsync(It.IsAny<WebhookLog>()))
            .Callback<WebhookLog>(l => captured = l)
            .ReturnsAsync((WebhookLog l) => l);
        _mockRepo.Setup(r => r.IncrementTriggerStatsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.TestAsync(1, new TestWebhookRequest());

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("回环地址");
        captured.Should().NotBeNull();
        captured!.IsSuccess.Should().BeFalse();
        captured.ErrorMessage.Should().Contain("回环地址");
        _mockLogRepo.Verify(r => r.AddAsync(It.IsAny<WebhookLog>()), Times.Once);
    }

    [Fact]
    public async Task TestAsync_WithLocalServer200_ShouldSendRequestAndLogSuccess()
    {
        // Arrange
        var baseUrl = StartLocalServer(200, "{\"ok\":true}", out var listener, out var serverTask);
        try
        {
            var service = BuildService(allowPrivateUrl: true);
            SetupQueryable(NewWebhook(id: 1, url: $"{baseUrl}/hook"));
            WebhookLog? captured = null;
            _mockLogRepo.Setup(r => r.AddAsync(It.IsAny<WebhookLog>()))
                .Callback<WebhookLog>(l => captured = l)
                .ReturnsAsync((WebhookLog l) => l);
            _mockRepo.Setup(r => r.IncrementTriggerStatsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await service.TestAsync(1, new TestWebhookRequest());

            // Assert
            result.Success.Should().BeTrue();
            result.StatusCode.Should().Be(200);
            result.Message.Should().Be("发送成功");
            result.ResponseBody.Should().Be("{\"ok\":true}");
            captured.Should().NotBeNull();
            captured!.IsSuccess.Should().BeTrue();
            captured.ResponseStatusCode.Should().Be(200);
            captured.EventType.Should().Be("conversation_created");
            _mockLogRepo.Verify(r => r.AddAsync(It.IsAny<WebhookLog>()), Times.Once);
            _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    [Fact]
    public async Task TestAsync_WithLocalServer404_ShouldReturnFailureAndLog()
    {
        // Arrange
        var baseUrl = StartLocalServer(404, "{\"error\":\"not found\"}", out var listener, out var serverTask);
        try
        {
            var service = BuildService(allowPrivateUrl: true);
            SetupQueryable(NewWebhook(id: 1, url: $"{baseUrl}/hook"));
            WebhookLog? captured = null;
            _mockLogRepo.Setup(r => r.AddAsync(It.IsAny<WebhookLog>()))
                .Callback<WebhookLog>(l => captured = l)
                .ReturnsAsync((WebhookLog l) => l);
            _mockRepo.Setup(r => r.IncrementTriggerStatsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await service.TestAsync(1, new TestWebhookRequest());

            // Assert
            result.Success.Should().BeFalse();
            result.StatusCode.Should().Be(404);
            result.Message.Should().Be("HTTP 404");
            captured.Should().NotBeNull();
            captured!.IsSuccess.Should().BeFalse();
            captured.ResponseStatusCode.Should().Be(404);
            _mockLogRepo.Verify(r => r.AddAsync(It.IsAny<WebhookLog>()), Times.Once);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    [Fact]
    public async Task TestAsync_WithLocalServer204_ShouldReturnSuccessNoContent()
    {
        // Arrange
        var baseUrl = StartLocalServer(204, "", out var listener, out var serverTask);
        try
        {
            var service = BuildService(allowPrivateUrl: true);
            SetupQueryable(NewWebhook(id: 1, url: $"{baseUrl}/hook"));
            WebhookLog? captured = null;
            _mockLogRepo.Setup(r => r.AddAsync(It.IsAny<WebhookLog>()))
                .Callback<WebhookLog>(l => captured = l)
                .ReturnsAsync((WebhookLog l) => l);
            _mockRepo.Setup(r => r.IncrementTriggerStatsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await service.TestAsync(1, new TestWebhookRequest());

            // Assert
            result.Success.Should().BeTrue();
            result.StatusCode.Should().Be(204);
            result.Message.Should().Be("发送成功");
            result.ResponseBody.Should().BeEmpty();
            captured.Should().NotBeNull();
            captured!.IsSuccess.Should().BeTrue();
            captured.ResponseStatusCode.Should().Be(204);
            _mockLogRepo.Verify(r => r.AddAsync(It.IsAny<WebhookLog>()), Times.Once);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    #endregion

    #region GetLogsAsync

    [Fact]
    public async Task GetLogsAsync_ShouldReturnMappedLogsWithWebhookName()
    {
        // Arrange
        var logs = new List<WebhookLog>
        {
            NewLog(id: 1, webhookId: 1, eventType: "conversation_created"),
            NewLog(id: 2, webhookId: 1, eventType: "customer_created", isSuccess: false, errorMessage: "err"),
        };
        var webhook = NewWebhook(id: 1, name: "日志Hook");
        _mockLogRepo.Setup(r => r.GetByWebhookAsync(1, 50)).ReturnsAsync(logs);
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(webhook);

        // Act
        var result = await _service.GetLogsAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[0].WebhookName.Should().Be("日志Hook");
        result[0].EventType.Should().Be("conversation_created");
        result[1].IsSuccess.Should().BeFalse();
        result[1].ErrorMessage.Should().Be("err");
    }

    #endregion

    #region GetLogsPagedAsync

    [Fact]
    public async Task GetLogsPagedAsync_ShouldReturnMappedLogsAndResolveNames()
    {
        // Arrange
        var logs = new List<WebhookLog>
        {
            NewLog(id: 1, webhookId: 10),
            NewLog(id: 2, webhookId: 20),
        };
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 10, null, "conversation_created", true))
            .ReturnsAsync((logs, 2));

        SetupQueryable(
            NewWebhook(id: 10, name: "Webhook10"),
            NewWebhook(id: 20, name: "Webhook20"));

        var query = new WebhookLogQuery
        {
            Page = 1,
            PageSize = 10,
            EventType = "conversation_created",
            IsSuccess = true,
        };

        // Act
        var (items, total) = await _service.GetLogsPagedAsync(query);

        // Assert
        total.Should().Be(2);
        items.Should().HaveCount(2);
        items[0].WebhookName.Should().Be("Webhook10");
        items[1].WebhookName.Should().Be("Webhook20");
    }

    [Fact]
    public async Task GetLogsPagedAsync_WhenNoIds_ShouldNotQueryWebhookNames()
    {
        // Arrange
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 10, null, null, null))
            .ReturnsAsync((new List<WebhookLog>(), 0));

        var query = new WebhookLogQuery { Page = 1, PageSize = 10 };

        // Act
        var (items, total) = await _service.GetLogsPagedAsync(query);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
        _mockRepo.Verify(r => r.GetQueryable(It.IsAny<bool>()), Times.Never);
    }

    #endregion

    #region TriggerAsync

    [Fact]
    public async Task TriggerAsync_WithEmptyEventType_ShouldReturnZero()
    {
        // Act
        var result = await _service.TriggerAsync("  ");

        // Assert
        result.Should().Be(0);
        _mockRepo.Verify(r => r.GetActiveForEventAsync(It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task TriggerAsync_WhenNoWebhooksMatch_ShouldReturnZero()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetActiveForEventAsync("conversation_created", 3))
            .ReturnsAsync(new List<Webhook>());

        // Act
        var result = await _service.TriggerAsync("conversation_created", 3);

        // Assert
        result.Should().Be(0);
        _mockLogRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<WebhookLog>>()), Times.Never);
    }

    [Fact]
    public async Task TriggerAsync_WithInvalidUrlWebhooks_ShouldRecordFailureLogsAndReturnZero()
    {
        // Arrange
        var webhooks = new List<Webhook>
        {
            NewWebhook(id: 1, url: "http://localhost/hook", events: "conversation_created"),
            NewWebhook(id: 2, url: "http://localhost/hook", events: "conversation_created"),
        };
        _mockRepo.Setup(r => r.GetActiveForEventAsync("conversation_created", 3))
            .ReturnsAsync(webhooks);

        IEnumerable<WebhookLog>? captured = null;
        _mockLogRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<WebhookLog>>()))
            .Callback<IEnumerable<WebhookLog>>(l => captured = l)
            .Returns(Task.CompletedTask);
        _mockRepo.Setup(r => r.IncrementTriggerStatsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.TriggerAsync("conversation_created", 3);

        // Assert
        result.Should().Be(0);
        captured.Should().NotBeNull();
        captured!.Should().HaveCount(2);
        captured!.All(l => !l.IsSuccess).Should().BeTrue();
        _mockLogRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<WebhookLog>>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task TriggerAsync_WithLocalServer200_ShouldSendAndReturnTriggeredCount()
    {
        // Arrange
        var baseUrl = StartLocalServer(200, "{\"ok\":true}", out var listener, out var serverTask);
        try
        {
            var service = BuildService(allowPrivateUrl: true);
            var webhooks = new List<Webhook>
            {
                NewWebhook(id: 1, url: $"{baseUrl}/hook1", events: "conversation_created"),
                NewWebhook(id: 2, url: $"{baseUrl}/hook2", events: "conversation_created"),
            };
            _mockRepo.Setup(r => r.GetActiveForEventAsync("conversation_created", 3))
                .ReturnsAsync(webhooks);

            IEnumerable<WebhookLog>? captured = null;
            _mockLogRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<WebhookLog>>()))
                .Callback<IEnumerable<WebhookLog>>(l => captured = l)
                .Returns(Task.CompletedTask);
            _mockRepo.Setup(r => r.IncrementTriggerStatsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await service.TriggerAsync("conversation_created", 3);

            // Assert
            result.Should().Be(2);
            captured.Should().NotBeNull();
            captured!.Should().HaveCount(2);
            captured!.All(l => l.IsSuccess).Should().BeTrue();
            captured!.All(l => l.ResponseStatusCode == 200).Should().BeTrue();
            _mockLogRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<WebhookLog>>()), Times.Once);
            _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    [Fact]
    public async Task TriggerAsync_WithLocalServer404_ShouldLogFailureAndReturnZero()
    {
        // Arrange
        var baseUrl = StartLocalServer(404, "{\"error\":\"not found\"}", out var listener, out var serverTask);
        try
        {
            var service = BuildService(allowPrivateUrl: true);
            var webhooks = new List<Webhook>
            {
                NewWebhook(id: 1, url: $"{baseUrl}/hook", events: "conversation_created"),
            };
            _mockRepo.Setup(r => r.GetActiveForEventAsync("conversation_created", 3))
                .ReturnsAsync(webhooks);

            IEnumerable<WebhookLog>? captured = null;
            _mockLogRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<WebhookLog>>()))
                .Callback<IEnumerable<WebhookLog>>(l => captured = l)
                .Returns(Task.CompletedTask);
            _mockRepo.Setup(r => r.IncrementTriggerStatsAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await service.TriggerAsync("conversation_created", 3);

            // Assert
            result.Should().Be(0);
            captured.Should().NotBeNull();
            captured!.Should().HaveCount(1);
            captured!.Single().IsSuccess.Should().BeFalse();
            captured!.Single().ResponseStatusCode.Should().Be(404);
            _mockLogRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<WebhookLog>>()), Times.Once);
            _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    #endregion
}
