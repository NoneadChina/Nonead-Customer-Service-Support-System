using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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

public class WorkflowServiceTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private IUnitOfWork _unitOfWork = null!;
    private Mock<ILogger<WorkflowService>> _mockLogger = null!;
    private Mock<IMailTemplateService> _mockMailTemplateService = null!;
    private Mock<IEmailQueueService> _mockEmailQueueService = null!;
    private Mock<IConfiguration> _mockConfiguration = null!;
    private Mock<IFolderService> _mockFolderService = null!;
    private Mock<NoneadCSS.Domain.Events.INotificationPublisher> _mockNotificationPublisher = null!;
    private Mock<IWebhookService> _mockWebhookService = null!;
    private Mock<IWorkflowActionHandlerProvider> _mockHandlerProvider = null!;
    private WorkflowService _workflowService = null!;
    private ServiceProvider _serviceProvider = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"WorkflowTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new NoneadCSSDbContext(options);
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        _mockLogger = new Mock<ILogger<WorkflowService>>();
        _mockMailTemplateService = new Mock<IMailTemplateService>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockFolderService = new Mock<IFolderService>();
        _mockNotificationPublisher = new Mock<NoneadCSS.Domain.Events.INotificationPublisher>();
        _mockWebhookService = new Mock<IWebhookService>();
        _mockHandlerProvider = new Mock<IWorkflowActionHandlerProvider>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(_mockMailTemplateService.Object);
        serviceCollection.AddSingleton(_mockEmailQueueService.Object);
        serviceCollection.AddSingleton(_mockConfiguration.Object);
        serviceCollection.AddSingleton(_mockWebhookService.Object);
        _serviceProvider = serviceCollection.BuildServiceProvider();

        _mockConfiguration.Setup(c => c["CompanyName"]).Returns("NoneadCSS");

        _workflowService = new WorkflowService(
            _unitOfWork,
            _mockLogger.Object,
            _mockFolderService.Object,
            _mockNotificationPublisher.Object,
            _mockHandlerProvider.Object,
            _serviceProvider,
            Options.Create<BrandOptions>(new BrandOptions()));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        _serviceProvider?.Dispose();
    }

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WithValidRequest_ShouldCreateWorkflow()
    {
        var request = new CreateWorkflowRequest
        {
            Name = "Test Workflow",
            Description = "Test Description",
            MailboxId = 1,
            IsActive = true,
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>
            {
                new() { Type = "set_status", Value = "2" }
            }
        };

        var result = await _workflowService.CreateAsync(request);

        result.Should().NotBeNull();
        result.Name.Should().Be("Test Workflow");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WithZeroMailboxId_ShouldSetMailboxIdToNull()
    {
        var request = new CreateWorkflowRequest
        {
            Name = "Null Mailbox Test",
            MailboxId = 0,
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        };

        var result = await _workflowService.CreateAsync(request);

        result.Should().NotBeNull();
        result.MailboxId.Should().BeNull();
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_WithExistingId_ShouldReturnWorkflow()
    {
        var request = new CreateWorkflowRequest
        {
            Name = "GetById Test",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        };

        var created = await _workflowService.CreateAsync(request);

        var result = await _workflowService.GetByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("GetById Test");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistingId_ShouldReturnNull()
    {
        var result = await _workflowService.GetByIdAsync(999);
        result.Should().BeNull();
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WithValidRequest_ShouldUpdateWorkflow()
    {
        var request = new CreateWorkflowRequest
        {
            Name = "Update Test",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        };

        var created = await _workflowService.CreateAsync(request);

        var updateRequest = new UpdateWorkflowRequest
        {
            Name = "Updated Workflow",
            Description = "Updated Description",
            IsActive = false,
            TriggerType = "new_reply",
            Actions = new List<WorkflowActionDto>()
        };

        var result = await _workflowService.UpdateAsync(created.Id, updateRequest);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Workflow");
        result.IsActive.Should().BeFalse();
        result.TriggerType.Should().Be("new_reply");
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingId_ShouldSoftDelete()
    {
        var request = new CreateWorkflowRequest
        {
            Name = "Delete Test",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        };

        var created = await _workflowService.CreateAsync(request);

        var result = await _workflowService.DeleteAsync(created.Id);

        result.Should().BeTrue();
        var deletedWorkflow = await _context.Workflows.FindAsync(created.Id);
        deletedWorkflow.Should().NotBeNull();
        deletedWorkflow!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingId_ShouldReturnFalse()
    {
        var result = await _workflowService.DeleteAsync(999);
        result.Should().BeFalse();
    }

    #endregion

    #region ToggleActiveAsync Tests

    [Fact]
    public async Task ToggleActiveAsync_WithExistingId_ShouldReturnTrue()
    {
        var request = new CreateWorkflowRequest
        {
            Name = "Toggle Test",
            IsActive = true,
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        };

        var created = await _workflowService.CreateAsync(request);

        var result = await _workflowService.ToggleActiveAsync(created.Id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleActiveAsync_WithNonExistingId_ShouldReturnFalse()
    {
        var result = await _workflowService.ToggleActiveAsync(999);
        result.Should().BeFalse();
    }

    #endregion

    #region GetOverviewAsync Tests

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnOverview()
    {
        var result = await _workflowService.GetOverviewAsync();

        result.Should().NotBeNull();
        result.TriggerOptions.Should().ContainKey("new_conversation");
    }

    #endregion

    #region GetListAsync Tests

    [Fact]
    public async Task GetListAsync_ShouldReturnPagedResults()
    {
        // Arrange
        for (var i = 1; i <= 3; i++)
        {
            await _workflowService.CreateAsync(new CreateWorkflowRequest
            {
                Name = $"Workflow {i}",
                TriggerType = "new_conversation",
                Actions = new List<WorkflowActionDto>()
            });
        }

        // Act
        var (items, total) = await _workflowService.GetListAsync(1, 10);

        // Assert
        total.Should().BeGreaterThanOrEqualTo(3);
        items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetListAsync_WithKeywordFilter_ShouldReturnFilteredResults()
    {
        // Arrange
        await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Special Keyword Workflow",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Other Workflow",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });

        // Act
        var (items, total) = await _workflowService.GetListAsync(1, 10, keyword: "Special");

        // Assert
        total.Should().BeGreaterThanOrEqualTo(1);
        items.Should().Contain(w => w.Name.Contains("Special"));
    }

    [Fact]
    public async Task GetListAsync_WithActiveFilter_ShouldReturnOnlyActiveWorkflows()
    {
        // Arrange
        await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Active Workflow",
            IsActive = true,
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        var inactive = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Inactive Workflow",
            IsActive = false,
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });

        // Act
        var (items, total) = await _workflowService.GetListAsync(1, 10, isActive: true);

        // Assert
        items.Should().NotContain(w => w.Id == inactive.Id);
    }

    #endregion

    #region RestoreAsync Tests

    [Fact]
    public async Task RestoreAsync_WithDeletedWorkflow_ShouldRestoreSuccessfully()
    {
        // Arrange
        var created = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "To Restore",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        await _workflowService.DeleteAsync(created.Id);

        // Act
        var result = await _workflowService.RestoreAsync(created.Id);

        // Assert
        result.Should().BeTrue();
        var restored = await _context.Workflows.FindAsync(created.Id);
        restored.Should().NotBeNull();
        restored!.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task RestoreAsync_WithNonExistingId_ShouldReturnFalse()
    {
        // Act
        var result = await _workflowService.RestoreAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region HardDeleteAsync Tests

    [Fact(Skip = "InMemory DB 不支持 ExecuteDeleteAsync，需使用 SQL Server/SQLite 集成测试验证")]
    public async Task HardDeleteAsync_WithDeletedWorkflow_ShouldPermanentlyDelete()
    {
        // Arrange
        var created = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "To Hard Delete",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        await _workflowService.DeleteAsync(created.Id);

        // Act
        var result = await _workflowService.HardDeleteAsync(created.Id);

        // Assert
        result.Should().BeTrue();
        var entity = await _context.Workflows.FindAsync(created.Id);
        entity.Should().BeNull();
    }

    [Fact]
    public async Task HardDeleteAsync_WithNonExistingId_ShouldReturnFalse()
    {
        // Act
        var result = await _workflowService.HardDeleteAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetDeletedListAsync Tests

    [Fact]
    public async Task GetDeletedListAsync_ShouldReturnOnlyDeletedWorkflows()
    {
        // Arrange
        var created = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Deleted Workflow",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Active Workflow",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        await _workflowService.DeleteAsync(created.Id);

        // Act
        var (items, total) = await _workflowService.GetDeletedListAsync(1, 10);

        // Assert
        total.Should().BeGreaterThanOrEqualTo(1);
        items.Should().Contain(w => w.Id == created.Id);
    }

    #endregion

    #region GetDeletedCountAsync Tests

    [Fact]
    public async Task GetDeletedCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        var w1 = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Delete Me 1",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        var w2 = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Delete Me 2",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        await _workflowService.DeleteAsync(w1.Id);
        await _workflowService.DeleteAsync(w2.Id);

        // Act
        var count = await _workflowService.GetDeletedCountAsync();

        // Assert
        count.Should().BeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region ClearTrashAsync Tests

    [Fact(Skip = "InMemory DB 不支持 ExecuteDeleteAsync，需使用 SQL Server/SQLite 集成测试验证")]
    public async Task ClearTrashAsync_ShouldRemoveAllDeletedWorkflows()
    {
        // Arrange
        var w1 = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Trash 1",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        var w2 = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Trash 2",
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>()
        });
        await _workflowService.DeleteAsync(w1.Id);
        await _workflowService.DeleteAsync(w2.Id);

        // Act
        var result = await _workflowService.ClearTrashAsync();

        // Assert
        result.Should().BeGreaterThanOrEqualTo(2);
        var (items, total) = await _workflowService.GetDeletedListAsync(1, 10);
        total.Should().Be(0);
    }

    #endregion

    #region TestAsync Tests

    [Fact]
    public async Task TestAsync_WithNonExistingWorkflow_ShouldReturnEmptyResponse()
    {
        // Act
        var result = await _workflowService.TestAsync(999, new WorkflowTestRequest());

        // Assert
        result.Should().NotBeNull();
        result.Matched.Should().BeFalse();
    }

    [Fact]
    public async Task TestAsync_WithMatchingConditions_ShouldReturnMatchedTrue()
    {
        // Arrange
        var created = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Test Workflow",
            TriggerType = "new_conversation",
            IsActive = true,
            Conditions = new WorkflowConditionGroup
            {
                MatchType = "all",
                Conditions = new List<WorkflowCondition>
                {
                    new() { Field = "subject", Operator = "contains", Value = "urgent" }
                }
            },
            Actions = new List<WorkflowActionDto>
            {
                new() { Type = "set_status", Value = "3" }
            }
        });

        var request = new WorkflowTestRequest
        {
            Subject = "This is an urgent message"
        };

        // Act
        var result = await _workflowService.TestAsync(created.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.Matched.Should().BeTrue();
        result.ActionsToExecute.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TestAsync_WithNonMatchingConditions_ShouldReturnMatchedFalse()
    {
        // Arrange
        var created = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Test Workflow",
            TriggerType = "new_conversation",
            IsActive = true,
            Conditions = new WorkflowConditionGroup
            {
                MatchType = "all",
                Conditions = new List<WorkflowCondition>
                {
                    new() { Field = "subject", Operator = "contains", Value = "urgent" }
                }
            },
            Actions = new List<WorkflowActionDto>()
        });

        var request = new WorkflowTestRequest
        {
            Subject = "This is a normal message"
        };

        // Act
        var result = await _workflowService.TestAsync(created.Id, request);

        // Assert
        result.Matched.Should().BeFalse();
    }

    #endregion

    #region TestAsync 收件人解析（T000103：白名单 / 去重 / 畸形地址）

    /// <summary>
    /// 构造一个"匹配条件 + send_email 动作"的工作流并执行 TestAsync，
    /// 通过 mock 的 EnqueueEmailAsync 捕获实际入队的收件人列表。
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
            Name = "Test-SendEmail",
            MailboxId = 1,
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
        // T000103 补全：customer + assignee 同源时只发送一封，避免重复入队
        var (result, sentTo) = await RunTestSendEmailAsync("customer, assignee", "agent@x.com");

        result.Matched.Should().BeTrue();
        result.ActionResults.Should().ContainSingle(r => r.Type == "send_email" && r.Success);
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "agent@x.com");
    }

    [Fact]
    public async Task TestSendEmail_DuplicateLiteralEmail_ShouldDedupToOne()
    {
        // T000103 补全：规则里重复填写同一邮箱时只发送一封
        var (result, sentTo) = await RunTestSendEmailAsync("ops@example.com, ops@example.com", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    [Fact]
    public async Task TestSendEmail_CaseInsensitiveDuplicate_ShouldDedupToOne()
    {
        // T000103 补全：大小写不同的同一邮箱视为重复
        var (result, sentTo) = await RunTestSendEmailAsync("ops@example.com, OPS@EXAMPLE.COM", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    [Fact]
    public async Task TestSendEmail_MalformedAddress_ShouldFilterAndFallbackToOperatorEmail()
    {
        // T000103 补全：a@b@example.com 多 @ 畸形地址被过滤，兜底发到操作者发件人邮箱
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
        // T000103 补全：畸形地址与合法地址混排时，畸形被过滤、合法正常发送
        var (result, sentTo) = await RunTestSendEmailAsync("a@b@example.com, ops@example.com", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    [Fact]
    public async Task TestSendEmail_ExternalDomainFiltered_ShouldFallbackAndNote()
    {
        // T000103：外部域名受白名单保护不真发，兜底到操作者发件人邮箱并在结果中注明
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
        // T000103：内部域 example.com 在白名单内，直接发送到配置收件人
        var (result, sentTo) = await RunTestSendEmailAsync("ops@example.com", null);

        result.Matched.Should().BeTrue();
        sentTo.Should().HaveCount(1);
        sentTo.Should().ContainSingle(e => e == "ops@example.com");
    }

    // ==================== 日志逻辑单元用例（超长 / 格式非法告警） ====================

    private static string OverlongEmail => $"{new string('a', 300)}@example.com"; // 306 字符 > RFC 5321 上限 254

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

    #endregion

    #region GetExecutionLogsAsync Tests

    [Fact]
    public async Task GetExecutionLogsAsync_ShouldReturnPagedLogs()
    {
        // Act
        var (items, total) = await _workflowService.GetExecutionLogsAsync(1, 10);

        // Assert
        total.Should().BeGreaterThanOrEqualTo(0);
        items.Should().NotBeNull();
    }

    [Fact]
    public async Task GetExecutionLogsAsync_WithWorkflowIdFilter_ShouldReturnFilteredLogs()
    {
        // Act
        var (items, total) = await _workflowService.GetExecutionLogsAsync(1, 10, workflowId: 999);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    #endregion

    #region ExecuteForConversationAsync Tests

    [Fact]
    public async Task ExecuteForConversationAsync_WithNoMatchingWorkflows_ShouldReturnZero()
    {
        // Arrange — 无工作流匹配，应返回 0
        // Act
        var result = await _workflowService.ExecuteForConversationAsync(999, "new_conversation");

        // Assert
        result.Should().Be(0);
    }

    [Fact(Skip = "InMemory DB 不支持 ExecuteDeleteAsync，需使用 SQL Server/SQLite 集成测试验证")]
    public async Task ExecuteForConversationAsync_WithMatchingWorkflow_ShouldExecuteAndReturnCount()
    {
        // Arrange — 设置 mock handler 让 set_status 动作返回成功
        var mockHandler = new Mock<IWorkflowActionHandler>();
        mockHandler.Setup(h => h.ExecuteAsync(It.IsAny<WorkflowAction>(), It.IsAny<Conversation>(), It.IsAny<WorkflowActionContext>()))
            .ReturnsAsync(true);
        _mockHandlerProvider
            .Setup(p => p.TryGetHandler(It.IsAny<string>(), out It.Ref<IWorkflowActionHandler?>.IsAny))
            .Returns(true)
            .Callback(new TryGetHandlerCallback((string _, out IWorkflowActionHandler? h) => h = mockHandler.Object));

        await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "Auto Execute",
            TriggerType = "new_conversation",
            IsActive = true,
            Actions = new List<WorkflowActionDto>
            {
                new() { Type = "set_status", Value = "2" }
            }
        });

        // 先创建会话（InMemory DB）
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "customer@example.com"
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        // Act
        var result = await _workflowService.ExecuteForConversationAsync(1, "new_conversation");

        // Assert
        result.Should().BeGreaterThanOrEqualTo(1);
    }

    private delegate void TryGetHandlerCallback(string actionType, out IWorkflowActionHandler? handler);

    #endregion

    #region ExecuteForSatisfactionRatingAsync Tests

    [Fact]
    public async Task ExecuteForSatisfactionRatingAsync_WithNoMatchingWorkflows_ShouldReturnZero()
    {
        // Act
        var result = await _workflowService.ExecuteForSatisfactionRatingAsync(999, "satisfaction_submitted");

        // Assert
        result.Should().Be(0);
    }

    #endregion
}