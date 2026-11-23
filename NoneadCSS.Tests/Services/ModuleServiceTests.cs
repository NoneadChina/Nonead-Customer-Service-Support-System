using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ModuleService 单元测试
/// 覆盖模块的 CRUD、缓存管理、统计概览与激活状态切换
/// </summary>
public class ModuleServiceTests : IDisposable
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IModuleRepository> _mockRepo;
    private readonly MemoryCache _cache;
    private readonly ModuleService _service;

    public ModuleServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockRepo = new Mock<IModuleRepository>();
        _mockUnitOfWork.Setup(u => u.Modules).Returns(_mockRepo.Object);

        _cache = new MemoryCache(new MemoryCacheOptions());
        _service = new ModuleService(_mockUnitOfWork.Object, _cache, Options.Create(new NoneadCSS.Application.Configuration.CacheOptions()));
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    /// <summary>
    /// 创建测试用模块实体
    /// </summary>
    private static Module CreateModule(int id, string name, string category = "general", bool isActive = true)
    {
        return new Module
        {
            Id = id,
            Name = name,
            Title = $"Title {name}",
            Description = $"Description {name}",
            Version = "1.0.0",
            Author = "Tester",
            Icon = "icon",
            IsActive = isActive,
            Category = category,
            Settings = "{}",
            SortOrder = id,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    #region Constructor

    // Note: ModuleService constructor does not validate null arguments (just assigns them).
    // Skipping null-argument tests since the service doesn't enforce this.

    #endregion

    #region GetOverviewAsync

    [Fact]
    public async Task GetOverviewAsync_WithMixedModules_ShouldReturnCorrectStats()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", "general", isActive: true),
            CreateModule(2, "mod2", "general", isActive: false),
            CreateModule(3, "mod3", "tools", isActive: true)
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Should().NotBeNull();
        result.Total.Should().Be(3);
        result.Active.Should().Be(2);
        result.Inactive.Should().Be(1);
        result.Categories.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetOverviewAsync_WithEmptyModules_ShouldReturnZeroStats()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Total.Should().Be(0);
        result.Active.Should().Be(0);
        result.Inactive.Should().Be(0);
        result.Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_WithAllActive_ShouldReturnZeroInactive()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", "general", isActive: true),
            CreateModule(2, "mod2", "tools", isActive: true)
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Active.Should().Be(2);
        result.Inactive.Should().Be(0);
    }

    [Fact]
    public async Task GetOverviewAsync_WithAllInactive_ShouldReturnZeroActive()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", "general", isActive: false),
            CreateModule(2, "mod2", "tools", isActive: false)
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Active.Should().Be(0);
        result.Inactive.Should().Be(2);
    }

    [Theory]
    [InlineData("integration", "集成")]
    [InlineData("communication", "通讯")]
    [InlineData("reports", "报表")]
    [InlineData("tools", "工具")]
    [InlineData("general", "通用")]
    [InlineData("automation", "自动化")]
    public async Task GetOverviewAsync_WithKnownCategory_ShouldReturnCorrectLabel(string category, string expectedLabel)
    {
        // Arrange
        var modules = new List<Module> { CreateModule(1, "mod1", category, isActive: true) };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Categories.Should().HaveCount(1);
        result.Categories[0].Category.Should().Be(category);
        result.Categories[0].CategoryLabel.Should().Be(expectedLabel);
    }

    [Fact]
    public async Task GetOverviewAsync_WithUnknownCategory_ShouldUseCategoryAsLabel()
    {
        // Arrange
        var modules = new List<Module> { CreateModule(1, "mod1", "custom_cat", isActive: true) };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Categories[0].CategoryLabel.Should().Be("custom_cat");
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldOrderCategoriesAlphabetically()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", "tools", isActive: true),
            CreateModule(2, "mod2", "general", isActive: true),
            CreateModule(3, "mod3", "automation", isActive: true)
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Categories.Select(c => c.Category).Should().BeInAscendingOrder();
        result.Categories.Select(c => c.Category).Should().Equal("automation", "general", "tools");
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldCountActivePerCategory()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", "general", isActive: true),
            CreateModule(2, "mod2", "general", isActive: true),
            CreateModule(3, "mod3", "general", isActive: false),
            CreateModule(4, "mod4", "tools", isActive: false)
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetOverviewAsync();

        // Assert
        var generalStats = result.Categories.Single(c => c.Category == "general");
        generalStats.Count.Should().Be(3);
        generalStats.ActiveCount.Should().Be(2);

        var toolsStats = result.Categories.Single(c => c.Category == "tools");
        toolsStats.Count.Should().Be(1);
        toolsStats.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOverviewAsync_ShouldUseCacheAndNotCallRepoTwice()
    {
        // Arrange
        var modules = new List<Module> { CreateModule(1, "mod1") };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        await _service.GetOverviewAsync();
        await _service.GetOverviewAsync();

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    #endregion

    #region GetPagedAsync

    [Fact]
    public async Task GetPagedAsync_ShouldDelegateToRepositoryAndMapDtos()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1"),
            CreateModule(2, "mod2")
        };
        _mockRepo.Setup(r => r.GetPagedAsync(null, null, null, 1, 20))
            .ReturnsAsync((modules, 2));

        var query = new ModuleQuery { Page = 1, PageSize = 20 };

        // Act
        var (items, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(2);
        items.Should().HaveCount(2);
        items[0].Name.Should().Be("mod1");
        items[1].Name.Should().Be("mod2");
    }

    [Fact]
    public async Task GetPagedAsync_WithEmptyResult_ShouldReturnEmptyListAndZeroTotal()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<Module>(), 0));

        var query = new ModuleQuery();

        // Act
        var (items, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(0);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPagedAsync_ShouldPassAllQueryParametersToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetPagedAsync("tools", true, "search-term", 2, 10))
            .ReturnsAsync((new List<Module>(), 5));

        var query = new ModuleQuery
        {
            Category = "tools",
            IsActive = true,
            Search = "search-term",
            Page = 2,
            PageSize = 10
        };

        // Act
        var (_, total) = await _service.GetPagedAsync(query);

        // Assert
        _mockRepo.Verify(r => r.GetPagedAsync("tools", true, "search-term", 2, 10), Times.Once);
        total.Should().Be(5);
    }

    [Fact]
    public async Task GetPagedAsync_ShouldNotUseCache()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<Module>(), 0));

        var query = new ModuleQuery();

        // Act
        await _service.GetPagedAsync(query);
        await _service.GetPagedAsync(query);

        // Assert - paged query should always call repo, not use cache
        _mockRepo.Verify(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetPagedAsync_ShouldMapAllDtoFields()
    {
        // Arrange
        var module = CreateModule(1, "mod1", "tools", isActive: true);
        _mockRepo.Setup(r => r.GetPagedAsync(It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<Module> { module }, 1));

        var query = new ModuleQuery();

        // Act
        var (items, _) = await _service.GetPagedAsync(query);

        // Assert
        var dto = items[0];
        dto.Id.Should().Be(1);
        dto.Name.Should().Be("mod1");
        dto.Title.Should().Be("Title mod1");
        dto.Description.Should().Be("Description mod1");
        dto.Version.Should().Be("1.0.0");
        dto.Author.Should().Be("Tester");
        dto.Icon.Should().Be("icon");
        dto.IsActive.Should().BeTrue();
        dto.Category.Should().Be("tools");
        dto.Settings.Should().Be("{}");
        dto.SortOrder.Should().Be(1);
        dto.CreatedAt.Should().NotBe(default);
        dto.UpdatedAt.Should().NotBe(default);
    }

    #endregion

    #region GetAllAsync

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllDtos()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1"),
            CreateModule(2, "mod2"),
            CreateModule(3, "mod3")
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        result.Should().HaveCount(3);
        result.Select(d => d.Name).Should().Equal("mod1", "mod2", "mod3");
    }

    [Fact]
    public async Task GetAllAsync_WithEmptyModules_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());

        // Act
        var result = await _service.GetAllAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ShouldUseCacheAndNotCallRepoTwice()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());

        // Act
        await _service.GetAllAsync();
        await _service.GetAllAsync();

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    #endregion

    #region GetByNameAsync

    [Fact]
    public async Task GetByNameAsync_WithExisting_ShouldReturnDto()
    {
        // Arrange
        var module = CreateModule(1, "mod1");
        _mockRepo.Setup(r => r.GetByNameAsync("mod1")).ReturnsAsync(module);

        // Act
        var result = await _service.GetByNameAsync("mod1");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("mod1");
    }

    [Fact]
    public async Task GetByNameAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByNameAsync("nonexistent")).ReturnsAsync((Module?)null);

        // Act
        var result = await _service.GetByNameAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByNameAsync_ShouldDelegateToRepositoryWithName()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByNameAsync(It.IsAny<string>())).ReturnsAsync((Module?)null);

        // Act
        await _service.GetByNameAsync("target-name");

        // Assert
        _mockRepo.Verify(r => r.GetByNameAsync("target-name"), Times.Once);
    }

    [Fact]
    public async Task GetByNameAsync_DoesNotUseCache_ShouldAlwaysCallRepo()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByNameAsync(It.IsAny<string>())).ReturnsAsync((Module?)null);

        // Act
        await _service.GetByNameAsync("name1");
        await _service.GetByNameAsync("name2");

        // Assert
        _mockRepo.Verify(r => r.GetByNameAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    #endregion

    #region GetByCategoryAsync

    [Fact]
    public async Task GetByCategoryAsync_ShouldFilterByCategory()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", "tools"),
            CreateModule(2, "mod2", "general"),
            CreateModule(3, "mod3", "tools")
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetByCategoryAsync("tools");

        // Assert
        result.Should().HaveCount(2);
        result.Select(d => d.Name).Should().Equal("mod1", "mod3");
    }

    [Fact]
    public async Task GetByCategoryAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", "Tools"),
            CreateModule(2, "mod2", "general")
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetByCategoryAsync("tools");

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("mod1");
    }

    [Fact]
    public async Task GetByCategoryAsync_WithNoMatching_ShouldReturnEmptyList()
    {
        // Arrange
        var modules = new List<Module> { CreateModule(1, "mod1", "general") };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetByCategoryAsync("nonexistent");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCategoryAsync_WithEmptyModules_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());

        // Act
        var result = await _service.GetByCategoryAsync("general");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByCategoryAsync_ShouldUseCache()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());

        // Act
        await _service.GetByCategoryAsync("general");
        await _service.GetByCategoryAsync("tools");

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    #endregion

    #region GetActiveAsync

    [Fact]
    public async Task GetActiveAsync_ShouldReturnOnlyActiveModules()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", isActive: true),
            CreateModule(2, "mod2", isActive: false),
            CreateModule(3, "mod3", isActive: true)
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetActiveAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(d => d.IsActive);
        result.Select(d => d.Name).Should().Equal("mod1", "mod3");
    }

    [Fact]
    public async Task GetActiveAsync_WithNoActive_ShouldReturnEmptyList()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", isActive: false),
            CreateModule(2, "mod2", isActive: false)
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        var result = await _service.GetActiveAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveAsync_WithEmptyModules_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());

        // Act
        var result = await _service.GetActiveAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveAsync_ShouldUseCache()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());

        // Act
        await _service.GetActiveAsync();
        await _service.GetActiveAsync();

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_WithUniqueName_ShouldCreateAndReturnDto()
    {
        // Arrange
        var request = new CreateModuleRequest
        {
            Name = "newmod",
            Title = "New Module",
            Description = "A new module",
            Version = "2.0.0",
            Author = "Author",
            Icon = "star",
            Category = "tools",
            Settings = "{ key: 'value' }",
            SortOrder = 5
        };

        _mockRepo.Setup(r => r.ExistsByNameAsync("newmod")).ReturnsAsync(false);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Module>()))
            .ReturnsAsync((Module m) =>
            {
                m.Id = 10;
                m.CreatedAt = DateTime.UtcNow;
                m.UpdatedAt = DateTime.UtcNow;
                return m;
            });

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("newmod");
        result.Title.Should().Be("New Module");
        result.Id.Should().Be(10);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Module>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithExistingName_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var request = new CreateModuleRequest { Name = "existing", Title = "Existing" };
        _mockRepo.Setup(r => r.ExistsByNameAsync("existing")).ReturnsAsync(true);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"模块 'existing' 已存在");
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<Module>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldAlwaysSetIsActiveToFalse()
    {
        // Arrange
        var request = new CreateModuleRequest
        {
            Name = "newmod",
            Title = "New Module",
            Category = "general"
        };

        _mockRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>())).ReturnsAsync(false);
        Module? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Module>()))
            .Callback<Module>(m => captured = m)
            .ReturnsAsync((Module m) => m);

        // Act
        await _service.CreateAsync(request);

        // Assert
        captured.Should().NotBeNull();
        captured!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_ShouldMapAllRequestFieldsToModule()
    {
        // Arrange
        var request = new CreateModuleRequest
        {
            Name = "newmod",
            Title = "New Module",
            Description = "Description text",
            Version = "3.1.4",
            Author = "Author Name",
            Icon = "icon-class",
            Category = "integration",
            Settings = "{\"setting\":true}",
            SortOrder = 42
        };

        _mockRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>())).ReturnsAsync(false);
        Module? captured = null;
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Module>()))
            .Callback<Module>(m => captured = m)
            .ReturnsAsync((Module m) => m);

        // Act
        await _service.CreateAsync(request);

        // Assert
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("newmod");
        captured.Title.Should().Be("New Module");
        captured.Description.Should().Be("Description text");
        captured.Version.Should().Be("3.1.4");
        captured.Author.Should().Be("Author Name");
        captured.Icon.Should().Be("icon-class");
        captured.Category.Should().Be("integration");
        captured.Settings.Should().Be("{\"setting\":true}");
        captured.SortOrder.Should().Be(42);
    }

    [Fact]
    public async Task CreateAsync_ShouldInvalidateCache()
    {
        // Arrange - prime cache first
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());
        _mockRepo.Setup(r => r.ExistsByNameAsync(It.IsAny<string>())).ReturnsAsync(false);
        _mockRepo.Setup(r => r.AddAsync(It.IsAny<Module>()))
            .ReturnsAsync((Module m) => m);

        await _service.GetAllAsync(); // primes cache

        // Act
        await _service.CreateAsync(new CreateModuleRequest { Name = "new", Title = "New" });
        await _service.GetAllAsync(); // should miss cache after invalidation

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Exactly(2));
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithExisting_ShouldReturnUpdatedDto()
    {
        // Arrange
        var updated = CreateModule(1, "mod1", "tools", isActive: true);
        updated.Title = "Updated Title";

        _mockRepo.Setup(r => r.UpdateAsync("mod1", It.IsAny<Module>()))
            .ReturnsAsync(updated);

        var request = new UpdateModuleRequest
        {
            Title = "Updated Title",
            Description = "Updated",
            Version = "2.0.0",
            Author = "Author",
            Icon = "icon",
            Category = "tools",
            Settings = "{}",
            SortOrder = 1
        };

        // Act
        var result = await _service.UpdateAsync("mod1", request);

        // Assert
        result.Should().NotBeNull();
        result!.Title.Should().Be("Updated Title");
    }

    [Fact]
    public async Task UpdateAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Module>()))
            .ReturnsAsync((Module?)null);

        var request = new UpdateModuleRequest { Title = "Title" };

        // Act
        var result = await _service.UpdateAsync("nonexistent", request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldMapAllRequestFieldsToModule()
    {
        // Arrange
        Module? captured = null;
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Module>()))
            .Callback<string, Module>((_, m) => captured = m)
            .ReturnsAsync((string _, Module m) => m);

        var request = new UpdateModuleRequest
        {
            Title = "Updated Title",
            Description = "Updated Description",
            Version = "5.0.0",
            Author = "New Author",
            Icon = "new-icon",
            Category = "reports",
            Settings = "{\"updated\":true}",
            SortOrder = 99
        };

        // Act
        await _service.UpdateAsync("mod1", request);

        // Assert
        captured.Should().NotBeNull();
        captured!.Title.Should().Be("Updated Title");
        captured.Description.Should().Be("Updated Description");
        captured.Version.Should().Be("5.0.0");
        captured.Author.Should().Be("New Author");
        captured.Icon.Should().Be("new-icon");
        captured.Category.Should().Be("reports");
        captured.Settings.Should().Be("{\"updated\":true}");
        captured.SortOrder.Should().Be(99);
    }

    [Fact]
    public async Task UpdateAsync_ShouldPassNameToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Module>()))
            .ReturnsAsync((Module?)null);

        var request = new UpdateModuleRequest { Title = "Title" };

        // Act
        await _service.UpdateAsync("target-name", request);

        // Assert
        _mockRepo.Verify(r => r.UpdateAsync("target-name", It.IsAny<Module>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldInvalidateCache_EvenWhenModuleNotFound()
    {
        // Arrange - prime cache
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Module>()))
            .ReturnsAsync((Module?)null);

        await _service.GetAllAsync();

        // Act
        await _service.UpdateAsync("nonexistent", new UpdateModuleRequest { Title = "X" });
        await _service.GetAllAsync();

        // Assert - cache invalidated even on non-existing
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateAsync_WithExisting_ShouldInvalidateCache()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());
        _mockRepo.Setup(r => r.UpdateAsync(It.IsAny<string>(), It.IsAny<Module>()))
            .ReturnsAsync((Module?)CreateModule(1, "mod1"));

        await _service.GetAllAsync(); // prime cache

        // Act
        await _service.UpdateAsync("mod1", new UpdateModuleRequest { Title = "Updated" });
        await _service.GetAllAsync(); // should miss cache

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Exactly(2));
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithExisting_ShouldReturnTrue()
    {
        // Arrange
        _mockRepo.Setup(r => r.DeleteAsync("mod1")).ReturnsAsync(true);

        // Act
        var result = await _service.DeleteAsync("mod1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        _mockRepo.Setup(r => r.DeleteAsync("nonexistent")).ReturnsAsync(false);

        // Act
        var result = await _service.DeleteAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ShouldDelegateNameToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);

        // Act
        await _service.DeleteAsync("target-name");

        // Assert
        _mockRepo.Verify(r => r.DeleteAsync("target-name"), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithSuccess_ShouldInvalidateCache()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());
        _mockRepo.Setup(r => r.DeleteAsync(It.IsAny<string>())).ReturnsAsync(true);

        await _service.GetAllAsync(); // prime cache

        // Act
        await _service.DeleteAsync("mod1");
        await _service.GetAllAsync(); // should miss cache

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteAsync_WithFailure_ShouldNotInvalidateCache()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());
        _mockRepo.Setup(r => r.DeleteAsync(It.IsAny<string>())).ReturnsAsync(false);

        await _service.GetAllAsync(); // prime cache

        // Act
        await _service.DeleteAsync("nonexistent");
        await _service.GetAllAsync(); // should hit cache

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    #endregion

    #region ToggleActiveAsync

    [Fact]
    public async Task ToggleActiveAsync_WithExisting_ShouldReturnDto()
    {
        // Arrange
        var module = CreateModule(1, "mod1", isActive: false);
        _mockRepo.Setup(r => r.ToggleActiveAsync("mod1")).ReturnsAsync(module);

        // Act
        var result = await _service.ToggleActiveAsync("mod1");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("mod1");
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleActiveAsync_WithNonExisting_ShouldReturnNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.ToggleActiveAsync("nonexistent")).ReturnsAsync((Module?)null);

        // Act
        var result = await _service.ToggleActiveAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ToggleActiveAsync_ShouldDelegateNameToRepository()
    {
        // Arrange
        _mockRepo.Setup(r => r.ToggleActiveAsync(It.IsAny<string>()))
            .ReturnsAsync((Module?)null);

        // Act
        await _service.ToggleActiveAsync("target-name");

        // Assert
        _mockRepo.Verify(r => r.ToggleActiveAsync("target-name"), Times.Once);
    }

    [Fact]
    public async Task ToggleActiveAsync_WithSuccess_ShouldInvalidateCache()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());
        _mockRepo.Setup(r => r.ToggleActiveAsync(It.IsAny<string>()))
            .ReturnsAsync((Module?)CreateModule(1, "mod1"));

        await _service.GetAllAsync(); // prime cache

        // Act
        await _service.ToggleActiveAsync("mod1");
        await _service.GetAllAsync(); // should miss cache

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task ToggleActiveAsync_WithFailure_ShouldNotInvalidateCache()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Module>());
        _mockRepo.Setup(r => r.ToggleActiveAsync(It.IsAny<string>()))
            .ReturnsAsync((Module?)null);

        await _service.GetAllAsync(); // prime cache

        // Act
        await _service.ToggleActiveAsync("nonexistent");
        await _service.GetAllAsync(); // should hit cache

        // Assert
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    #endregion

    #region Cache Behavior (cross-method)

    [Fact]
    public async Task Cache_WhenPrimedByGetAll_GetByCategoryShouldUseCachedData()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", "tools"),
            CreateModule(2, "mod2", "general")
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act - prime with GetAllAsync, then GetByCategoryAsync should use cache
        await _service.GetAllAsync();
        var result = await _service.GetByCategoryAsync("tools");

        // Assert
        result.Should().HaveCount(1);
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task Cache_WhenPrimedByGetAll_GetActiveShouldUseCachedData()
    {
        // Arrange
        var modules = new List<Module>
        {
            CreateModule(1, "mod1", isActive: true),
            CreateModule(2, "mod2", isActive: false)
        };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        await _service.GetAllAsync();
        var result = await _service.GetActiveAsync();

        // Assert
        result.Should().HaveCount(1);
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task Cache_WhenPrimedByGetAll_GetOverviewShouldUseCachedData()
    {
        // Arrange
        var modules = new List<Module> { CreateModule(1, "mod1") };
        _mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(modules);

        // Act
        await _service.GetAllAsync();
        var result = await _service.GetOverviewAsync();

        // Assert
        result.Total.Should().Be(1);
        _mockRepo.Verify(r => r.GetAllAsync(), Times.Once);
    }

    #endregion
}
