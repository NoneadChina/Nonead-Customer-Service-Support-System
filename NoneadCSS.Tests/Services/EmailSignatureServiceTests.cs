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
/// EmailSignatureService 单元测试
/// 覆盖邮件签名的 CRUD、默认签名管理、软删除与回收站
/// </summary>
public class EmailSignatureServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IEmailSignatureRepository> _mockRepo;
    private readonly Mock<ICurrentUserAccessor> _mockCurrentUserAccessor;
    private readonly EmailSignatureService _service;

    public EmailSignatureServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockRepo = new Mock<IEmailSignatureRepository>();
        _mockCurrentUserAccessor = new Mock<ICurrentUserAccessor>();

        _mockUnitOfWork.Setup(u => u.EmailSignatures).Returns(_mockRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _mockCurrentUserAccessor.Setup(x => x.CanAccessMailboxAsync(It.IsAny<int?>()))
            .ReturnsAsync(true);
        _mockCurrentUserAccessor.Setup(x => x.IsSuperAdmin).Returns(true);

        _service = new EmailSignatureService(_mockUnitOfWork.Object, _mockCurrentUserAccessor.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock EmailSignature DbSet
    /// </summary>
    private void SetupQueryable(params EmailSignature[] items)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(items.ToList());
        _mockRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    #region GetPagedAsync

    [Fact]
    public async Task GetPagedAsync_ShouldReturnPagedDtos()
    {
        // Arrange
        var items = new List<EmailSignature>
        {
            new() { Id = 1, Name = "Sig1", Body = "<p>1</p>", UserId = 1, IsDefault = true },
            new() { Id = 2, Name = "Sig2", Body = "<p>2</p>", UserId = 2, IsDefault = false }
        };

        _mockRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null, null))
            .ReturnsAsync((items, 2));

        var query = new EmailSignatureQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(2);
        resultItems.Should().HaveCount(2);
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WithExisting_ShouldReturnDto()
    {
        // Arrange
        var entity = new EmailSignature
        {
            Id = 1,
            Name = "Sig",
            Body = "<p>Hi</p>",
            UserId = 1
        };

        _mockRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(entity);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Sig");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((EmailSignature?)null);

        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithSoftDeleted_ShouldReturnNull()
    {
        // Arrange
        var entity = new EmailSignature
        {
            Id = 1,
            Name = "Deleted",
            Body = "<p>X</p>",
            UserId = 1,
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

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_WithIsDefaultTrue_ShouldCreateAndSetDefault()
    {
        // Arrange
        var request = new CreateEmailSignatureRequest
        {
            Name = "New Sig",
            Body = "<p>Hello</p>",
            MailboxId = 5,
            IsDefault = true,
            SortOrder = 3
        };

        EmailSignature? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<EmailSignature>()))
            .Callback<EmailSignature>(e => captured = e)
            .ReturnsAsync((EmailSignature e) => e);

        // Act
        var result = await _service.CreateAsync(userId: 42, request);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("New Sig");
        result.Body.Should().Be("<p>Hello</p>");
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(42);
        captured.MailboxId.Should().Be(5);
        captured.IsDefault.Should().BeTrue();
        captured.SortOrder.Should().Be(3);
        captured.CreatedAt.Should().NotBe(default);
        // M6: 仓储不再内部保存，CreateAsync 内 AddAsync 后 + SetDefaultAsync 后各一次 UoW 保存
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Exactly(2));
        _mockRepo.Verify(r => r.SetDefaultAsync(42, 5, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithIsDefaultFalse_ShouldNotCallSetDefault()
    {
        // Arrange
        var request = new CreateEmailSignatureRequest
        {
            Name = "New Sig",
            Body = "<p>Hello</p>",
            MailboxId = null,
            IsDefault = false
        };

        _mockRepo.Setup(r => r.AddAsync(It.IsAny<EmailSignature>()))
            .ReturnsAsync((EmailSignature e) => e);

        // Act
        var result = await _service.CreateAsync(userId: 42, request);

        // Assert
        result.Should().NotBeNull();
        _mockRepo.Verify(r => r.SetDefaultAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldSanitizeBody()
    {
        // Arrange
        var request = new CreateEmailSignatureRequest
        {
            Name = "X",
            Body = "<p>Hi</p><script>alert(1)</script>",
            IsDefault = false
        };

        EmailSignature? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<EmailSignature>()))
            .Callback<EmailSignature>(e => captured = e)
            .ReturnsAsync((EmailSignature e) => e);

        // Act
        var result = await _service.CreateAsync(1, request);

        // Assert
        captured!.Body.Should().NotContain("<script>");
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithExisting_ShouldUpdateAndReturnDto()
    {
        // Arrange
        var entity = new EmailSignature
        {
            Id = 1,
            Name = "Old",
            Body = "<p>Old</p>",
            UserId = 42,
            MailboxId = 5
        };

        SetupQueryable(entity);

        var request = new UpdateEmailSignatureRequest
        {
            Name = "New",
            Body = "<p>New</p>",
            MailboxId = 6,
            IsDefault = false,
            SortOrder = 10
        };

        // Act
        var result = await _service.UpdateAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("New");
        result.MailboxId.Should().Be(6);
        entity.Name.Should().Be("New");
        entity.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithIsDefaultTrue_ShouldCallSetDefault()
    {
        // Arrange
        var entity = new EmailSignature
        {
            Id = 1,
            Name = "Old",
            Body = "<p>Old</p>",
            UserId = 42,
            MailboxId = 5
        };

        SetupQueryable(entity);

        var request = new UpdateEmailSignatureRequest
        {
            Name = "New",
            Body = "<p>New</p>",
            MailboxId = 5,
            IsDefault = true,
            SortOrder = 0
        };

        // Act
        var result = await _service.UpdateAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        _mockRepo.Verify(r => r.SetDefaultAsync(42, 5, 1), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        SetupQueryable();

        var request = new UpdateEmailSignatureRequest
        {
            Name = "X",
            Body = "<p>Y</p>",
            MailboxId = 1,
            IsDefault = false,
            SortOrder = 0
        };

        // Act
        var result = await _service.UpdateAsync(999, request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithSoftDeleted_ShouldReturnNull()
    {
        // Arrange - 软删除的记录不应该出现在查询结果中
        SetupQueryable();

        var request = new UpdateEmailSignatureRequest
        {
            Name = "X",
            Body = "<p>Y</p>",
            MailboxId = 1,
            IsDefault = false,
            SortOrder = 0
        };

        // Act
        var result = await _service.UpdateAsync(1, request);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithExisting_ShouldSoftDeleteAndUnsetIfDefault()
    {
        // Arrange
        var entity = new EmailSignature
        {
            Id = 1,
            Name = "X",
            Body = "<p>Y</p>",
            UserId = 1,
            IsDefault = true
        };

        SetupQueryable(entity);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
        entity.UpdatedAt.Should().NotBe(default);
        entity.IsDefault.Should().BeFalse(); // 应取消默认状态
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        SetupQueryable();

        // Act
        var result = await _service.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_WithNonDefaultSignature_ShouldNotChangeDefault()
    {
        // Arrange
        var entity = new EmailSignature
        {
            Id = 1,
            Name = "X",
            Body = "<p>Y</p>",
            UserId = 1,
            IsDefault = false
        };

        SetupQueryable(entity);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        entity.IsDefault.Should().BeFalse();
    }

    #endregion

    #region GetOverviewAsync

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnCorrectStats()
    {
        // Arrange
        var items = new List<EmailSignature>
        {
            new() { Id = 1, Name = "Default", Body = "<p>1</p>", IsDefault = true, MailboxId = 1 },
            new() { Id = 2, Name = "Default2", Body = "<p>2</p>", IsDefault = true, MailboxId = null },
            new() { Id = 3, Name = "Regular", Body = "<p>3</p>", IsDefault = false, MailboxId = 2 }
        };

        _mockRepo.Setup(r => r.GetPagedAsync(1, 100, null, null, null, null))
            .ReturnsAsync((items, 3));

        var topItems = items.Where(s => s.IsDefault).Take(5).ToList();
        _mockRepo.Setup(r => r.GetPagedAsync(1, 5, null, null, true, null))
            .ReturnsAsync((topItems, 2));

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalSignatures.Should().Be(3);
        result.DefaultSignatures.Should().Be(2);
        result.MailboxSignatures.Should().Be(2); // MailboxId 非 null 的有 2 个
        result.GlobalSignatures.Should().Be(1); // MailboxId 为 null 的有 1 个
        result.TopSignatures.Should().HaveCount(2);
    }

    #endregion

    #region GetByUserIdAsync

    [Fact]
    public async Task GetByUserIdAsync_ShouldReturnUserSignatures()
    {
        // Arrange
        var items = new List<EmailSignature>
        {
            new() { Id = 1, Name = "A", Body = "<p>1</p>", UserId = 42 },
            new() { Id = 2, Name = "B", Body = "<p>2</p>", UserId = 42 }
        };

        _mockRepo.Setup(r => r.GetByUserIdAndMailboxIdAsync(42, null))
            .ReturnsAsync(items);

        // Act
        var result = await _service.GetByUserIdAsync(42);

        // Assert
        result.Should().HaveCount(2);
    }

    #endregion

    #region GetDefaultAsync

    [Fact]
    public async Task GetDefaultAsync_WithExistingDefault_ShouldReturnDto()
    {
        // Arrange
        var sig = new EmailSignature
        {
            Id = 1,
            Name = "Default",
            Body = "<p>X</p>",
            UserId = 42,
            IsDefault = true
        };

        _mockRepo.Setup(r => r.GetDefaultByUserIdAsync(42, null))
            .ReturnsAsync(sig);

        // Act
        var result = await _service.GetDefaultAsync(42);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Default");
    }

    [Fact]
    public async Task GetDefaultAsync_WithNoDefault_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetDefaultByUserIdAsync(99, null))
            .ReturnsAsync((EmailSignature?)null);

        // Act
        var result = await _service.GetDefaultAsync(99);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region SetDefaultAsync

    [Fact]
    public async Task SetDefaultAsync_WithExistingAndOwner_ShouldSetDefaultAndReturnTrue()
    {
        // Arrange
        var sig = new EmailSignature
        {
            Id = 1,
            Name = "X",
            Body = "<p>Y</p>",
            UserId = 42,
            MailboxId = 5
        };

        _mockRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(sig);

        // Act
        var result = await _service.SetDefaultAsync(userId: 42, id: 1);

        // Assert
        result.Should().BeTrue();
        _mockRepo.Verify(r => r.SetDefaultAsync(42, 5, 1), Times.Once);
    }

    [Fact]
    public async Task SetDefaultAsync_WithNonExistingSignature_ShouldReturnFalse()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((EmailSignature?)null);

        // Act
        var result = await _service.SetDefaultAsync(42, 999);

        // Assert
        result.Should().BeFalse();
        _mockRepo.Verify(r => r.SetDefaultAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SetDefaultAsync_WithDifferentOwner_ShouldReturnFalse()
    {
        // Arrange
        var sig = new EmailSignature
        {
            Id = 1,
            Name = "X",
            Body = "<p>Y</p>",
            UserId = 42 // 不是 99
        };

        _mockRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(sig);

        // Act
        var result = await _service.SetDefaultAsync(userId: 99, id: 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetDefaultAsync_WithSoftDeletedSignature_ShouldReturnFalse()
    {
        // Arrange
        var sig = new EmailSignature
        {
            Id = 1,
            Name = "X",
            Body = "<p>Y</p>",
            UserId = 42,
            DeletedAt = DateTime.UtcNow
        };

        _mockRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(sig);

        // Act
        var result = await _service.SetDefaultAsync(42, 1);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Recycle Bin (Trash)

    [Fact]
    public async Task GetTrashAsync_ShouldReturnPagedDeletedItems()
    {
        // Arrange
        var items = new List<EmailSignature>
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
            .ReturnsAsync(3);

        // Act
        var result = await _service.GetTrashCountAsync();

        // Assert
        result.Should().Be(3);
    }

    [Fact]
    public async Task RestoreAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.RestoreAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.RestoreAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HardDeleteAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.HardDeleteAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ClearTrashAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAllDeletedAsync())
            .ReturnsAsync(4);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(4);
    }

    #endregion
}
