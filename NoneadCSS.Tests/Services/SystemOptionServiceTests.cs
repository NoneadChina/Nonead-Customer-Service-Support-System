using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// SystemOptionService 单元测试
/// 覆盖系统配置的 CRUD、缓存、密码策略缓存联动、邮箱收取频率更新等场景
/// </summary>
public class SystemOptionServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ISystemOptionRepository> _mockOptionRepo;
    private readonly Mock<IMailboxRepository> _mockMailboxRepo;
    private readonly Mock<IPasswordPolicyService> _mockPasswordPolicyService;
    private readonly Mock<ILogger<SystemOptionService>> _mockLogger;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;
    private readonly IMemoryCache _cache;
    private readonly SystemOptionService _service;

    public SystemOptionServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockOptionRepo = new Mock<ISystemOptionRepository>();
        _mockMailboxRepo = new Mock<IMailboxRepository>();
        _mockPasswordPolicyService = new Mock<IPasswordPolicyService>();
        _mockLogger = new Mock<ILogger<SystemOptionService>>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockOptionRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _service = new SystemOptionService(
            _mockUnitOfWork.Object,
            _mockPasswordPolicyService.Object,
            _mockLogger.Object,
            _cache,
            Options.Create(new NoneadCSS.Application.Configuration.CacheOptions()));
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock SystemOption DbSet（用于 BatchUpdateAsync 的批量查询）
    /// </summary>
    private void SetupOptionQueryable(params SystemOption[] options)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(options.ToList());
        _mockOptionRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_WithOptions_ShouldReturnAllOptions()
    {
        // Arrange
        var options = new List<SystemOption>
        {
            new() { Id = 1, Name = "app.name", Value = "NoneadCSS", Category = "general" },
            new() { Id = 2, Name = "app.url", Value = "https://example.com", Category = "general" }
        };

        _mockOptionRepo.Setup(r => r.GetAllWithOptionsAsync())
            .ReturnsAsync(options);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("app.name");
        result[0].Value.Should().Be("NoneadCSS");
        result[1].Name.Should().Be("app.url");
    }

    [Fact]
    public async Task GetAllAsync_ShouldCacheResult()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetAllWithOptionsAsync())
            .ReturnsAsync(new List<SystemOption>());

        // Act
        await _service.GetAllAsync();
        await _service.GetAllAsync();
        await _service.GetAllAsync();

        // Assert - 只有第一次调用查询数据库
        _mockOptionRepo.Verify(r => r.GetAllWithOptionsAsync(), Times.Once);
    }

    #endregion

    #region GetByCategoryAsync

    [Fact]
    public async Task GetByCategoryAsync_ShouldReturnOptionsInCategory()
    {
        // Arrange
        var options = new List<SystemOption>
        {
            new() { Id = 1, Name = "security.password_min_length", Value = "12", Category = "security" }
        };

        _mockOptionRepo.Setup(r => r.GetByCategoryAsync("security"))
            .ReturnsAsync(options);

        // Act
        var result = await _service.GetByCategoryAsync("security");

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("security.password_min_length");
        result[0].Value.Should().Be("12");
    }

    [Fact]
    public async Task GetByCategoryAsync_ShouldCacheResult()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetByCategoryAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<SystemOption>());

        // Act
        await _service.GetByCategoryAsync("security");
        await _service.GetByCategoryAsync("security");

        // Assert
        _mockOptionRepo.Verify(r => r.GetByCategoryAsync("security"), Times.Once);
    }

    #endregion

    #region GetByNameAsync

    [Fact]
    public async Task GetByNameAsync_WithExistingOption_ShouldReturnOption()
    {
        // Arrange
        var option = new SystemOption { Id = 1, Name = "app.name", Value = "TestApp", Category = "general" };

        _mockOptionRepo.Setup(r => r.GetByNameAsync("app.name"))
            .ReturnsAsync(option);

        // Act
        var result = await _service.GetByNameAsync("app.name");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("app.name");
        result.Value.Should().Be("TestApp");
    }

    [Fact]
    public async Task GetByNameAsync_WithNonExistingOption_ShouldReturnNull()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetByNameAsync("non.existent"))
            .ReturnsAsync((SystemOption?)null);

        // Act
        var result = await _service.GetByNameAsync("non.existent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetValueAsync

    [Fact]
    public async Task GetValueAsync_WithExistingOption_ShouldReturnValue()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetValueAsync("app.name"))
            .ReturnsAsync("TestApp");

        // Act
        var result = await _service.GetValueAsync("app.name");

        // Assert
        result.Should().Be("TestApp");
    }

    [Fact]
    public async Task GetValueAsync_WithNonExistingOption_ShouldReturnNull()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetValueAsync("non.existent"))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.GetValueAsync("non.existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetValueAsync_ShouldCacheValue()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetValueAsync("app.name"))
            .ReturnsAsync("TestApp");

        // Act
        await _service.GetValueAsync("app.name");
        await _service.GetValueAsync("app.name");

        // Assert
        _mockOptionRepo.Verify(r => r.GetValueAsync("app.name"), Times.Once);
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_WithValidRequest_ShouldCreateOption()
    {
        // Arrange
        var request = new CreateSystemOptionRequest
        {
            Name = "new.option",
            Value = "value",
            Category = "test"
        };

        _mockOptionRepo.Setup(r => r.GetByNameAsync("new.option"))
            .ReturnsAsync((SystemOption?)null);

        var created = new SystemOption { Id = 1, Name = "new.option", Value = "value", Category = "test" };
        _mockOptionRepo.Setup(r => r.SetAsync("new.option", "value", "test"))
            .Returns(Task.CompletedTask);
        _mockOptionRepo.Setup(r => r.GetByNameAsync("new.option"))
            .ReturnsAsync((SystemOption?)null); // 第一次检查

        // 模拟 SetAsync 后再次查询返回创建的实体
        var callCount = 0;
        _mockOptionRepo.Setup(r => r.GetByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((string _) =>
            {
                callCount++;
                return callCount == 1 ? null : created;
            });

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("new.option");
        result.Value.Should().Be("value");
        _mockOptionRepo.Verify(r => r.SetAsync("new.option", "value", "test"), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithExistingName_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var request = new CreateSystemOptionRequest
        {
            Name = "existing.option",
            Value = "value"
        };

        var existing = new SystemOption { Id = 1, Name = "existing.option", Value = "old" };
        _mockOptionRepo.Setup(r => r.GetByNameAsync("existing.option"))
            .ReturnsAsync(existing);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
        _mockOptionRepo.Verify(r => r.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithExistingOption_ShouldUpdateAndReturnOption()
    {
        // Arrange
        var option = new SystemOption
        {
            Id = 1,
            Name = "app.name",
            Value = "OldValue",
            Category = "general"
        };

        _mockOptionRepo.Setup(r => r.GetByNameTrackedAsync("app.name"))
            .ReturnsAsync(option);

        var request = new UpdateSystemOptionRequest
        {
            Name = "app.name",
            Value = "NewValue",
            Category = "updated"
        };

        // Act
        var result = await _service.UpdateAsync("app.name", request);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().Be("NewValue");
        result.Category.Should().Be("updated");
        option.Value.Should().Be("NewValue");
        option.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistingOption_ShouldReturnNull()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetByNameTrackedAsync("non.existent"))
            .ReturnsAsync((SystemOption?)null);

        var request = new UpdateSystemOptionRequest
        {
            Name = "non.existent",
            Value = "X"
        };

        // Act
        var result = await _service.UpdateAsync("non.existent", request);

        // Assert
        result.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WithNullCategory_ShouldKeepExistingCategory()
    {
        // Arrange
        var option = new SystemOption
        {
            Id = 1,
            Name = "app.name",
            Value = "OldValue",
            Category = "general"
        };

        _mockOptionRepo.Setup(r => r.GetByNameTrackedAsync("app.name"))
            .ReturnsAsync(option);

        var request = new UpdateSystemOptionRequest
        {
            Name = "app.name",
            Value = "NewValue",
            Category = null // 不修改分类
        };

        // Act
        var result = await _service.UpdateAsync("app.name", request);

        // Assert
        result.Should().NotBeNull();
        result!.Category.Should().Be("general"); // 应保留原分类
    }

    [Fact]
    public async Task UpdateAsync_WithMailFetchFrequency_ShouldUpdateAllMailboxes()
    {
        // Arrange
        var option = new SystemOption
        {
            Id = 1,
            Name = "mail.fetch_frequency",
            Value = "5",
            Category = "mail"
        };

        _mockOptionRepo.Setup(r => r.GetByNameTrackedAsync("mail.fetch_frequency"))
            .ReturnsAsync(option);

        var mailboxes = new List<Mailbox>
        {
            new() { Id = 1, Name = "MB1", FetchFrequency = 5 },
            new() { Id = 2, Name = "MB2", FetchFrequency = 5 }
        };

        _mockMailboxRepo.Setup(r => r.GetActiveTrackedAsync())
            .ReturnsAsync(mailboxes);

        var request = new UpdateSystemOptionRequest
        {
            Name = "mail.fetch_frequency",
            Value = "10"
        };

        // Act
        var result = await _service.UpdateAsync("mail.fetch_frequency", request);

        // Assert
        result.Should().NotBeNull();
        mailboxes[0].FetchFrequency.Should().Be(10);
        mailboxes[1].FetchFrequency.Should().Be(10);
        mailboxes[0].UpdatedAt.Should().NotBe(default);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("收取频率")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithPasswordSecurityOption_ShouldInvalidatePasswordPolicyCache()
    {
        // Arrange
        var option = new SystemOption
        {
            Id = 1,
            Name = "security.password_min_length",
            Value = "8",
            Category = "security"
        };

        _mockOptionRepo.Setup(r => r.GetByNameTrackedAsync("security.password_min_length"))
            .ReturnsAsync(option);

        var request = new UpdateSystemOptionRequest
        {
            Name = "security.password_min_length",
            Value = "12"
        };

        // Act
        await _service.UpdateAsync("security.password_min_length", request);

        // Assert - 安全相关配置修改后应清除密码策略缓存
        _mockPasswordPolicyService.Verify(p => p.InvalidateCache(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNonSecurityOption_ShouldNotInvalidatePasswordPolicyCache()
    {
        // Arrange
        var option = new SystemOption
        {
            Id = 1,
            Name = "app.name",
            Value = "Old",
            Category = "general"
        };

        _mockOptionRepo.Setup(r => r.GetByNameTrackedAsync("app.name"))
            .ReturnsAsync(option);

        var request = new UpdateSystemOptionRequest
        {
            Name = "app.name",
            Value = "New"
        };

        // Act
        await _service.UpdateAsync("app.name", request);

        // Assert - 非安全相关配置修改不应清除密码策略缓存
        _mockPasswordPolicyService.Verify(p => p.InvalidateCache(), Times.Never);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithExistingOption_ShouldDeleteAndReturnTrue()
    {
        // Arrange
        var option = new SystemOption { Id = 1, Name = "app.name", Value = "X", Category = "general" };

        _mockOptionRepo.Setup(r => r.GetByNameAsync("app.name"))
            .ReturnsAsync(option);

        // Act
        var result = await _service.DeleteAsync("app.name");

        // Assert
        result.Should().BeTrue();
        _mockOptionRepo.Verify(r => r.DeleteAsync(option), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingOption_ShouldReturnFalse()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetByNameAsync("non.existent"))
            .ReturnsAsync((SystemOption?)null);

        // Act
        var result = await _service.DeleteAsync("non.existent");

        // Assert
        result.Should().BeFalse();
        _mockOptionRepo.Verify(r => r.DeleteAsync(It.IsAny<SystemOption>()), Times.Never);
    }

    #endregion

    #region GetAllAsDictionaryAsync

    [Fact]
    public async Task GetAllAsDictionaryAsync_ShouldReturnDictionary()
    {
        // Arrange
        var options = new List<SystemOption>
        {
            new() { Id = 1, Name = "key1", Value = "value1" },
            new() { Id = 2, Name = "key2", Value = "value2" }
        };

        _mockOptionRepo.Setup(r => r.GetAllWithOptionsAsync())
            .ReturnsAsync(options);

        // Act
        var result = await _service.GetAllAsDictionaryAsync();

        // Assert
        result.Should().HaveCount(2);
        result["key1"].Should().Be("value1");
        result["key2"].Should().Be("value2");
    }

    [Fact]
    public async Task GetAllAsDictionaryAsync_ShouldCacheResult()
    {
        // Arrange
        _mockOptionRepo.Setup(r => r.GetAllWithOptionsAsync())
            .ReturnsAsync(new List<SystemOption>());

        // Act
        await _service.GetAllAsDictionaryAsync();
        await _service.GetAllAsDictionaryAsync();

        // Assert
        _mockOptionRepo.Verify(r => r.GetAllWithOptionsAsync(), Times.Once);
    }

    #endregion

    #region BatchUpdateAsync

    [Fact]
    public async Task BatchUpdateAsync_WithExistingOptions_ShouldUpdateAllAndCommit()
    {
        // Arrange
        var options = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        var opt1 = new SystemOption { Id = 1, Name = "key1", Value = "old1" };
        var opt2 = new SystemOption { Id = 2, Name = "key2", Value = "old2" };

        SetupOptionQueryable(opt1, opt2);

        // Act
        await _service.BatchUpdateAsync(options);

        // Assert
        opt1.Value.Should().Be("value1");
        opt1.UpdatedAt.Should().NotBe(default);
        opt2.Value.Should().Be("value2");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task BatchUpdateAsync_WithSomeNonExistingOptions_ShouldUpdateOnlyExisting()
    {
        // Arrange
        var options = new Dictionary<string, string>
        {
            { "existing", "new-value" },
            { "non-existing", "ignored" }
        };

        var opt = new SystemOption { Id = 1, Name = "existing", Value = "old" };

        // 只返回已存在的选项，non-existing 不在查询结果中
        SetupOptionQueryable(opt);

        // Act
        await _service.BatchUpdateAsync(options);

        // Assert - 只更新已存在的选项，tracked 查询 + SaveChangesAsync 持久化
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task BatchUpdateAsync_WhenExceptionOccurs_ShouldRollbackAndRethrow()
    {
        // Arrange
        var options = new Dictionary<string, string>
        {
            { "key1", "value1" }
        };

        SetupOptionQueryable(new SystemOption { Id = 1, Name = "key1", Value = "old" });
        // 模拟 SaveChangesAsync 抛出异常（替代原 UpdateAsync 抛异常的场景）
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act
        var act = () => _service.BatchUpdateAsync(options);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task BatchUpdateAsync_WithSecurityOptions_ShouldInvalidatePasswordPolicyCache()
    {
        // Arrange
        var options = new Dictionary<string, string>
        {
            { "security.password_min_length", "12" }
        };

        var opt = new SystemOption { Id = 1, Name = "security.password_min_length", Value = "10" };
        SetupOptionQueryable(opt);

        // Act
        await _service.BatchUpdateAsync(options);

        // Assert
        _mockPasswordPolicyService.Verify(p => p.InvalidateCache(), Times.Once);
    }

    #endregion
}
