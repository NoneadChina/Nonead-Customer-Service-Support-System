using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;
using System.Linq.Expressions;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ConversationTagService 单元测试 - 使用 Moq 完全模拟依赖
/// </summary>
public class ConversationTagServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<ILogger<ConversationTagService>> _mockLogger;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly Mock<ITagRepository> _mockTagRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IRepository<ConversationTag>> _mockConversationTagRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    private readonly ConversationTagService _service;

    public ConversationTagServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockLogger = new Mock<ILogger<ConversationTagService>>();
        // UserId 默认 0 = 无 HTTP 用户上下文（后台/系统调用），访问控制放行，保持既有用例语义不变
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();
        _mockTagRepo = new Mock<ITagRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockConversationTagRepo = new Mock<IRepository<ConversationTag>>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.Tags).Returns(_mockTagRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Repository<ConversationTag>()).Returns(_mockConversationTagRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _service = new ConversationTagService(
            _mockUnitOfWork.Object,
            _mockWorkflowService.Object,
            _mockLogger.Object,
            _mockCurrentUserAccessor.Object
        );
    }

    #region GetConversationTagsAsync Tests

    [Fact]
    public async Task GetConversationTagsAsync_WithExistingTags_ShouldReturnTagResponses()
    {
        // Arrange
        var tags = new List<Tag>
        {
            new Tag { Id = 1, Name = "Important", Color = "#ff0000", MailboxId = 1 },
            new Tag { Id = 2, Name = "Bug", Color = "#00ff00", MailboxId = 1 }
        };

        _mockTagRepo.Setup(r => r.GetByConversationIdAsync(1))
            .ReturnsAsync(tags);

        // Act
        var result = await _service.GetConversationTagsAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Important");
        result[1].Id.Should().Be(2);
        result[1].Name.Should().Be("Bug");
    }

    [Fact]
    public async Task GetConversationTagsAsync_WithNoTags_ShouldReturnEmptyList()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.GetByConversationIdAsync(999))
            .ReturnsAsync(new List<Tag>());

        // Act
        var result = await _service.GetConversationTagsAsync(999);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion

    #region UpdateConversationTagsAsync Tests

    [Fact]
    public async Task UpdateConversationTagsAsync_WithNonExistingConversation_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999))
            .ReturnsAsync((Conversation?)null);

        var request = new UpdateConversationTagsRequest
        {
            TagIds = new List<int> { 1, 2 }
        };

        // Act
        Func<Task> act = async () => await _service.UpdateConversationTagsAsync(999, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("会话不存在");
    }

    [Fact]
    public async Task UpdateConversationTagsAsync_WithValidConversation_ShouldReplaceTags()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        var existingConversationTags = new List<ConversationTag>
        {
            new ConversationTag { Id = 10, ConversationId = 1, TagId = 3 }
        };

        _mockConversationTagRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationTag, bool>>>()))
            .ReturnsAsync(existingConversationTags);

        var newTags = new List<Tag>
        {
            new Tag { Id = 1, Name = "Important", Color = "#ff0000" },
            new Tag { Id = 2, Name = "Bug", Color = "#00ff00" }
        };

        _mockTagRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(newTags);

        _mockTagRepo.Setup(r => r.GetByConversationIdAsync(1))
            .ReturnsAsync(newTags);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        var request = new UpdateConversationTagsRequest
        {
            TagIds = new List<int> { 1, 2 }
        };

        // Act
        var result = await _service.UpdateConversationTagsAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        _mockConversationTagRepo.Verify(r => r.DeleteAsync(It.IsAny<ConversationTag>()), Times.Once);
        _mockConversationTagRepo.Verify(r => r.AddAsync(It.IsAny<ConversationTag>()), Times.Exactly(2));
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateConversationTagsAsync_WithEmptyTagIds_ShouldClearExistingTags()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        var existingConversationTags = new List<ConversationTag>
        {
            new ConversationTag { Id = 10, ConversationId = 1, TagId = 3 },
            new ConversationTag { Id = 11, ConversationId = 1, TagId = 4 }
        };

        _mockConversationTagRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationTag, bool>>>()))
            .ReturnsAsync(existingConversationTags);

        _mockTagRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<Tag>());

        _mockTagRepo.Setup(r => r.GetByConversationIdAsync(1))
            .ReturnsAsync(new List<Tag>());

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        var request = new UpdateConversationTagsRequest
        {
            TagIds = new List<int>()
        };

        // Act
        var result = await _service.UpdateConversationTagsAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();

        _mockConversationTagRepo.Verify(r => r.DeleteAsync(It.IsAny<ConversationTag>()), Times.Exactly(2));
        _mockConversationTagRepo.Verify(r => r.AddAsync(It.IsAny<ConversationTag>()), Times.Never);
    }

    #endregion

    #region AddTagToConversationAsync Tests

    [Fact]
    public async Task AddTagToConversationAsync_WithNonExistingConversation_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(999))
            .ReturnsAsync((Conversation?)null);

        // Act
        Func<Task> act = async () => await _service.AddTagToConversationAsync(999, 1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("会话不存在");
    }

    [Fact]
    public async Task AddTagToConversationAsync_WithNonExistingTag_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockTagRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Tag?)null);

        // Act
        Func<Task> act = async () => await _service.AddTagToConversationAsync(1, 999);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("标签不存在");
    }

    [Fact]
    public async Task AddTagToConversationAsync_WithNewTag_ShouldAddAndTriggerWorkflow()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        var tag = new Tag { Id = 1, Name = "Important", Color = "#ff0000" };
        _mockTagRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(tag);

        _mockConversationTagRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationTag, bool>>>()))
            .ReturnsAsync(new List<ConversationTag>());

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockTagRepo.Setup(r => r.GetByConversationIdAsync(1))
            .ReturnsAsync(new List<Tag> { tag });

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerTagAdded))
            .ReturnsAsync(1);

        // Act
        var result = await _service.AddTagToConversationAsync(1, 1);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(1);

        _mockConversationTagRepo.Verify(r => r.AddAsync(It.IsAny<ConversationTag>()), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerTagAdded), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task AddTagToConversationAsync_WithExistingTag_ShouldNotAddDuplicate()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        var tag = new Tag { Id = 1, Name = "Important", Color = "#ff0000" };
        _mockTagRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(tag);

        var existingConversationTag = new ConversationTag { Id = 10, ConversationId = 1, TagId = 1 };
        _mockConversationTagRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationTag, bool>>>()))
            .ReturnsAsync(new List<ConversationTag> { existingConversationTag });

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockTagRepo.Setup(r => r.GetByConversationIdAsync(1))
            .ReturnsAsync(new List<Tag> { tag });

        // Act
        var result = await _service.AddTagToConversationAsync(1, 1);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);

        _mockConversationTagRepo.Verify(r => r.AddAsync(It.IsAny<ConversationTag>()), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(It.IsAny<int>(), WorkflowConst.TriggerTagAdded), Times.Never);
    }

    [Fact]
    public async Task AddTagToConversationAsync_WhenWorkflowFails_ShouldStillReturnTags()
    {
        // Arrange
        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };

        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        var tag = new Tag { Id = 1, Name = "Important", Color = "#ff0000" };
        _mockTagRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(tag);

        _mockConversationTagRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationTag, bool>>>()))
            .ReturnsAsync(new List<ConversationTag>());

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockTagRepo.Setup(r => r.GetByConversationIdAsync(1))
            .ReturnsAsync(new List<Tag> { tag });

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerTagAdded))
            .ThrowsAsync(new Exception("Workflow failed"));

        // Act
        var result = await _service.AddTagToConversationAsync(1, 1);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    #endregion

    #region RemoveTagFromConversationAsync Tests

    [Fact]
    public async Task RemoveTagFromConversationAsync_WithNonExistingTag_ShouldReturnFalse()
    {
        // Arrange
        _mockConversationTagRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationTag, bool>>>()))
            .ReturnsAsync(new List<ConversationTag>());

        // Act
        var result = await _service.RemoveTagFromConversationAsync(1, 999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task RemoveTagFromConversationAsync_WithExistingTag_ShouldRemoveAndTriggerWorkflow()
    {
        // Arrange
        var conversationTag = new ConversationTag { Id = 10, ConversationId = 1, TagId = 1 };
        _mockConversationTagRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationTag, bool>>>()))
            .ReturnsAsync(new List<ConversationTag> { conversationTag });

        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerTagRemoved))
            .ReturnsAsync(1);

        // Act
        var result = await _service.RemoveTagFromConversationAsync(1, 1);

        // Assert
        result.Should().BeTrue();

        _mockConversationTagRepo.Verify(r => r.DeleteAsync(conversationTag), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerTagRemoved), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task RemoveTagFromConversationAsync_WhenWorkflowFails_ShouldStillReturnTrue()
    {
        // Arrange
        var conversationTag = new ConversationTag { Id = 10, ConversationId = 1, TagId = 1 };
        _mockConversationTagRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationTag, bool>>>()))
            .ReturnsAsync(new List<ConversationTag> { conversationTag });

        var conversation = new Conversation
        {
            Id = 1,
            Subject = "Test",
            MailboxId = 1,
            CustomerEmail = "test@example.com"
        };
        _mockConversationRepo.Setup(r => r.GetByIdForUpdateAsync(1))
            .ReturnsAsync(conversation);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockWorkflowService.Setup(w => w.ExecuteForConversationAsync(1, WorkflowConst.TriggerTagRemoved))
            .ThrowsAsync(new Exception("Workflow failed"));

        // Act
        var result = await _service.RemoveTagFromConversationAsync(1, 1);

        // Assert
        result.Should().BeTrue();
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    #endregion

    #region BulkUpdateConversationTagsAsync Tests

    [Fact]
    public async Task BulkUpdateConversationTagsAsync_WithValidConversations_ShouldUpdateSuccessfully()
    {
        // Arrange
        var request = new BulkUpdateConversationTagsRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            TagIds = new List<int> { 1, 2 }
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, CustomerEmail = "test1@example.com" },
            new Conversation { Id = 2, Subject = "Test 2", MailboxId = 1, CustomerEmail = "test2@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        var newTags = new List<Tag>
        {
            new Tag { Id = 1, Name = "Important", Color = "#ff0000" },
            new Tag { Id = 2, Name = "Bug", Color = "#00ff00" }
        };

        _mockTagRepo.Setup(r => r.GetByIdsAsync(request.TagIds))
            .ReturnsAsync(newTags);

        // 使用 TestAsyncQueryProvider 创建支持异步 LINQ 的 Mock DbSet
        var existingConversationTags = new List<ConversationTag>();
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(existingConversationTags);
        _mockConversationTagRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _service.BulkUpdateConversationTagsAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.TotalCount.Should().Be(2);

        // 每个会话添加 2 个标签，共 4 次
        _mockConversationTagRepo.Verify(r => r.AddAsync(It.IsAny<ConversationTag>()), Times.Exactly(4));
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Exactly(1));
    }

    [Fact]
    public async Task BulkUpdateConversationTagsAsync_WithSomeNonExistingConversations_ShouldReportFailures()
    {
        // Arrange
        var request = new BulkUpdateConversationTagsRequest
        {
            ConversationIds = new List<int> { 1, 999 },
            TagIds = new List<int> { 1 }
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, CustomerEmail = "test1@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        var newTags = new List<Tag>
        {
            new Tag { Id = 1, Name = "Important", Color = "#ff0000" }
        };

        _mockTagRepo.Setup(r => r.GetByIdsAsync(request.TagIds))
            .ReturnsAsync(newTags);

        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(new List<ConversationTag>());
        _mockConversationTagRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _service.BulkUpdateConversationTagsAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.TotalCount.Should().Be(2);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("999");
    }

    [Fact]
    public async Task BulkUpdateConversationTagsAsync_WithExistingTags_ShouldDeleteAndReAdd()
    {
        // Arrange
        var request = new BulkUpdateConversationTagsRequest
        {
            ConversationIds = new List<int> { 1 },
            TagIds = new List<int> { 2 }
        };

        var conversations = new List<Conversation>
        {
            new Conversation { Id = 1, Subject = "Test 1", MailboxId = 1, CustomerEmail = "test1@example.com" }
        };

        _mockConversationRepo.Setup(r => r.GetByIdsForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        var newTags = new List<Tag>
        {
            new Tag { Id = 2, Name = "Bug", Color = "#00ff00" }
        };

        _mockTagRepo.Setup(r => r.GetByIdsAsync(request.TagIds))
            .ReturnsAsync(newTags);

        // 预置已存在的会话标签，验证删除逻辑
        var existingConversationTags = new List<ConversationTag>
        {
            new ConversationTag { Id = 10, ConversationId = 1, TagId = 3 },
            new ConversationTag { Id = 11, ConversationId = 1, TagId = 4 }
        };
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(existingConversationTags);
        _mockConversationTagRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _service.BulkUpdateConversationTagsAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);

        // 删除旧标签（2 个），添加新标签（1 个）
        _mockConversationTagRepo.Verify(r => r.DeleteAsync(It.IsAny<ConversationTag>()), Times.Exactly(2));
        _mockConversationTagRepo.Verify(r => r.AddAsync(It.IsAny<ConversationTag>()), Times.Once);
    }

    /// <summary>
    /// 关键路径：TagIds 传空数组时，语义应为「清空所选会话的所有已有标签」。
    /// 必须调用 DeleteAsync 删除全部已有，且不调用 AddAsync，SuccessCount 等于有效会话数。
    /// </summary>
    [Fact]
    public async Task BulkUpdateConversationTagsAsync_WithEmptyTagIds_ShouldClearAllExistingTags()
    {
        // Arrange
        var request = new BulkUpdateConversationTagsRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            TagIds = new List<int>()      // ← 本次补测的核心场景
        };

        var conversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "Conv 1", MailboxId = 1, CustomerEmail = "a@example.com" },
            new() { Id = 2, Subject = "Conv 2", MailboxId = 1, CustomerEmail = "b@example.com" }
        };
        _mockConversationRepo.Setup(r => r.GetByIdsForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);

        // 空数组 GetByIdsAsync → 返回空 IEnumerable
        _mockTagRepo.Setup(r => r.GetByIdsAsync(It.Is<IEnumerable<int>>(ids => !ids.Any())))
            .ReturnsAsync(Enumerable.Empty<Tag>());

        // 预置 2 个会话都有旧标签：会话 1 有 3/4，会话 2 有 5
        var existing = new List<ConversationTag>
        {
            new() { Id = 10, ConversationId = 1, TagId = 3 },
            new() { Id = 11, ConversationId = 1, TagId = 4 },
            new() { Id = 12, ConversationId = 2, TagId = 5 }
        };
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(existing);
        _mockConversationTagRepo.Setup(r => r.GetQueryable(It.IsAny<bool>())).Returns(mockDbSet.Object);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.BulkUpdateConversationTagsAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.SuccessCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.TotalCount.Should().Be(2);

        // 必须删除 3 条旧标签
        _mockConversationTagRepo.Verify(r => r.DeleteAsync(It.IsAny<ConversationTag>()), Times.Exactly(3));
        // 绝不应该新增任何标签（TagIds 为空）
        _mockConversationTagRepo.Verify(r => r.AddAsync(It.IsAny<ConversationTag>()), Times.Never);
        // 一次性事务提交成功
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        // 两个会话的 UpdatedAt 都在事务中按同一 now 赋值（此处通过 SaveChanges 调用次数间接覆盖）
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    /// <summary>
    /// 主事务 SaveChanges 抛 DbUpdateException（例如 Number/主键冲突）时，应进入降级逐条事务分支，
    /// 不再回滚所有会话的成功，而是保留会话级部分成功语义。
    /// 此用例覆盖 BulkUpdateConversationTagsAsync 的 catch → foreach(perTx) 分支。
    /// </summary>
    [Fact]
    public async Task BulkUpdateConversationTagsAsync_WhenMainTransactionFails_ShouldFallbackToPerConversation()
    {
        // Arrange
        var request = new BulkUpdateConversationTagsRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            TagIds = new List<int> { 9 }
        };
        var conversations = new List<Conversation>
        {
            new() { Id = 1, Subject = "C1", MailboxId = 1, CustomerEmail = "1@x.com" },
            new() { Id = 2, Subject = "C2", MailboxId = 1, CustomerEmail = "2@x.com" }
        };
        _mockConversationRepo.Setup(r => r.GetByIdsForUpdateAsync(request.ConversationIds))
            .ReturnsAsync(conversations);
        _mockTagRepo.Setup(r => r.GetByIdsAsync(request.TagIds))
            .ReturnsAsync(new List<Tag> { new() { Id = 9, Name = "Urgent" } });
        _mockConversationTagRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(TestAsyncQueryProvider.CreateMockDbSet(new List<ConversationTag>()).Object);

        // 主事务 1 次 SaveChanges → 抛异常，强制走 fallback
        int callCount = 0;
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1) throw new InvalidOperationException("Simulated bulk commit failure");
                return Task.FromResult(1);
            });

        // Act
        var result = await _service.BulkUpdateConversationTagsAsync(request);

        // Assert
        result.SuccessCount.Should().Be(2, "fallback 逐条事务均成功");
        result.FailedCount.Should().Be(0);
        // 1（主事务） + 2（fallback 两条） = 3 次 SaveChanges
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Exactly(3));
        // ClearChanges 至少被调用一次（主事务失败）
        _mockUnitOfWork.Verify(u => u.ClearChanges(), Times.AtLeastOnce);
    }

    #endregion
}
