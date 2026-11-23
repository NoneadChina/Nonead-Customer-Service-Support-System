using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 工作流执行日志与动作处理器单元测试（InMemory DB）。
/// 覆盖执行日志写入（无匹配工作流时 not_matched 状态）及
/// H-006 修复（SetStatusActionHandler 同状态时跳过实时通知与工作流递归）。
/// </summary>
public class WorkflowExecutionLoggingTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private IUnitOfWork _unitOfWork = null!;
    private Mock<ILogger<WorkflowService>> _mockLogger = null!;
    private Mock<IMailTemplateService> _mockMailTemplateService = null!;
    private Mock<IEmailQueueService> _mockEmailQueueService = null!;
    private Mock<IConfiguration> _mockConfiguration = null!;
    private Mock<IFolderService> _mockFolderService = null!;
    private Mock<INotificationPublisher> _mockNotificationPublisher = null!;
    private Mock<IWebhookService> _mockWebhookService = null!;
    private Mock<IWorkflowActionHandlerProvider> _mockHandlerProvider = null!;
    private WorkflowService _workflowService = null!;
    private ServiceProvider _serviceProvider = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"WorkflowLoggingTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new NoneadCSSDbContext(options);
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        _mockLogger = new Mock<ILogger<WorkflowService>>();
        _mockMailTemplateService = new Mock<IMailTemplateService>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockFolderService = new Mock<IFolderService>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
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

    #region ExecuteWorkflowsForConversationAsync Tests

    /// <summary>
    /// 无匹配工作流时，ExecuteForConversationAsync 应写入
    /// Status = "not_matched" 的 WorkflowExecutionLog 记录。
    /// </summary>
    [Fact]
    public async Task ExecuteWorkflowsForConversationAsync_NoMatch_WritesLogWithStatusNotMatched()
    {
        // Arrange — 新建 Conversation（不创建任何匹配工作流）
        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Subject = "工作流执行日志测试：无匹配",
            MailboxId = 99, // 无对应工作流
            Status = ConversationStatus.Active,
            CustomerEmail = "test@example.com",
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        // Act — 调用公开方法 ExecuteForConversationAsync，内部会调用私有方法 ExecuteWorkflowsForConversationAsync
        await _workflowService.ExecuteForConversationAsync(conversation.Id, "new_conversation");
        await _unitOfWork.SaveChangesAsync();

        // Assert — 查询 WorkflowExecutionLogs，验证 not_matched 日志存在
        var logs = await _unitOfWork.Workflows.GetExecutionLogsAsync(conversation.Id, 50);

        // 如果 GetExecutionLogsAsync(workflowId) 签名不对，通过 DbContext 直接查询
        var directLogs = await _context.Set<WorkflowExecutionLog>()
            .AsNoTracking()
            .Where(l => l.ConversationId == conversation.Id)
            .OrderByDescending(l => l.ExecutedAt)
            .ToListAsync();

        // 两种方式结合断言：至少应该有一条日志记录通过某种方式可查
        // 因为无工作流时 workflows.Count == 0 → 直接 return 0，不写日志。
        // 因此需要先创建至少一个 TriggerType 不匹配或条件不匹配的工作流，让执行器进入循环并写 not_matched 日志。

        // ---- 重新构造场景：创建一个条件不匹配的工作流，保证进入循环写日志 ----

        // 清空先前日志（InMemory DB 按测试类共享，此处用新的 Conversation Id 避免交叉）
        var conversation2 = new Conversation
        {
            Subject = "工作流执行日志测试（有工作流但不匹配）",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "test2@example.com",
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Conversations.Add(conversation2);
        await _context.SaveChangesAsync();

        // 创建一个匹配 MailboxId=1、TriggerType="new_conversation" 但条件永远不通过的工作流
        var workflow = new Workflow
        {
            Name = "永远不匹配的工作流",
            MailboxId = 1,
            IsActive = true,
            TriggerType = "new_conversation",
            // 使用永远不成立的条件（subject contains 不可能出现的字符串）
            ConditionsJson = "{\"matchType\":\"all\",\"conditions\":[{\"field\":\"subject\",\"operator\":\"contains\",\"value\":\"__NEVER_MATCH_12345__\"}]}",
            ActionsJson = "[]",
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Workflows.Add(workflow);
        await _context.SaveChangesAsync();

        // Act
        await _workflowService.ExecuteForConversationAsync(conversation2.Id, "new_conversation");
        await _unitOfWork.SaveChangesAsync();

        // Assert
        var executionLogs = await _context.Set<WorkflowExecutionLog>()
            .AsNoTracking()
            .Where(l => l.ConversationId == conversation2.Id)
            .ToListAsync();

        executionLogs.Should().NotBeEmpty(
            because: "进入工作流循环后，对条件不匹配的工作流应写入 not_matched 日志");

        executionLogs.Should().Contain(log =>
            log.ConversationId == conversation2.Id &&
            log.WorkflowId == workflow.Id &&
            log.Status == "not_matched",
            because: "条件评估不通过时应记录 Status=not_matched，并关联正确的 ConversationId 与 WorkflowId");
    }

    #endregion

    #region SetStatusActionHandler Tests

    /// <summary>
    /// H-006 回归验证：SetStatusActionHandler 设置与当前相同的状态时，应跳过对外可见的副作用，
    /// 避免引发工作流递归循环。（Eventy 风格的事件层已移除，
    /// 该行为现在由 NotifyMailboxAsync 的 Times.Never 断言守住。）
    /// </summary>
    [Fact]
    public async Task SetStatusActionHandler_SameStatus_SkipsRealtimeNotification()
    {
        // Arrange — Conversation 当前状态 = Active
        var conversation = new Conversation
        {
            Id = 42,
            Subject = "H-006 同状态跳过事件",
            MailboxId = 1,
            Status = ConversationStatus.Active,
            CustomerEmail = "h006@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Mock 依赖（Eventy 风格的事件层已移除，无需再 mock 事件分发器）
        var mockUnitOfWork = new Mock<IUnitOfWork>();
        var mockFolderService = new Mock<IFolderService>();
        var mockNotificationPublisher = new Mock<INotificationPublisher>();
        var mockHandlerLogger = new Mock<ILogger<SetStatusActionHandler>>();

        var handler = new SetStatusActionHandler(
            mockUnitOfWork.Object,
            mockFolderService.Object,
            mockNotificationPublisher.Object,
            mockHandlerLogger.Object);

        // 构造动作：Value = 相同状态值（Active = 1，根据 ConversationStatus 枚举）
        // 先确认 ConversationStatus.Active 的整数值
        var activeIntValue = (int)ConversationStatus.Active;
        var action = new WorkflowAction
        {
            Type = "set_status",
            Value = activeIntValue.ToString()
        };

        // WorkflowActionContext 使用 init 属性初始化（无参数化构造函数）
        var actionContext = new WorkflowActionContext
        {
            Rating = null,
            ChatSession = null,
            PendingSideEffects = null,
            ServiceProvider = _serviceProvider
        };

        // Act
        var result = await handler.ExecuteAsync(action, conversation, actionContext);

        // Assert
        result.Should().BeTrue(because: "即使状态相同，动作本身也算执行成功（返回 true）");

        // H-006 核心验证：新旧状态相同（均为 Active）时不得产生任何对外可见的副作用，
        // 即不发送实时通知（Eventy 事件层已移除，实时通知是该分支唯一可观察的行为出口），
        // 否则工作流会因收到状态变更通知而递归触发。
        mockNotificationPublisher.Verify(
            p => p.NotifyMailboxAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>()),
            Times.Never);

        // 补充：同状态动作不应改写会话状态
        conversation.Status.Should().Be(ConversationStatus.Active, because: "目标状态与当前状态相同，不应产生状态变化");
    }

    #endregion
}
