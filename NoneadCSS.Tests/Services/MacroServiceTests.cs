using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// MacroService 单元测试。
/// 重点验证宏的 set_status / assign_user 动作在执行时会调用 FolderService.UpdateConversationFolderAsync，
/// 确保 T000060 修复（FolderService 不再依赖未加载的 ConversationFolders 导航集合）覆盖宏执行路径（T000059）。
/// </summary>
public class MacroServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConversationService> _mockConversationService;
    private readonly Mock<IFolderService> _mockFolderService;
    private readonly Mock<ILogger<MacroService>> _mockLogger;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<IConversationThreadService> _mockThreadService;
    private readonly Mock<IMacroRepository> _mockMacroRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IMacroExecutionLogRepository> _mockMacroExecutionLogRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    private readonly MacroService _service;

    public MacroServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockConversationService = new Mock<IConversationService>();
        _mockFolderService = new Mock<IFolderService>();
        _mockLogger = new Mock<ILogger<MacroService>>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockThreadService = new Mock<IConversationThreadService>();
        _mockMacroRepo = new Mock<IMacroRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockMacroExecutionLogRepo = new Mock<IMacroExecutionLogRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.Macros).Returns(_mockMacroRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.MacroExecutionLogs).Returns(_mockMacroExecutionLogRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        // 默认允许访问邮箱
        _mockCurrentUserAccessor.Setup(c => c.CanAccessMailboxAsync(It.IsAny<int>())).ReturnsAsync(true);

        _service = new MacroService(
            _mockUnitOfWork.Object,
            _mockConversationService.Object,
            _mockFolderService.Object,
            _mockLogger.Object,
            _mockCurrentUserAccessor.Object,
            _mockThreadService.Object
        );
    }

    /// <summary>
    /// 构造一个启用状态、绑定 mailboxId 的宏，包含指定动作。
    /// </summary>
    private static Macro CreateMacro(int id, int mailboxId, List<MacroAction> actions)
    {
        return new Macro
        {
            Id = id,
            Name = "Test Macro",
            MailboxId = mailboxId,
            IsActive = true,
            ActionsJson = System.Text.Json.JsonSerializer.Serialize(actions,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    private static Conversation CreateConversation(int id, int mailboxId, ConversationStatus status = ConversationStatus.Active)
    {
        return new Conversation
        {
            Id = id,
            Number = id,
            MailboxId = mailboxId,
            Subject = "Test conversation",
            Status = status,
            State = ConversationState.Published,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    #region ExecuteAsync - set_status Tests

    [Fact]
    public async Task ExecuteAsync_WithSetStatusAction_ShouldCallUpdateConversationFolderAsync_AndSucceed()
    {
        // Arrange: 宏包含一个 set_status 动作，将会话状态改为 Pending
        var macro = CreateMacro(1, 10, new List<MacroAction>
        {
            new() { Type = "set_status", Value = "2" } // Pending = 2
        });
        var conversation = CreateConversation(400, 10, ConversationStatus.Active);

        _mockMacroRepo.Setup(r => r.GetByIdTrackedAsync(1)).ReturnsAsync(macro);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(400)).ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(400)).ReturnsAsync(conversation);
        // 模拟 FolderService 成功执行（T000060 修复后不再因导航集合为空报错）
        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        var result = await _service.ExecuteAsync(1, 400, userId: 1);

        // Assert: 宏执行成功，FolderService 被调用（验证 T000060 修复覆盖宏路径）
        result.Success.Should().BeTrue();
        result.ActionsExecuted.Should().Be(1);
        result.ExecutedActions.Should().ContainSingle();
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(It.Is<Conversation>(c => c.Status == ConversationStatus.Pending)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithSetStatusAction_ToClosed_ShouldCallFolderServiceAndSucceed()
    {
        // Arrange: 宏将会话从 Active 改为 Closed
        var macro = CreateMacro(1, 10, new List<MacroAction>
        {
            new() { Type = "set_status", Value = "3" } // Closed = 3
        });
        var conversation = CreateConversation(400, 10, ConversationStatus.Active);

        _mockMacroRepo.Setup(r => r.GetByIdTrackedAsync(1)).ReturnsAsync(macro);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(400)).ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(400)).ReturnsAsync(conversation);
        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(1, 400, userId: 1);

        // Assert
        result.Success.Should().BeTrue();
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(It.Is<Conversation>(c => c.Status == ConversationStatus.Closed)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleActionsIncludingSetStatus_ShouldSucceedAndCallFolderServiceForEach()
    {
        // Arrange: 宏包含 set_status + set_priority 两个动作
        var macro = CreateMacro(1, 10, new List<MacroAction>
        {
            new() { Type = "set_status", Value = "2" },     // Pending = 2
            new() { Type = "set_priority", Value = "3" }    // High = 3
        });
        var conversation = CreateConversation(400, 10, ConversationStatus.Active);

        _mockMacroRepo.Setup(r => r.GetByIdTrackedAsync(1)).ReturnsAsync(macro);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(400)).ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(400)).ReturnsAsync(conversation);
        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.ExecuteAsync(1, 400, userId: 1);

        // Assert: 两个动作都执行成功，FolderService 仅对 set_status 调用一次（set_priority 不触发文件夹更新）
        result.Success.Should().BeTrue();
        result.ActionsExecuted.Should().Be(2);
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFolderServiceThrows_ShouldRollbackAndReportFailure()
    {
        // Arrange: 模拟 FolderService 抛出异常（例如数据库约束冲突），
        // 验证宏执行会回滚事务并返回失败信息（这是 T000059 修复前的行为，
        // T000060 修复后 FolderService 不再抛出此异常，但 MacroService 的错误处理仍应保留）
        var macro = CreateMacro(1, 10, new List<MacroAction>
        {
            new() { Type = "set_status", Value = "2" }
        });
        var conversation = CreateConversation(400, 10, ConversationStatus.Active);

        _mockMacroRepo.Setup(r => r.GetByIdTrackedAsync(1)).ReturnsAsync(macro);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(400)).ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(400)).ReturnsAsync(conversation);
        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .ThrowsAsync(new InvalidOperationException("模拟文件夹更新失败"));

        // Act
        var result = await _service.ExecuteAsync(1, 400, userId: 1);

        // Assert: 宏执行失败，事务回滚
        // ExecuteActionAsync 内部 catch 捕获异常后返回 (false, actionName, ex.Message)，
        // ExecuteAsync 检测到失败后调用 transaction.RollbackAsync()（非 SafeRollbackAsync）
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("更改状态");
        result.Message.Should().Contain("模拟文件夹更新失败");
        result.ActionsExecuted.Should().Be(0);
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistingMacro_ShouldReturnFailure()
    {
        // Arrange
        _mockMacroRepo.Setup(r => r.GetByIdTrackedAsync(999)).ReturnsAsync((Macro?)null);

        // Act
        var result = await _service.ExecuteAsync(999, 400, userId: 1);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("宏不存在");
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistingConversation_ShouldReturnFailure()
    {
        // Arrange
        var macro = CreateMacro(1, 10, new List<MacroAction>
        {
            new() { Type = "set_status", Value = "2" }
        });
        _mockMacroRepo.Setup(r => r.GetByIdTrackedAsync(1)).ReturnsAsync(macro);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(400)).ReturnsAsync((Conversation?)null);

        // Act
        var result = await _service.ExecuteAsync(1, 400, userId: 1);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("会话不存在");
    }

    #endregion

    #region ExecuteAsync - assign_user Tests

    [Fact]
    public async Task ExecuteAsync_WithAssignUserAction_ShouldCallUpdateConversationFolderAsync_AndSucceed()
    {
        // Arrange: 宏包含 assign_user 动作，也会触发文件夹更新（分配后从未分配移到已分配）
        var macro = CreateMacro(1, 10, new List<MacroAction>
        {
            new() { Type = "assign_user", Value = "5" }
        });
        var conversation = CreateConversation(400, 10, ConversationStatus.Active);

        _mockMacroRepo.Setup(r => r.GetByIdTrackedAsync(1)).ReturnsAsync(macro);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(400)).ReturnsAsync(conversation);
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(400)).ReturnsAsync(conversation);
        _mockFolderService.Setup(f => f.UpdateConversationFolderAsync(It.IsAny<Conversation>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        var result = await _service.ExecuteAsync(1, 400, userId: 1);

        // Assert
        result.Success.Should().BeTrue();
        result.ActionsExecuted.Should().Be(1);
        _mockFolderService.Verify(f => f.UpdateConversationFolderAsync(It.Is<Conversation>(c => c.UserId == 5)), Times.Once);
    }

    #endregion

    #region PreviewAsync Tests

    [Fact]
    public async Task PreviewAsync_WithExistingMacroAndConversation_ShouldReturnPreview()
    {
        // Arrange
        var macro = CreateMacro(1, 10, new List<MacroAction>
        {
            new() { Type = "set_status", Value = "2" },
            new() { Type = "set_priority", Value = "3" }
        });
        var conversation = CreateConversation(400, 10);

        _mockMacroRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(macro);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(400)).ReturnsAsync(conversation);

        // Act
        var result = await _service.PreviewAsync(1, 400);

        // Assert
        result.Success.Should().BeTrue();
        result.MacroId.Should().Be(1);
        result.ConversationId.Should().Be(400);
        result.ActionCount.Should().Be(2);
        result.Actions.Should().HaveCount(2);
    }

    [Fact]
    public async Task PreviewAsync_WithNonExistingMacro_ShouldReturnFailure()
    {
        // Arrange
        _mockMacroRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Macro?)null);

        // Act
        var result = await _service.PreviewAsync(999, 400);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("宏不存在");
    }

    [Fact]
    public async Task PreviewAsync_WithNonExistingConversation_ShouldReturnFailureWithSuggestion()
    {
        // Arrange
        var macro = CreateMacro(1, 10, new List<MacroAction>
        {
            new() { Type = "set_status", Value = "2" }
        });
        _mockMacroRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(macro);
        _mockConversationRepo.Setup(r => r.GetByIdAsync(400)).ReturnsAsync((Conversation?)null);

        // 模拟存在其他会话供建议（使用支持异步操作的 TestAsyncEnumerable，FirstOrDefaultAsync 才能正常工作）
        var otherConversation = CreateConversation(300, 10);
        _mockConversationRepo.Setup(r => r.GetQueryable())
            .Returns(TestAsyncQueryProvider.CreateAsyncQueryable(new[] { otherConversation }));

        // Act
        var result = await _service.PreviewAsync(1, 400);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("会话 #400 不存在");
    }

    #endregion
}
