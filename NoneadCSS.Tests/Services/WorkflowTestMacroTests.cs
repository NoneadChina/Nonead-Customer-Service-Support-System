using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// T000053：工作流"测试规则"应真实执行"执行宏"动作（沙箱回滚，不持久化）。
/// 背景：原测试模式仅真实执行 send_email，execute_macro 只返回"仅预览（测试模式不执行此动作）"，
/// 导致测试人员反馈"条件已满足但宏动作未触发"。
/// </summary>
public class WorkflowTestMacroTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private IUnitOfWork _unitOfWork = null!;
    private Mock<ILogger<WorkflowService>> _mockLogger = null!;
    private Mock<IFolderService> _mockFolderService = null!;
    private Mock<NoneadCSS.Domain.Events.INotificationPublisher> _mockNotificationPublisher = null!;
    private Mock<IWebhookService> _mockWebhookService = null!;
    private Mock<IWorkflowActionHandlerProvider> _mockHandlerProvider = null!;
    private Mock<IMacroService> _mockMacroService = null!;
    private WorkflowService _workflowService = null!;
    private ServiceProvider _serviceProvider = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"WorkflowTestMacroDb_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new NoneadCSSDbContext(options);
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        _mockLogger = new Mock<ILogger<WorkflowService>>();
        _mockFolderService = new Mock<IFolderService>();
        _mockNotificationPublisher = new Mock<NoneadCSS.Domain.Events.INotificationPublisher>();
        _mockWebhookService = new Mock<IWebhookService>();
        _mockHandlerProvider = new Mock<IWorkflowActionHandlerProvider>();
        _mockMacroService = new Mock<IMacroService>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(_mockMacroService.Object);
        _serviceProvider = serviceCollection.BuildServiceProvider();

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

    private async Task<Mailbox> CreateMailboxAsync()
    {
        var mailbox = new Mailbox
        {
            Name = "测试邮箱",
            Email = "test@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Mailboxes.Add(mailbox);
        await _context.SaveChangesAsync();
        return mailbox;
    }

    private async Task<WorkflowDto> CreateMacroWorkflowAsync(int mailboxId, string macroValue)
    {
        return await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "宏自动执行测试",
            Description = "T000053 回归测试",
            MailboxId = mailboxId,
            IsActive = true,
            SortOrder = 0,
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>
            {
                new() { Type = "execute_macro", Value = macroValue }
            }
        });
    }

    private WorkflowTestRequest BuildRequest()
    {
        return new WorkflowTestRequest
        {
            Subject = "测试主题",
            FromEmail = "customer@example.com",
            Body = "测试正文",
            MailboxId = 0
        };
    }

    [Fact]
    public async Task TestAsync_MacroActionMatched_ShouldInvokeMacroAndReportExecuted()
    {
        var mailbox = await CreateMailboxAsync();
        var workflow = await CreateMacroWorkflowAsync(mailbox.Id, "7");

        _mockMacroService
            .Setup(m => m.ExecuteForWorkflowAsync(7, It.IsAny<int>()))
            .ReturnsAsync(new ExecuteMacroResponse
            {
                Success = true,
                Message = "已执行 2 个动作",
                ActionsExecuted = 2,
                ExecutedActions = new List<string> { "添加标签", "修改状态" }
            });

        var result = await _workflowService.TestAsync(workflow.Id, BuildRequest());

        result.Matched.Should().BeTrue();
        var actionResult = result.ActionResults.Should().ContainSingle(x => x.Type == "execute_macro").Subject;
        actionResult.Executed.Should().BeTrue("条件满足时必须真正触发宏动作（T000053）");
        actionResult.Success.Should().BeTrue();
        actionResult.Message.Should().Contain("已触发宏执行");

        _mockMacroService.Verify(
            m => m.ExecuteForWorkflowAsync(7, It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    public async Task TestAsync_MacroFails_ShouldReportExecutedButFailed()
    {
        var mailbox = await CreateMailboxAsync();
        var workflow = await CreateMacroWorkflowAsync(mailbox.Id, "9");

        _mockMacroService
            .Setup(m => m.ExecuteForWorkflowAsync(9, It.IsAny<int>()))
            .ReturnsAsync(new ExecuteMacroResponse
            {
                Success = false,
                Message = "宏未启用或当前邮箱不可用",
                ActionsExecuted = 0,
                ExecutedActions = new List<string>()
            });

        var result = await _workflowService.TestAsync(workflow.Id, BuildRequest());

        result.Matched.Should().BeTrue();
        var actionResult = result.ActionResults.Should().ContainSingle(x => x.Type == "execute_macro").Subject;
        actionResult.Executed.Should().BeTrue();
        actionResult.Success.Should().BeFalse();
        actionResult.Message.Should().Contain("宏触发失败");
        actionResult.Message.Should().Contain("宏未启用或当前邮箱不可用");
    }

    [Fact]
    public async Task TestAsync_MacroIdInvalid_ShouldReportFailureWithoutCallingMacro()
    {
        var mailbox = await CreateMailboxAsync();
        // 值不是合法宏 ID（历史脏数据或旧版前端可能存名称）
        var workflow = await CreateMacroWorkflowAsync(mailbox.Id, "not-a-number");

        var result = await _workflowService.TestAsync(workflow.Id, BuildRequest());

        var actionResult = result.ActionResults.Should().ContainSingle(x => x.Type == "execute_macro").Subject;
        actionResult.Executed.Should().BeTrue();
        actionResult.Success.Should().BeFalse();
        actionResult.Message.Should().Contain("宏 ID 无效");

        _mockMacroService.Verify(
            m => m.ExecuteForWorkflowAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task TestAsync_NoMailboxAvailable_ShouldReportClearError()
    {
        // 工作流不绑定邮箱 + 测试请求也未指定邮箱 + 系统无可用邮箱 → 明确报错而不是静默失败
        var workflow = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "未绑定邮箱的宏工作流",
            MailboxId = 0,
            IsActive = true,
            TriggerType = "new_conversation",
            Actions = new List<WorkflowActionDto>
            {
                new() { Type = "execute_macro", Value = "3" }
            }
        });

        var result = await _workflowService.TestAsync(workflow.Id, BuildRequest());

        result.Matched.Should().BeTrue();
        var actionResult = result.ActionResults.Should().ContainSingle(x => x.Type == "execute_macro").Subject;
        actionResult.Executed.Should().BeTrue();
        actionResult.Success.Should().BeFalse();
        actionResult.Message.Should().Contain("未找到可用邮箱");
    }
}
