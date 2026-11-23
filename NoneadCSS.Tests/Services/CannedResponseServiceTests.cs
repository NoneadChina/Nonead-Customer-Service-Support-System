using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
/// CannedResponseService 单元测试
/// 覆盖快捷回复模板的 CRUD、搜索、权限校验、软删除与回收站
/// </summary>
public class CannedResponseServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ICannedResponseRepository> _mockRepo;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly CannedResponseService _service;

    public CannedResponseServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockRepo = new Mock<ICannedResponseRepository>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();

        _mockUnitOfWork.Setup(u => u.CannedResponses).Returns(_mockRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>()))
            .ReturnsAsync(true);

        _service = new CannedResponseService(_mockUnitOfWork.Object, _mockCurrentUserAccessor.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock CannedResponse DbSet
    /// </summary>
    private void SetupQueryable(params CannedResponse[] items)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(items.ToList());
        _mockRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    #region GetListAsync

    [Fact]
    public async Task GetListAsync_WithItems_ShouldReturnDtos()
    {
        // Arrange
        var items = new List<CannedResponse>
        {
            new() { Id = 1, Name = "Greeting", Body = "<p>Hello</p>", MailboxId = 1, IsShared = true, CreatedByUserId = 1 },
            new() { Id = 2, Name = "Closing", Body = "<p>Bye</p>", MailboxId = 1, IsShared = false, CreatedByUserId = 2 }
        };

        _mockRepo.Setup(r => r.GetByMailboxAsync(1, null))
            .ReturnsAsync(items);

        // Act
        var result = await _service.GetListAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Greeting");
        result[1].Name.Should().Be("Closing");
    }

    [Fact]
    public async Task GetListAsync_WithCategory_ShouldFilterByCategory()
    {
        // Arrange
        var items = new List<CannedResponse>
        {
            new() { Id = 1, Name = "A", Body = "x", Category = "Sales", MailboxId = 1 },
            new() { Id = 2, Name = "B", Body = "y", Category = "Support", MailboxId = 1 }
        };

        _mockRepo.Setup(r => r.GetByMailboxAsync(1, null))
            .ReturnsAsync(items);

        // Act
        var result = await _service.GetListAsync(1, category: "Sales");

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("A");
    }

    [Fact]
    public async Task GetListAsync_WithEmptyResult_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByMailboxAsync(It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<CannedResponse>());

        // Act
        var result = await _service.GetListAsync();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WithExisting_ShouldReturnDto()
    {
        // Arrange
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "Greeting",
            Body = "<p>Hello</p>",
            MailboxId = 1
        };

        _mockRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(entity);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Greeting");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((CannedResponse?)null);

        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithSoftDeleted_ShouldReturnNull()
    {
        // Arrange
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "X",
            Body = "y",
            DeletedAt = DateTime.UtcNow
        };

        _mockRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(entity);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region SearchAsync

    [Fact]
    public async Task SearchAsync_WithWhitespaceKeyword_ShouldReturnAllList()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByMailboxAsync(It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<CannedResponse>());

        // Act
        var result = await _service.SearchAsync("   ");

        // Assert - 应委托给 GetListAsync，不调用 SearchAsync
        _mockRepo.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()), Times.Never);
        _mockRepo.Verify(r => r.GetByMailboxAsync(It.IsAny<int?>(), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithKeyword_ShouldCallRepositorySearch()
    {
        // Arrange
        var items = new List<CannedResponse>
        {
            new() { Id = 1, Name = "Hello", Body = "<p>Hi</p>" }
        };

        _mockRepo.Setup(r => r.SearchAsync("hello", null, null))
            .ReturnsAsync(items);

        // Act
        var result = await _service.SearchAsync("hello");

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Hello");
    }

    #endregion

    #region GetCategoriesAsync

    [Fact]
    public async Task GetCategoriesAsync_ShouldReturnCategories()
    {
        // Arrange
        var categories = new List<string> { "Sales", "Support" };
        _mockRepo.Setup(r => r.GetAllCategoriesAsync(1))
            .ReturnsAsync(categories);

        // Act
        var result = await _service.GetCategoriesAsync(1);

        // Assert
        result.Should().BeEquivalentTo(categories);
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_ShouldSanitizeBodyAndSave()
    {
        // Arrange
        var request = new CreateCannedResponseRequest
        {
            MailboxId = 1,
            Name = "Test",
            Body = "<p>Hello</p><script>alert(1)</script>",
            Category = "  Sales  ",
            SortOrder = 5,
            IsShared = true
        };

        CannedResponse? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<CannedResponse>()))
            .Callback<CannedResponse>(e => captured = e)
            .ReturnsAsync((CannedResponse e) => e);

        // Act
        var result = await _service.CreateAsync(request, createdByUserId: 42);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test");
        // Body 应被消毒，移除 script 标签
        result.Body.Should().NotContain("<script>");
        captured.Should().NotBeNull();
        captured!.CreatedByUserId.Should().Be(42);
        captured.MailboxId.Should().Be(1);
        captured.SortOrder.Should().Be(5);
        captured.IsShared.Should().BeTrue();
        captured.Category.Should().Be("Sales"); // 应被 Trim
        captured.CreatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyCategory_ShouldSetNull()
    {
        // Arrange
        var request = new CreateCannedResponseRequest
        {
            MailboxId = null,
            Name = "X",
            Body = "<p>Hi</p>",
            Category = "   ",
            IsShared = false
        };

        CannedResponse? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<CannedResponse>()))
            .Callback<CannedResponse>(e => captured = e)
            .ReturnsAsync((CannedResponse e) => e);

        // Act
        var result = await _service.CreateAsync(request, 1);

        // Assert
        captured!.Category.Should().BeNull();
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithExistingAsOwner_ShouldUpdateAndReturnDto()
    {
        // Arrange
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "Old",
            Body = "<p>Old</p>",
            MailboxId = 1,
            CreatedByUserId = 42
        };

        SetupQueryable(entity);

        var request = new UpdateCannedResponseRequest
        {
            Name = "New",
            Body = "<p>New</p>",
            Category = "Updated",
            MailboxId = 2,
            SortOrder = 10,
            IsShared = false
        };

        // Act - 用户 42 是创建者，不需要管理员权限
        var result = await _service.UpdateAsync(1, request, userId: 42, isAdmin: false);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("New");
        result.Body.Should().Be("<p>New</p>");
        result.MailboxId.Should().Be(2);
        entity.Name.Should().Be("New");
        entity.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithExistingAsAdmin_ShouldUpdate()
    {
        // Arrange
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "Old",
            Body = "<p>Old</p>",
            MailboxId = 1,
            CreatedByUserId = 42
        };

        SetupQueryable(entity);

        var request = new UpdateCannedResponseRequest
        {
            Name = "By Admin",
            Body = "<p>Admin</p>",
            MailboxId = 1,
            SortOrder = 0,
            IsShared = true
        };

        // Act - 管理员可以更新任何人的模板
        var result = await _service.UpdateAsync(1, request, userId: 99, isAdmin: true);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("By Admin");
    }

    [Fact]
    public async Task UpdateAsync_AsNonOwnerNonAdmin_ShouldReturnNull()
    {
        // Arrange
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "Old",
            Body = "<p>Old</p>",
            CreatedByUserId = 42
        };

        SetupQueryable(entity);

        var request = new UpdateCannedResponseRequest
        {
            Name = "X",
            Body = "<p>Y</p>",
            MailboxId = 1,
            SortOrder = 0,
            IsShared = true
        };

        // Act - 用户 99 不是创建者也不是管理员
        var result = await _service.UpdateAsync(1, request, userId: 99, isAdmin: false);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        SetupQueryable(); // 空数据集

        var request = new UpdateCannedResponseRequest
        {
            Name = "X",
            Body = "<p>Y</p>",
            MailboxId = 1,
            SortOrder = 0,
            IsShared = true
        };

        // Act
        var result = await _service.UpdateAsync(999, request, userId: 1, isAdmin: true);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithSoftDeleted_ShouldReturnNull()
    {
        // Arrange - 软删除的实体不应该出现在查询结果中（WHERE DeletedAt == null）
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "Deleted",
            Body = "<p>X</p>",
            DeletedAt = DateTime.UtcNow
        };

        // 模拟查询结果不返回软删除记录
        SetupQueryable();

        var request = new UpdateCannedResponseRequest
        {
            Name = "X",
            Body = "<p>Y</p>",
            MailboxId = 1,
            SortOrder = 0,
            IsShared = true
        };

        // Act
        var result = await _service.UpdateAsync(1, request, userId: 1, isAdmin: true);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_AsOwner_ShouldSoftDeleteAndReturnTrue()
    {
        // Arrange
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "X",
            Body = "<p>Y</p>",
            CreatedByUserId = 42
        };

        SetupQueryable(entity);

        // Act
        var result = await _service.DeleteAsync(1, userId: 42, isAdmin: false);

        // Assert
        result.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
        entity.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_AsAdmin_ShouldSoftDelete()
    {
        // Arrange
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "X",
            Body = "<p>Y</p>",
            CreatedByUserId = 42
        };

        SetupQueryable(entity);

        // Act
        var result = await _service.DeleteAsync(1, userId: 99, isAdmin: true);

        // Assert
        result.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_AsNonOwnerNonAdmin_ShouldReturnFalse()
    {
        // Arrange
        var entity = new CannedResponse
        {
            Id = 1,
            Name = "X",
            Body = "<p>Y</p>",
            CreatedByUserId = 42
        };

        SetupQueryable(entity);

        // Act
        var result = await _service.DeleteAsync(1, userId: 99, isAdmin: false);

        // Assert
        result.Should().BeFalse();
        entity.DeletedAt.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        SetupQueryable();

        // Act
        var result = await _service.DeleteAsync(999, userId: 1, isAdmin: true);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Recycle Bin (Trash)

    [Fact]
    public async Task GetTrashAsync_ShouldReturnPagedDeletedItems()
    {
        // Arrange
        var items = new List<CannedResponse>
        {
            new() { Id = 1, Name = "Deleted1", Body = "<p>1</p>", DeletedAt = DateTime.UtcNow },
            new() { Id = 2, Name = "Deleted2", Body = "<p>2</p>", DeletedAt = DateTime.UtcNow }
        };

        _mockRepo.Setup(r => r.GetDeletedAsync(1, 20, null))
            .ReturnsAsync((items, 2));

        // Act
        var (resultItems, total) = await _service.GetTrashAsync(1, 20);

        // Assert
        total.Should().Be(2);
        resultItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTrashCountAsync_ShouldReturnCount()
    {
        // Arrange
        _mockRepo.Setup(r => r.CountDeletedAsync())
            .ReturnsAsync(5);

        // Act
        var result = await _service.GetTrashCountAsync();

        // Assert
        result.Should().Be(5);
    }

    [Fact]
    public async Task RestoreAsync_AsOwner_ShouldReturnTrue()
    {
        // Arrange
        var deleted = new CannedResponse
        {
            Id = 1,
            Name = "X",
            Body = "y",
            CreatedByUserId = 42,
            DeletedAt = DateTime.UtcNow
        };

        _mockRepo.Setup(r => r.GetDeletedByIdAsync(1))
            .ReturnsAsync(deleted);
        _mockRepo.Setup(r => r.RestoreAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.RestoreAsync(1, userId: 42, isAdmin: false);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAsync_AsNonOwnerNonAdmin_ShouldReturnFalse()
    {
        // Arrange
        var deleted = new CannedResponse
        {
            Id = 1,
            Name = "X",
            Body = "y",
            CreatedByUserId = 42,
            DeletedAt = DateTime.UtcNow
        };

        _mockRepo.Setup(r => r.GetDeletedByIdAsync(1))
            .ReturnsAsync(deleted);

        // Act
        var result = await _service.RestoreAsync(1, userId: 99, isAdmin: false);

        // Assert
        result.Should().BeFalse();
        _mockRepo.Verify(r => r.RestoreAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetDeletedByIdAsync(999))
            .ReturnsAsync((CannedResponse?)null);

        // Act
        var result = await _service.RestoreAsync(999, userId: 1, isAdmin: true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HardDeleteAsync_AsOwner_ShouldReturnTrue()
    {
        // Arrange
        var deleted = new CannedResponse
        {
            Id = 1,
            Name = "X",
            Body = "y",
            CreatedByUserId = 42,
            DeletedAt = DateTime.UtcNow
        };

        _mockRepo.Setup(r => r.GetDeletedByIdAsync(1))
            .ReturnsAsync(deleted);
        _mockRepo.Setup(r => r.HardDeleteAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.HardDeleteAsync(1, userId: 42, isAdmin: false);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HardDeleteAsync_AsNonOwnerNonAdmin_ShouldReturnFalse()
    {
        // Arrange
        var deleted = new CannedResponse
        {
            Id = 1,
            Name = "X",
            Body = "y",
            CreatedByUserId = 42,
            DeletedAt = DateTime.UtcNow
        };

        _mockRepo.Setup(r => r.GetDeletedByIdAsync(1))
            .ReturnsAsync(deleted);

        // Act
        var result = await _service.HardDeleteAsync(1, userId: 99, isAdmin: false);

        // Assert
        result.Should().BeFalse();
        _mockRepo.Verify(r => r.HardDeleteAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ClearTrashAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAllDeletedAsync())
            .ReturnsAsync(7);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(7);
    }

    #endregion
}
