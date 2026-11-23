using FluentAssertions;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// TagService 单元测试
/// 覆盖标签的 CRUD、软删除、垃圾箱回收站等核心场景
/// </summary>
public class TagServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ITagRepository> _mockTagRepo;
    private readonly Mock<IConversationTagService> _mockConversationTagService;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly TagService _service;

    public TagServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockTagRepo = new Mock<ITagRepository>();
        _mockConversationTagService = new Mock<IConversationTagService>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();

        _mockUnitOfWork.Setup(u => u.Tags).Returns(_mockTagRepo.Object);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>()))
            .ReturnsAsync(true);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _service = new TagService(_mockUnitOfWork.Object, _mockConversationTagService.Object, _mockCurrentUserAccessor.Object);
    }

    #region GetTagsAsync

    [Fact]
    public async Task GetTagsAsync_WithTags_ShouldReturnTagsWithCounts()
    {
        // Arrange
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "Important", Color = "#ff0000", MailboxId = 1 },
            new() { Id = 2, Name = "Bug", Color = "#00ff00", MailboxId = 1 }
        };

        _mockTagRepo.Setup(r => r.GetByMailboxIdAsync(1))
            .ReturnsAsync(tags);
        _mockTagRepo.Setup(r => r.GetTagCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int> { { 1, 5 }, { 2, 3 } });

        // Act
        var result = await _service.GetTagsAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Important");
        result[0].ConversationCount.Should().Be(5);
        result[1].Id.Should().Be(2);
        result[1].ConversationCount.Should().Be(3);
    }

    [Fact]
    public async Task GetTagsAsync_WithNoTags_ShouldReturnEmptyList()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.GetByMailboxIdAsync(It.IsAny<int?>()))
            .ReturnsAsync(new List<Tag>());
        _mockTagRepo.Setup(r => r.GetTagCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int>());

        // Act
        var result = await _service.GetTagsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTagsAsync_WhenCountMissing_ShouldDefaultToZero()
    {
        // Arrange - counts 字典不包含该 TagId，应默认为 0
        var tags = new List<Tag>
        {
            new() { Id = 1, Name = "TagWithoutCount", Color = "#000000", MailboxId = null }
        };

        _mockTagRepo.Setup(r => r.GetByMailboxIdAsync(It.IsAny<int?>()))
            .ReturnsAsync(tags);
        _mockTagRepo.Setup(r => r.GetTagCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int>());

        // Act
        var result = await _service.GetTagsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].ConversationCount.Should().Be(0);
    }

    #endregion

    #region GetTagByIdAsync

    [Fact]
    public async Task GetTagByIdAsync_WithExistingTag_ShouldReturnTagWithCount()
    {
        // Arrange
        var tag = new Tag { Id = 1, Name = "Important", Color = "#ff0000", MailboxId = 1 };

        _mockTagRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(tag);
        _mockTagRepo.Setup(r => r.GetTagCountAsync(1))
            .ReturnsAsync(7);

        // Act
        var result = await _service.GetTagByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Important");
        result.ConversationCount.Should().Be(7);
    }

    [Fact]
    public async Task GetTagByIdAsync_WithNonExistingTag_ShouldReturnNull()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Tag?)null);

        // Act
        var result = await _service.GetTagByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTagByIdAsync_WithSoftDeletedTag_ShouldReturnNull()
    {
        // Arrange
        var tag = new Tag
        {
            Id = 1,
            Name = "Deleted",
            Color = "#000000",
            DeletedAt = DateTime.UtcNow.AddDays(-1)
        };

        _mockTagRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(tag);

        // Act
        var result = await _service.GetTagByIdAsync(1);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateTagAsync

    [Fact]
    public async Task CreateTagAsync_WithValidRequest_ShouldCreateAndReturnTag()
    {
        // Arrange
        var request = new CreateTagRequest
        {
            Name = "NewTag",
            Color = "#abcdef",
            MailboxId = 1
        };

        _mockTagRepo.Setup(r => r.GetByNameAsync(request.Name, request.MailboxId))
            .ReturnsAsync((Tag?)null);

        Tag? captured = null;
        _mockTagRepo.Setup(r => r.AddAsync(It.IsAny<Tag>()))
            .Callback<Tag>(t => captured = t)
            .ReturnsAsync((Tag t) => t);

        // Act
        var result = await _service.CreateTagAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("NewTag");
        result.Color.Should().Be("#abcdef");
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("NewTag");
        captured.MailboxId.Should().Be(1);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateTagAsync_WithDuplicateName_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var request = new CreateTagRequest
        {
            Name = "Existing",
            Color = "#000000",
            MailboxId = 1
        };

        var existing = new Tag { Id = 10, Name = "Existing", Color = "#000000", MailboxId = 1 };
        _mockTagRepo.Setup(r => r.GetByNameAsync(request.Name, request.MailboxId))
            .ReturnsAsync(existing);

        // Act
        var act = () => _service.CreateTagAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
        _mockTagRepo.Verify(r => r.AddAsync(It.IsAny<Tag>()), Times.Never);
    }

    #endregion

    #region UpdateTagAsync

    [Fact]
    public async Task UpdateTagAsync_WithValidRequest_ShouldUpdateAndReturnTag()
    {
        // Arrange
        var tag = new Tag { Id = 1, Name = "OldName", Color = "#000000", MailboxId = 1 };
        var request = new UpdateTagRequest { Name = "NewName", Color = "#ffffff" };

        _mockTagRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(tag);
        _mockTagRepo.Setup(r => r.GetByNameAsync(request.Name, tag.MailboxId))
            .ReturnsAsync((Tag?)null);
        _mockTagRepo.Setup(r => r.GetTagCountAsync(1))
            .ReturnsAsync(4);

        // Act
        var result = await _service.UpdateTagAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("NewName");
        result.Color.Should().Be("#ffffff");
        result.ConversationCount.Should().Be(4);
        tag.Name.Should().Be("NewName");
        tag.Color.Should().Be("#ffffff");
        tag.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateTagAsync_WithNonExistingTag_ShouldReturnNull()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.GetByIdTrackedAsync(999))
            .ReturnsAsync((Tag?)null);

        var request = new UpdateTagRequest { Name = "X", Color = "#000000" };

        // Act
        var result = await _service.UpdateTagAsync(999, request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateTagAsync_WithSoftDeletedTag_ShouldReturnNull()
    {
        // Arrange
        var tag = new Tag
        {
            Id = 1,
            Name = "Deleted",
            Color = "#000000",
            DeletedAt = DateTime.UtcNow
        };

        _mockTagRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(tag);

        var request = new UpdateTagRequest { Name = "X", Color = "#000000" };

        // Act
        var result = await _service.UpdateTagAsync(1, request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTagAsync_WithDuplicateName_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var tag = new Tag { Id = 1, Name = "OldName", Color = "#000000", MailboxId = 1 };
        var other = new Tag { Id = 2, Name = "Existing", Color = "#000000", MailboxId = 1 };

        _mockTagRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(tag);
        _mockTagRepo.Setup(r => r.GetByNameAsync("Existing", tag.MailboxId))
            .ReturnsAsync(other);

        var request = new UpdateTagRequest { Name = "Existing", Color = "#000000" };

        // Act
        var act = () => _service.UpdateTagAsync(1, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
    }

    [Fact]
    public async Task UpdateTagAsync_WithSameNameAsCurrent_ShouldNotThrow()
    {
        // Arrange - 当查询到自身时不应该抛异常（existingTag.Id == id）
        var tag = new Tag { Id = 1, Name = "SameName", Color = "#000000", MailboxId = 1 };

        _mockTagRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(tag);
        // 返回的是自身
        _mockTagRepo.Setup(r => r.GetByNameAsync("SameName", tag.MailboxId))
            .ReturnsAsync(tag);
        _mockTagRepo.Setup(r => r.GetTagCountAsync(1))
            .ReturnsAsync(0);

        var request = new UpdateTagRequest { Name = "SameName", Color = "#ffffff" };

        // Act
        var result = await _service.UpdateTagAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.Color.Should().Be("#ffffff");
    }

    #endregion

    #region DeleteTagAsync

    [Fact]
    public async Task DeleteTagAsync_WithExistingTag_ShouldSoftDeleteAndReturnTrue()
    {
        // Arrange
        var tag = new Tag { Id = 1, Name = "ToDelete", Color = "#000000", MailboxId = 1 };

        _mockTagRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(tag);

        // Act
        var result = await _service.DeleteTagAsync(1);

        // Assert
        result.Should().BeTrue();
        tag.DeletedAt.Should().NotBeNull();
        tag.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteTagAsync_WithNonExistingTag_ShouldReturnFalse()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.GetByIdTrackedAsync(999))
            .ReturnsAsync((Tag?)null);

        // Act
        var result = await _service.DeleteTagAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteTagAsync_WithAlreadyDeletedTag_ShouldReturnFalse()
    {
        // Arrange
        var tag = new Tag
        {
            Id = 1,
            Name = "AlreadyDeleted",
            Color = "#000000",
            DeletedAt = DateTime.UtcNow
        };

        _mockTagRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(tag);

        // Act
        var result = await _service.DeleteTagAsync(1);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Recycle Bin (Trash) Tests

    [Fact]
    public async Task GetTrashAsync_ShouldReturnPagedDeletedTags()
    {
        // Arrange
        var deletedTags = new List<Tag>
        {
            new() { Id = 1, Name = "Deleted1", Color = "#000000", DeletedAt = DateTime.UtcNow },
            new() { Id = 2, Name = "Deleted2", Color = "#000000", DeletedAt = DateTime.UtcNow }
        };

        _mockTagRepo.Setup(r => r.GetDeletedAsync(1, 20, null))
            .ReturnsAsync((deletedTags, 2));

        // Act
        var result = await _service.GetTrashAsync(1, 20);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().HaveCount(2);
        result.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task GetTrashCountAsync_ShouldReturnCount()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.CountDeletedAsync())
            .ReturnsAsync(5);

        // Act
        var result = await _service.GetTrashCountAsync();

        // Assert
        result.Should().Be(5);
    }

    [Fact]
    public async Task RestoreTagAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.RestoreAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.RestoreTagAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HardDeleteTagAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.HardDeleteAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.HardDeleteTagAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ClearTrashAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        _mockTagRepo.Setup(r => r.HardDeleteAllDeletedAsync())
            .ReturnsAsync(7);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(7);
    }

    #endregion

    #region Conversation Tag Delegation

    [Fact]
    public async Task GetConversationTagsAsync_ShouldDelegateToConversationTagService()
    {
        // Arrange
        var expected = new List<TagResponse>
        {
            new() { Id = 1, Name = "A" }
        };
        _mockConversationTagService.Setup(s => s.GetConversationTagsAsync(5))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.GetConversationTagsAsync(5);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task UpdateConversationTagsAsync_ShouldDelegateToConversationTagService()
    {
        // Arrange
        var request = new UpdateConversationTagsRequest { TagIds = new List<int> { 1, 2 } };
        var expected = new List<TagResponse> { new() { Id = 1, Name = "A" } };
        _mockConversationTagService.Setup(s => s.UpdateConversationTagsAsync(5, request))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.UpdateConversationTagsAsync(5, request);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task AddTagToConversationAsync_ShouldDelegateToConversationTagService()
    {
        // Arrange
        var expected = new List<TagResponse> { new() { Id = 1, Name = "A" } };
        _mockConversationTagService.Setup(s => s.AddTagToConversationAsync(5, 9))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.AddTagToConversationAsync(5, 9);

        // Assert
        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RemoveTagFromConversationAsync_ShouldDelegateToConversationTagService()
    {
        // Arrange
        _mockConversationTagService.Setup(s => s.RemoveTagFromConversationAsync(5, 9))
            .ReturnsAsync(true);

        // Act
        var result = await _service.RemoveTagFromConversationAsync(5, 9);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BulkUpdateConversationTagsAsync_ShouldDelegateToConversationTagService()
    {
        // Arrange
        var request = new BulkUpdateConversationTagsRequest
        {
            ConversationIds = new List<int> { 1, 2 },
            TagIds = new List<int> { 10 }
        };
        var expected = new BulkOperationResponse();
        _mockConversationTagService.Setup(s => s.BulkUpdateConversationTagsAsync(request))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.BulkUpdateConversationTagsAsync(request);

        // Assert
        result.Should().BeSameAs(expected);
    }

    #endregion
}
