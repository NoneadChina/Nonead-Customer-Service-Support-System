using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Security;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ApiKeyService 单元测试
/// 覆盖 API Key 的 CRUD、软删除、回收站、密钥重新生成、状态切换与日志查询
/// </summary>
public class ApiKeyServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IApiKeyRepository> _mockRepo;
    private readonly Mock<IApiKeyLogRepository> _mockLogRepo;
    private readonly ApiKeyService _service;

    static ApiKeyServiceTests()
    {
        // SecretHasher 是全局静态，需初始化后才能在 CreateAsync / RegenerateKeyAsync 中调用 Hash
        SecretHasher.Initialize("TestSecretKeyForSecretHasherInitialization12345678");
    }

    public ApiKeyServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockRepo = new Mock<IApiKeyRepository>();
        _mockLogRepo = new Mock<IApiKeyLogRepository>();

        _mockUnitOfWork.Setup(u => u.ApiKeys).Returns(_mockRepo.Object);
        _mockUnitOfWork.Setup(u => u.ApiKeyLogs).Returns(_mockLogRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        _service = new ApiKeyService(_mockUnitOfWork.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock ApiKey DbSet
    /// </summary>
    private void SetupQueryable(params ApiKey[] items)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(items.ToList());
        _mockRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    #region GetOverviewAsync

    [Fact]
    public async Task GetOverviewAsync_ShouldReturnCorrectOverview()
    {
        // Arrange
        var stats = new Dictionary<string, int>
        {
            ["total"] = 10,
            ["active"] = 7,
            ["inactive"] = 3,
            ["totalUsage"] = 500
        };
        _mockRepo.Setup(r => r.GetStatsAsync()).ReturnsAsync(stats);

        var recentKeys = new List<ApiKey>
        {
            new() { Id = 1, Name = "Key1", Key = "hashedkey1" },
            new() { Id = 2, Name = "Key2", Key = "hashedkey2" }
        };
        _mockRepo.Setup(r => r.GetPagedAsync(1, 5, null, null, null, null))
            .ReturnsAsync((recentKeys, 2));

        var logs = new List<ApiKeyLog>
        {
            new() { Id = 1, ApiKeyId = 1, Endpoint = "/api/test", Method = "GET", StatusCode = 200, DurationMs = 50 }
        };
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 10, null, null, null))
            .ReturnsAsync((logs, 1));

        var scopesCount = new Dictionary<string, int> { ["read"] = 5, ["write"] = 3 };
        _mockRepo.Setup(r => r.GetScopeCountsAsync()).ReturnsAsync(scopesCount);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalKeys.Should().Be(10);
        result.ActiveKeys.Should().Be(7);
        result.InactiveKeys.Should().Be(3);
        result.TotalUsage.Should().Be(500);
        result.RecentKeys.Should().HaveCount(2);
        result.RecentLogs.Should().HaveCount(1);
        result.ScopesCount.Should().HaveCount(2);
        result.ScopesCount["read"].Should().Be(5);
        result.ScopesCount["write"].Should().Be(3);
    }

    [Fact]
    public async Task GetOverviewAsync_WithEmptyData_ShouldReturnZeros()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetStatsAsync())
            .ReturnsAsync(new Dictionary<string, int>());
        _mockRepo.Setup(r => r.GetPagedAsync(1, 5, null, null, null, null))
            .ReturnsAsync((new List<ApiKey>(), 0));
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 10, null, null, null))
            .ReturnsAsync((new List<ApiKeyLog>(), 0));
        _mockRepo.Setup(r => r.GetScopeCountsAsync())
            .ReturnsAsync(new Dictionary<string, int>());

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.TotalKeys.Should().Be(0);
        result.ActiveKeys.Should().Be(0);
        result.InactiveKeys.Should().Be(0);
        result.TotalUsage.Should().Be(0);
        result.RecentKeys.Should().BeEmpty();
        result.RecentLogs.Should().BeEmpty();
        result.ScopesCount.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_WithMissingStatsKeys_ShouldUseDefaults()
    {
        // Arrange - 统计字典缺少某些 key，GetValueOrDefault 应返回 0
        _mockRepo.Setup(r => r.GetStatsAsync())
            .ReturnsAsync(new Dictionary<string, int> { ["total"] = 5 });
        _mockRepo.Setup(r => r.GetPagedAsync(1, 5, null, null, null, null))
            .ReturnsAsync((new List<ApiKey>(), 0));
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 10, null, null, null))
            .ReturnsAsync((new List<ApiKeyLog>(), 0));
        _mockRepo.Setup(r => r.GetScopeCountsAsync())
            .ReturnsAsync(new Dictionary<string, int>());

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.TotalKeys.Should().Be(5);
        result.ActiveKeys.Should().Be(0);
        result.InactiveKeys.Should().Be(0);
        result.TotalUsage.Should().Be(0);
    }

    #endregion

    #region GetPagedAsync

    [Fact]
    public async Task GetPagedAsync_ShouldReturnPagedDtos()
    {
        // Arrange
        var items = new List<ApiKey>
        {
            new() { Id = 1, Name = "Key1", Key = "hashedkey1" },
            new() { Id = 2, Name = "Key2", Key = "hashedkey2" }
        };
        _mockRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null, null))
            .ReturnsAsync((items, 2));

        var query = new ApiKeyQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(2);
        resultItems.Should().HaveCount(2);
        resultItems[0].Name.Should().Be("Key1");
        resultItems[1].Name.Should().Be("Key2");
    }

    [Fact]
    public async Task GetPagedAsync_WithFilters_ShouldPassFiltersToRepository()
    {
        // Arrange
        var items = new List<ApiKey>
        {
            new() { Id = 1, Name = "Filtered", Key = "hashedkey", UserId = 5 }
        };
        _mockRepo.Setup(r => r.GetPagedAsync(2, 10, 5, true, "search", "read"))
            .ReturnsAsync((items, 1));

        var query = new ApiKeyQuery
        {
            Page = 2,
            PageSize = 10,
            UserId = 5,
            IsActive = true,
            Keyword = "search",
            Scope = "read"
        };

        // Act
        var (resultItems, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(1);
        resultItems.Should().HaveCount(1);
        _mockRepo.Verify(r => r.GetPagedAsync(2, 10, 5, true, "search", "read"), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_WithEmptyResult_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null, null))
            .ReturnsAsync((new List<ApiKey>(), 0));

        var query = new ApiKeyQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(0);
        resultItems.Should().BeEmpty();
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WithExisting_ShouldReturnDto()
    {
        // Arrange
        var entity = new ApiKey
        {
            Id = 1,
            Name = "Test Key",
            Key = "hashedkey123",
            Description = "Test description",
            UserId = 1,
            Scopes = "read,write",
            IsActive = true,
            UsageCount = 42
        };
        _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(entity);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Test Key");
        result.Description.Should().Be("Test description");
        result.Scopes.Should().Equal(new[] { "read", "write" });
        result.IsActive.Should().BeTrue();
        result.UsageCount.Should().Be(42);
        result.Key.Should().BeNull(); // 不包含完整密钥
        result.KeyPrefix.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ApiKey?)null);

        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_WithValidRequest_ShouldCreateAndReturnDtoWithPlaintextKey()
    {
        // Arrange
        var request = new CreateApiKeyRequest
        {
            Name = "New Key",
            Description = "A new key",
            UserId = 42,
            Scopes = new List<string> { "read", "write" },
            IsActive = true
        };

        ApiKey? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<ApiKey>()))
            .Callback<ApiKey>(e => { captured = e; e.Id = 1; })
            .ReturnsAsync((ApiKey e) => e);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(1);
        result.Name.Should().Be("New Key");
        result.Description.Should().Be("A new key");
        result.UserId.Should().Be(42);
        result.IsActive.Should().BeTrue();
        result.Scopes.Should().Equal(new[] { "read", "write" });
        result.Key.Should().NotBeNullOrEmpty();
        result.Key.Should().StartWith("fsk_"); // 返回明文密钥

        captured.Should().NotBeNull();
        captured!.Key.Should().NotBe(result.Key); // 数据库存储的是哈希，不是明文
        captured.Key.Should().NotStartWith("fsk_"); // 哈希值没有 fsk_ 前缀
        captured.Name.Should().Be("New Key");
        captured.UserId.Should().Be(42);
        captured.Scopes.Should().Be("read,write");
        captured.IsActive.Should().BeTrue();
        captured.CreatedAt.Should().NotBe(default);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEmptyScopes_ShouldSetEmptyScopesString()
    {
        // Arrange
        var request = new CreateApiKeyRequest
        {
            Name = "Key",
            Scopes = new List<string>()
        };

        ApiKey? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<ApiKey>()))
            .Callback<ApiKey>(e => { captured = e; e.Id = 1; })
            .ReturnsAsync((ApiKey e) => e);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        captured.Should().NotBeNull();
        captured!.Scopes.Should().BeEmpty();
        result.Scopes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithNullScopes_ShouldSetEmptyScopesString()
    {
        // Arrange
        var request = new CreateApiKeyRequest
        {
            Name = "Key",
            Scopes = null!
        };

        ApiKey? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<ApiKey>()))
            .Callback<ApiKey>(e => { captured = e; e.Id = 1; })
            .ReturnsAsync((ApiKey e) => e);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        captured.Should().NotBeNull();
        captured!.Scopes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WithExpiryDate_ShouldSetExpiresAt()
    {
        // Arrange
        var expiresAt = DateTime.UtcNow.AddDays(30);
        var request = new CreateApiKeyRequest
        {
            Name = "Temp Key",
            ExpiresAt = expiresAt,
            IsActive = true
        };

        ApiKey? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<ApiKey>()))
            .Callback<ApiKey>(e => { captured = e; e.Id = 1; })
            .ReturnsAsync((ApiKey e) => e);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.ExpiresAt.Should().Be(expiresAt);
        captured!.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task CreateAsync_WithInactiveFlag_ShouldCreateInactiveKey()
    {
        // Arrange
        var request = new CreateApiKeyRequest
        {
            Name = "Inactive Key",
            IsActive = false
        };

        ApiKey? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<ApiKey>()))
            .Callback<ApiKey>(e => { captured = e; e.Id = 1; })
            .ReturnsAsync((ApiKey e) => e);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.IsActive.Should().BeFalse();
        captured!.IsActive.Should().BeFalse();
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithExisting_ShouldUpdateAndReturnDto()
    {
        // Arrange
        var entity = new ApiKey
        {
            Id = 1,
            Name = "Old Name",
            Key = "hashedkey",
            Description = "Old desc",
            Scopes = "read",
            IsActive = true
        };
        SetupQueryable(entity);

        var request = new UpdateApiKeyRequest
        {
            Id = 1,
            Name = "New Name",
            Description = "New desc",
            Scopes = new List<string> { "read", "write" },
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            IsActive = false
        };

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("New Name");
        result.Description.Should().Be("New desc");
        result.Scopes.Should().Equal(new[] { "read", "write" });
        result.IsActive.Should().BeFalse();

        entity.Name.Should().Be("New Name");
        entity.Description.Should().Be("New desc");
        entity.Scopes.Should().Be("read,write");
        entity.IsActive.Should().BeFalse();
        entity.UpdatedAt.Should().NotBe(default);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        SetupQueryable();

        var request = new UpdateApiKeyRequest
        {
            Id = 999,
            Name = "X"
        };

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithEmptyScopes_ShouldSetEmptyScopesString()
    {
        // Arrange
        var entity = new ApiKey
        {
            Id = 1,
            Name = "Old",
            Scopes = "read,write"
        };
        SetupQueryable(entity);

        var request = new UpdateApiKeyRequest
        {
            Id = 1,
            Name = "Updated",
            Scopes = new List<string>()
        };

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().NotBeNull();
        entity.Scopes.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WithNullScopes_ShouldSetEmptyScopesString()
    {
        // Arrange
        var entity = new ApiKey
        {
            Id = 1,
            Name = "Old",
            Scopes = "read"
        };
        SetupQueryable(entity);

        var request = new UpdateApiKeyRequest
        {
            Id = 1,
            Name = "Updated",
            Scopes = null!
        };

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().NotBeNull();
        entity.Scopes.Should().BeEmpty();
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithExisting_ShouldSoftDeleteAndReturnTrue()
    {
        // Arrange
        var entity = new ApiKey { Id = 1, Name = "Key" };
        SetupQueryable(entity);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
        entity.UpdatedAt.Should().NotBe(default);
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
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region GetTrashPagedAsync

    [Fact]
    public async Task GetTrashPagedAsync_ShouldReturnPagedDeletedItems()
    {
        // Arrange
        var items = new List<ApiKey>
        {
            new() { Id = 1, Name = "Deleted1", DeletedAt = DateTime.UtcNow },
            new() { Id = 2, Name = "Deleted2", DeletedAt = DateTime.UtcNow }
        };
        _mockRepo.Setup(r => r.GetDeletedPagedAsync(1, 10, null))
            .ReturnsAsync((items, 2));

        // Act
        var (resultItems, total) = await _service.GetTrashPagedAsync(1, 10, null);

        // Assert
        total.Should().Be(2);
        resultItems.Should().HaveCount(2);
        resultItems[0].Name.Should().Be("Deleted1");
    }

    [Fact]
    public async Task GetTrashPagedAsync_WithKeyword_ShouldPassKeywordToRepository()
    {
        // Arrange
        var items = new List<ApiKey>
        {
            new() { Id = 1, Name = "Match", DeletedAt = DateTime.UtcNow }
        };
        _mockRepo.Setup(r => r.GetDeletedPagedAsync(1, 10, "match"))
            .ReturnsAsync((items, 1));

        // Act
        var (resultItems, total) = await _service.GetTrashPagedAsync(1, 10, "match");

        // Assert
        total.Should().Be(1);
        resultItems.Should().HaveCount(1);
        _mockRepo.Verify(r => r.GetDeletedPagedAsync(1, 10, "match"), Times.Once);
    }

    [Fact]
    public async Task GetTrashPagedAsync_WithEmptyTrash_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetDeletedPagedAsync(1, 10, null))
            .ReturnsAsync((new List<ApiKey>(), 0));

        // Act
        var (resultItems, total) = await _service.GetTrashPagedAsync(1, 10, null);

        // Assert
        total.Should().Be(0);
        resultItems.Should().BeEmpty();
    }

    #endregion

    #region RestoreAsync

    [Fact]
    public async Task RestoreAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetDeletedByIdAsync(999))
            .ReturnsAsync((ApiKey?)null);

        // Act
        var result = await _service.RestoreAsync(999);

        // Assert
        result.Should().BeNull();
        _mockRepo.Verify(r => r.RestoreAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_WithExisting_ShouldRestoreAndReturnDto()
    {
        // Arrange
        var deletedKey = new ApiKey
        {
            Id = 1,
            Name = "Deleted",
            Key = "hashedkey",
            DeletedAt = DateTime.UtcNow
        };
        _mockRepo.Setup(r => r.GetDeletedByIdAsync(1)).ReturnsAsync(deletedKey);
        _mockRepo.Setup(r => r.RestoreAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

        var restoredKey = new ApiKey
        {
            Id = 1,
            Name = "Deleted",
            Key = "hashedkey",
            IsActive = true,
            User = new User { FirstName = "John", LastName = "Doe" }
        };
        SetupQueryable(restoredKey);

        // Act
        var result = await _service.RestoreAsync(1);

        // Assert
        // 注意: 此测试依赖 Include + FirstOrDefaultAsync 的异步查询支持
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Deleted");
        result.UserName.Should().Be("John Doe");

        _mockRepo.Verify(r => r.GetDeletedByIdAsync(1), Times.Once);
        _mockRepo.Verify(r => r.RestoreAsync(1), Times.Once);
    }

    #endregion

    #region HardDeleteAsync

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
    public async Task HardDeleteAsync_WhenNotFound_ShouldReturnFalse()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAsync(999)).ReturnsAsync(false);

        // Act
        var result = await _service.HardDeleteAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockRepo.Verify(r => r.HardDeleteAsync(999), Times.Once);
    }

    #endregion

    #region ClearTrashAsync

    [Fact]
    public async Task ClearTrashAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAllDeletedAsync()).ReturnsAsync(5);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(5);
        _mockRepo.Verify(r => r.HardDeleteAllDeletedAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearTrashAsync_WithNoDeletedItems_ShouldReturnZero()
    {
        // Arrange
        _mockRepo.Setup(r => r.HardDeleteAllDeletedAsync()).ReturnsAsync(0);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region ToggleActiveAsync

    [Fact]
    public async Task ToggleActiveAsync_WhenActive_ShouldDeactivateAndReturnDto()
    {
        // Arrange
        var entity = new ApiKey { Id = 1, Name = "Key", IsActive = true };
        SetupQueryable(entity);

        // Act
        var result = await _service.ToggleActiveAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.IsActive.Should().BeFalse();
        entity.IsActive.Should().BeFalse();
        entity.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ToggleActiveAsync_WhenInactive_ShouldActivateAndReturnDto()
    {
        // Arrange
        var entity = new ApiKey { Id = 1, Name = "Key", IsActive = false };
        SetupQueryable(entity);

        // Act
        var result = await _service.ToggleActiveAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.IsActive.Should().BeTrue();
        entity.IsActive.Should().BeTrue();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ToggleActiveAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        SetupQueryable();

        // Act
        var result = await _service.ToggleActiveAsync(999);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region RegenerateKeyAsync

    [Fact]
    public async Task RegenerateKeyAsync_WithExisting_ShouldRegenerateAndReturnDtoWithPlaintextKey()
    {
        // Arrange
        var entity = new ApiKey
        {
            Id = 1,
            Name = "Key",
            Key = "oldhashedkey",
            IsActive = true
        };
        SetupQueryable(entity);
        var oldHash = entity.Key;

        // Act
        var result = await _service.RegenerateKeyAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Key.Should().NotBeNullOrEmpty();
        result.Key.Should().StartWith("fsk_"); // 返回明文密钥

        entity.Key.Should().NotBe(oldHash); // 哈希已更新
        entity.Key.Should().NotBe(result.Key); // 存储的是哈希，不是明文
        entity.Key.Should().NotStartWith("fsk_"); // 哈希值没有 fsk_ 前缀
        entity.UpdatedAt.Should().NotBe(default);

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task RegenerateKeyAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        SetupQueryable();

        // Act
        var result = await _service.RegenerateKeyAsync(999);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region GetLogsAsync

    [Fact]
    public async Task GetLogsAsync_ShouldReturnLogsForApiKey()
    {
        // Arrange
        var logs = new List<ApiKeyLog>
        {
            new() { Id = 1, ApiKeyId = 1, Endpoint = "/api/test", Method = "GET", StatusCode = 200, DurationMs = 50 },
            new() { Id = 2, ApiKeyId = 1, Endpoint = "/api/data", Method = "POST", StatusCode = 201, DurationMs = 100 }
        };
        _mockLogRepo.Setup(r => r.GetByApiKeyAsync(1, 50)).ReturnsAsync(logs);

        // Act
        var result = await _service.GetLogsAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[0].Endpoint.Should().Be("/api/test");
        result[0].StatusCode.Should().Be(200);
        result[1].Endpoint.Should().Be("/api/data");
        result[1].Method.Should().Be("POST");
    }

    [Fact]
    public async Task GetLogsAsync_WithCustomLimit_ShouldPassLimitToRepository()
    {
        // Arrange
        _mockLogRepo.Setup(r => r.GetByApiKeyAsync(1, 10)).ReturnsAsync(new List<ApiKeyLog>());

        // Act
        var result = await _service.GetLogsAsync(1, 10);

        // Assert
        result.Should().BeEmpty();
        _mockLogRepo.Verify(r => r.GetByApiKeyAsync(1, 10), Times.Once);
    }

    [Fact]
    public async Task GetLogsAsync_WithApiKeyNavigation_ShouldMapApiKeyName()
    {
        // Arrange
        var apiKey = new ApiKey { Id = 1, Name = "TestKey" };
        var logs = new List<ApiKeyLog>
        {
            new() { Id = 1, ApiKeyId = 1, Endpoint = "/api/test", StatusCode = 200, ApiKey = apiKey }
        };
        _mockLogRepo.Setup(r => r.GetByApiKeyAsync(1, 50)).ReturnsAsync(logs);

        // Act
        var result = await _service.GetLogsAsync(1);

        // Assert
        result.Should().HaveCount(1);
        result[0].ApiKeyName.Should().Be("TestKey");
    }

    #endregion

    #region GetLogsPagedAsync

    [Fact]
    public async Task GetLogsPagedAsync_ShouldReturnPagedLogs()
    {
        // Arrange
        var logs = new List<ApiKeyLog>
        {
            new() { Id = 1, ApiKeyId = 1, Endpoint = "/api/test", StatusCode = 200 }
        };
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null))
            .ReturnsAsync((logs, 1));

        var query = new ApiKeyLogQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, total) = await _service.GetLogsPagedAsync(query);

        // Assert
        total.Should().Be(1);
        resultItems.Should().HaveCount(1);
        resultItems[0].Endpoint.Should().Be("/api/test");
    }

    [Fact]
    public async Task GetLogsPagedAsync_WithFilters_ShouldPassFiltersToRepository()
    {
        // Arrange
        var logs = new List<ApiKeyLog>
        {
            new() { Id = 1, ApiKeyId = 5, Endpoint = "/api/data", StatusCode = 404 }
        };
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 20, 5, "/api/data", 404))
            .ReturnsAsync((logs, 1));

        var query = new ApiKeyLogQuery
        {
            Page = 1,
            PageSize = 20,
            ApiKeyId = 5,
            Endpoint = "/api/data",
            StatusCode = 404
        };

        // Act
        var (resultItems, total) = await _service.GetLogsPagedAsync(query);

        // Assert
        total.Should().Be(1);
        _mockLogRepo.Verify(r => r.GetPagedAsync(1, 20, 5, "/api/data", 404), Times.Once);
    }

    [Fact]
    public async Task GetLogsPagedAsync_WithEmptyResult_ShouldReturnEmptyList()
    {
        // Arrange
        _mockLogRepo.Setup(r => r.GetPagedAsync(1, 20, null, null, null))
            .ReturnsAsync((new List<ApiKeyLog>(), 0));

        var query = new ApiKeyLogQuery { Page = 1, PageSize = 20 };

        // Act
        var (resultItems, total) = await _service.GetLogsPagedAsync(query);

        // Assert
        total.Should().Be(0);
        resultItems.Should().BeEmpty();
    }

    #endregion
}
