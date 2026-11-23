using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// T000103 收件人解析（白名单 / 去重 / 畸形地址）集成测试。
///
/// 与 <see cref="WorkflowServiceTests"/> 中同类单测的区别：
/// - 使用真实 SQLite（内存连接保持打开）+ EnsureCreated 建库，
///   而非 EF InMemory Provider，验证 Workflow 实体真实持久化 →
///   ActionsJson 序列化回读 → TestAsync 收件人解析的完整链路；
/// - 邮件模板/发件队列仍走 Mock（外部边界），收件人解析逻辑跑在真实数据库栈上。
/// </summary>
public class WorkflowRecipientIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private NoneadCSSDbContext _context = null!;
    private IUnitOfWork _unitOfWork = null!;
    private WorkflowService _workflowService = null!;
    private Mock<IMailTemplateService> _mockMailTemplateService = null!;
    private Mock<IEmailQueueService> _mockEmailQueueService = null!;
    private Mock<IConfiguration> _mockConfiguration = null!;
    private Mock<ILogger<WorkflowService>> _mockLogger = null!;
    private ServiceProvider _serviceProvider = null!;
    private int _mailboxId;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        _context = new NoneadCSSDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        // 真实 SQLite 启用外键约束：工作流需关联真实存在的邮箱。
        // Mailboxes.Version 为 int 并发令牌（ValueGeneratedOnAddOrUpdate），EF 在 INSERT 时省略该值，
        // 而 EnsureCreated 的 schema 无 DEFAULT → NULL 触发 NOT NULL 约束。
        // 因此用 PRAGMA 动态读取"非空且无默认值"的列并原生 SQL 插入（显式给 Version），
        // 既绕过 EF 值生成，也不依赖硬编码列清单。
        _mailboxId = await InsertMailboxRowAsync();

        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        _mockMailTemplateService = new Mock<IMailTemplateService>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<WorkflowService>>();

        var services = new ServiceCollection();
        services.AddSingleton(_mockMailTemplateService.Object);
        services.AddSingleton(_mockEmailQueueService.Object);
        services.AddSingleton(_mockConfiguration.Object);
        _serviceProvider = services.BuildServiceProvider();

        _workflowService = new WorkflowService(
            _unitOfWork,
            _mockLogger.Object,
            new Mock<IFolderService>().Object,
            new Mock<INotificationPublisher>().Object,
            new Mock<IWorkflowActionHandlerProvider>().Object,
            _serviceProvider,
            Options.Create<BrandOptions>(new BrandOptions()));
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
        _serviceProvider?.Dispose();
    }

    /// <summary>
    /// 读取 Mailboxes 表"非空且无默认值"的列并原生 SQL 插入一行，
    /// 返回自增 Id。绕过 EF 对 int Version 的 ValueGeneratedOnAddOrUpdate 值生成。
    /// </summary>
    private async Task<int> InsertMailboxRowAsync()
    {
        var cols = new List<string>();
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Mailboxes)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                var notNull = reader.GetInt64(3) == 1;
                var hasDefault = !reader.IsDBNull(4);
                if (notNull && !hasDefault && !string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    cols.Add(name);
                }
            }
        }

        var colList = string.Join(", ", cols.Select(c => $"\"{c}\""));
        var placeholders = string.Join(", ", cols.Select((_, i) => $"$p{i}"));
        await using var insert = _connection.CreateCommand();
        insert.CommandText = $"INSERT INTO Mailboxes ({colList}) VALUES ({placeholders})";
        for (var i = 0; i < cols.Count; i++)
        {
            insert.Parameters.AddWithValue($"$p{i}", ResolveMailboxDummyValue(cols[i]));
        }
        await insert.ExecuteNonQueryAsync();

        await using var idCmd = _connection.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(await idCmd.ExecuteScalarAsync());
    }

    /// <summary>按列名生成合法的占位值（列名变更时通过 PRAGMA 动态适配，无需改这里）。</summary>
    private static object ResolveMailboxDummyValue(string column) => column switch
    {
        "Version" => 1,
        "InPort" or "OutPort" => 993,
        "FetchFrequency" => 5,
        "LastUid" => 0L,
        "InValidateCert" or "AutoReplyEnabled" or "OauthEnabled" or "IsActive" => 1L,
        "InProtocol" or "InEncryption" or "OutMethod" or "OutEncryption" => 0,
        "CreatedAt" or "UpdatedAt" => "2026-01-01T00:00:00.0000000",
        _ => "it-test" // 其余 TEXT 列统一占位
    };

    /// <summary>
    /// 通过真实 SQLite 持久化工作流（含 send_email 动作）并执行 TestAsync，
    /// 捕获 EnqueueEmailAsync 实际入队的收件人列表。
    /// </summary>
    private async Task<(WorkflowTestResponse Result, List<string> SentTo)> RunTestSendEmailAsync(
        string? extra, string? fromEmail, string subject = "test urgent message")
    {
        _mockMailTemplateService.Reset();
        _mockEmailQueueService.Reset();
        _mockMailTemplateService
            .Setup(s => s.GetBySlugAsync("test-slug"))
            .ReturnsAsync(new MailTemplateResponse
            {
                Id = 1,
                Name = "测试模板",
                Slug = "test-slug",
                IsActive = true,
                Subject = "测试主题",
                BodyHtml = "<p>正文</p>"
            });
        _mockMailTemplateService
            .Setup(s => s.RenderTemplateFullAsync(It.IsAny<int>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<HashSet<string>?>()))
            .ReturnsAsync(new RenderedTemplateResponse { Subject = "测试主题", BodyHtml = "<p>正文</p>" });

        var sentTo = new List<string>();
        _mockEmailQueueService
            .Setup(s => s.EnqueueEmailAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Callback<int, string, string, string, List<int>?, string?, string?, int?, string?, string?, int, int?, int?>(
                (_1, to, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13) => sentTo.Add(to))
            .Returns(Task.CompletedTask);

        var created = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "IT-SendEmail",
            MailboxId = _mailboxId,
            IsActive = true,
            TriggerType = "new_conversation",
            Conditions = new WorkflowConditionGroup
            {
                MatchType = "all",
                Conditions = new List<WorkflowCondition>
                {
                    new() { Field = "subject", Operator = "contains", Value = "test" }
                }
            },
            Actions = new List<WorkflowActionDto>
            {
                new() { Type = "send_email", Value = "test-slug", Extra = extra }
            }
        });

        // 集成验证点：确认动作已真实落库并可回读（ActionsJson 序列化 round-trip）
        var persisted = await _context.Workflows.FirstOrDefaultAsync(w => w.Id == created.Id);
        persisted.Should().NotBeNull();
        persisted!.ActionsJson.Should().Contain("send_email");

        var result = await _workflowService.TestAsync(created.Id, new WorkflowTestRequest
        {
            Subject = subject,
            FromEmail = fromEmail
        });
        return (result, sentTo);
    }

    [Fact]
    public async Task TestSendEmail_CustomerAndAssigneeSameSource_ShouldDedupToOne()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("customer, assignee", "agent@x.com");

        result.Matched.Should().BeTrue();
        result.ActionResults.Should().ContainSingle(r => r.Type == "send_email" && r.Success);
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "agent@x.com");
    }

    [Fact]
    public async Task TestSendEmail_DuplicateLiteralEmail_ShouldDedupToOne()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("ops@example.com, ops@example.com", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    [Fact]
    public async Task TestSendEmail_CaseInsensitiveDuplicate_ShouldDedupToOne()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("ops@example.com, OPS@EXAMPLE.COM", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    [Fact]
    public async Task TestSendEmail_MalformedAddress_ShouldFilterAndFallbackToOperatorEmail()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("a@b@example.com", "tester@x.com");

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "tester@x.com");
        var sendAction = result.ActionResults.Should().ContainSingle(r => r.Type == "send_email").Subject;
        sendAction.Success.Should().BeTrue();
        sendAction.Message.Should().Contain("已发送测试邮件到：tester@x.com");
    }

    [Fact]
    public async Task TestSendEmail_MalformedPlusValid_ShouldDropMalformedKeepValid()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("a@b@example.com, ops@example.com", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    [Fact]
    public async Task TestSendEmail_ExternalDomainFiltered_ShouldFallbackAndNote()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("external@qq.com", "tester@x.com");

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "tester@x.com");
        var sendAction = result.ActionResults.Should().ContainSingle(r => r.Type == "send_email").Subject;
        sendAction.Message.Should().Contain("external@qq.com");
        sendAction.Message.Should().Contain("不在测试白名单");
    }

    [Fact]
    public async Task TestSendEmail_CorpDomainWhitelisted_ShouldSendDirectly()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("ops@example.com", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    // ==================== 边界用例：空收件人 / 超长地址 ====================

    private static string OverlongEmail => $"{new string('a', 300)}@example.com"; // 306 字符 > RFC 5321 上限 254

    [Fact]
    public async Task TestSendEmail_EmptyFromEmailWithCustomer_ShouldFailAndNotEnqueue()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("customer", string.Empty);

        result.Matched.Should().BeTrue();
        sentTo.Should().BeEmpty();
        var sendAction = result.ActionResults.Should().ContainSingle(r => r.Type == "send_email").Subject;
        sendAction.Success.Should().BeFalse();
        sendAction.Message.Should().Contain("未解析到有效收件人");
    }

    [Fact]
    public async Task TestSendEmail_WhitespaceFromEmail_ShouldFailAndNotEnqueue()
    {
        var (result, sentTo) = await RunTestSendEmailAsync(null, "   ");

        result.Matched.Should().BeTrue();
        sentTo.Should().BeEmpty();
        result.ActionResults.Should().ContainSingle(r => r.Type == "send_email" && !r.Success);
    }

    [Fact]
    public async Task TestSendEmail_EmptyFromEmailWithAssignee_ShouldFailAndNotEnqueue()
    {
        var (result, sentTo) = await RunTestSendEmailAsync("assignee", string.Empty);

        result.Matched.Should().BeTrue();
        sentTo.Should().BeEmpty();
        var sendAction = result.ActionResults.Should().ContainSingle(r => r.Type == "send_email").Subject;
        sendAction.Success.Should().BeFalse();
        sendAction.Message.Should().Contain("未解析到有效收件人");
    }

    [Fact]
    public async Task TestSendEmail_OverlongAddress_ShouldFilterAndFallbackToOperatorEmail()
    {
        // 超长地址（>254，RFC 5321 上限）被过滤，兜底发到操作者发件人邮箱
        var (result, sentTo) = await RunTestSendEmailAsync(OverlongEmail, "tester@x.com");

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "tester@x.com");
    }

    [Fact]
    public async Task TestSendEmail_OverlongOperatorEmail_ShouldFailAndNotEnqueue()
    {
        // 操作者发件人邮箱超长 → 被拒 → 无收件人 → 报错且不发送
        var (result, sentTo) = await RunTestSendEmailAsync("customer", OverlongEmail);

        result.Matched.Should().BeTrue();
        sentTo.Should().BeEmpty();
        var sendAction = result.ActionResults.Should().ContainSingle(r => r.Type == "send_email").Subject;
        sendAction.Success.Should().BeFalse();
        sendAction.Message.Should().Contain("未解析到有效收件人");
    }

    [Fact]
    public async Task TestSendEmail_OverlongPlusValid_ShouldDropOverlongKeepValid()
    {
        // 超长地址与合法地址混排：超长被过滤，合法正常发送
        var (result, sentTo) = await RunTestSendEmailAsync($"{OverlongEmail}, ops@example.com", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    // ==================== 日志逻辑集成用例（超长 / 格式非法告警） ====================

    /// <summary>断言 WorkflowService 以 Warning 级别记录过包含指定关键字的日志。</summary>
    private void AssertLogWarningContaining(string keyword)
    {
        _mockLogger.Verify(
            m => m.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(keyword)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task TestSendEmail_OverlongOperatorEmail_ShouldLogOverlongWarning()
    {
        // 操作者发件人邮箱超长 → AddIfValidEmail 记录"超长"告警
        await RunTestSendEmailAsync("customer", OverlongEmail);

        AssertLogWarningContaining("超长");
    }

    [Fact]
    public async Task TestSendEmail_OverlongRuleAddress_ShouldLogOverlongWarning()
    {
        // 规则配置收件人超长 → TryAddWhitelisted 记录"超长"告警
        await RunTestSendEmailAsync(OverlongEmail, "tester@x.com");

        AssertLogWarningContaining("超长");
    }

    [Fact]
    public async Task TestSendEmail_InvalidFormatOperatorEmail_ShouldLogFormatWarning()
    {
        // 操作者发件人邮箱多 @ 格式非法 → AddIfValidEmail 记录"格式非法"告警
        await RunTestSendEmailAsync("customer", "a@@b.com");

        AssertLogWarningContaining("格式非法");
    }

    [Fact]
    public async Task TestSendEmail_InvalidFormatRuleAddress_ShouldLogFormatWarning()
    {
        // 规则配置收件人多 @ 格式非法 → TryAddWhitelisted 记录"格式非法"告警
        await RunTestSendEmailAsync("a@@example.com", "tester@x.com");

        AssertLogWarningContaining("格式非法");
    }

    [Fact]
    public async Task TestSendEmail_ValidRecipients_ShouldNotLogFormatOrOverlongWarnings()
    {
        // 正常合法收件人不应产生"格式非法"/"超长"告警（回归防线）
        await RunTestSendEmailAsync("ops@example.com", null);

        _mockLogger.Verify(
            m => m.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("格式非法") || state.ToString()!.Contains("超长")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
