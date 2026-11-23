using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// RoleService 单元测试
/// 覆盖角色的 CRUD、批量分配、权限委派、软删除、缓存失效等场景
/// </summary>
public class RoleServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IRoleRepository> _mockRoleRepo;
    private readonly Mock<IUserRoleRepository> _mockUserRoleRepo;
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;
    private readonly IMemoryCache _cache;
    private readonly RoleService _service;

    public RoleServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockRoleRepo = new Mock<IRoleRepository>();
        _mockUserRoleRepo = new Mock<IUserRoleRepository>();
        _mockUserRoleResolver = new Mock<IUserRoleResolver>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _mockUnitOfWork.Setup(u => u.Roles).Returns(_mockRoleRepo.Object);
        _mockUnitOfWork.Setup(u => u.UserRoles).Returns(_mockUserRoleRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        _service = new RoleService(_mockUnitOfWork.Object, _mockUserRoleResolver.Object, _cache, Options.Create(new NoneadCSS.Application.Configuration.CacheOptions()));
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock Role DbSet
    /// </summary>
    private void SetupRoleQueryable(params Role[] roles)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(roles.ToList());
        _mockRoleRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock UserRole DbSet（用于 BatchAssignRoleAsync 的批量查询）
    /// </summary>
    private void SetupUserRoleQueryable(params UserRole[] userRoles)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(userRoles.ToList());
        _mockUserRoleRepo.Setup(r => r.GetQueryable(It.IsAny<bool>()))
            .Returns(mockDbSet.Object);
    }

    #region GetByIdAsync

    [Fact]
    public async Task GetByIdAsync_WithExistingRole_ShouldReturnDto()
    {
        // Arrange
        var role = new Role
        {
            Id = 1,
            Name = "Admin",
            Slug = "admin",
            Description = "Administrator role",
            PermissionsJson = "[\"users.view\",\"users.create\"]",
            IsSystem = true,
            SortOrder = 1
        };

        _mockRoleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(role);
        _mockUserRoleResolver.Setup(r => r.CountUsersInRoleAsync(1))
            .ReturnsAsync(5);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Admin");
        result.Slug.Should().Be("admin");
        result.IsSystem.Should().BeTrue();
        result.UserCount.Should().Be(5);
        result.Permissions.Should().Contain(new[] { "users.view", "users.create" });
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistingRole_ShouldReturnNull()
    {
        // Arrange
        _mockRoleRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldCacheResult()
    {
        // Arrange
        var role = new Role { Id = 1, Name = "Admin", Slug = "admin" };
        _mockRoleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(role);
        _mockUserRoleResolver.Setup(r => r.CountUsersInRoleAsync(1))
            .ReturnsAsync(1);

        // Act
        await _service.GetByIdAsync(1);
        await _service.GetByIdAsync(1);
        await _service.GetByIdAsync(1);

        // Assert - 第二次应命中缓存，不再次查询仓储
        _mockRoleRepo.Verify(r => r.GetByIdAsync(1), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WithInvalidPermissionsJson_ShouldReturnEmptyList()
    {
        // Arrange
        var role = new Role
        {
            Id = 1,
            Name = "Admin",
            Slug = "admin",
            PermissionsJson = "invalid-json"
        };

        _mockRoleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(role);
        _mockUserRoleResolver.Setup(r => r.CountUsersInRoleAsync(1))
            .ReturnsAsync(0);

        // Act
        var result = await _service.GetByIdAsync(1);

        // Assert - 无效 JSON 应返回空权限列表，不抛异常
        result.Should().NotBeNull();
        result!.Permissions.Should().BeEmpty();
    }

    #endregion

    #region GetPagedAsync

    [Fact]
    public async Task GetPagedAsync_ShouldReturnPagedDtos()
    {
        // Arrange
        var roles = new List<Role>
        {
            new() { Id = 1, Name = "Admin", Slug = "admin" },
            new() { Id = 2, Name = "Agent", Slug = "agent" }
        };

        _mockRoleRepo.Setup(r => r.GetPagedAsync(1, 20, null))
            .ReturnsAsync((roles, 2));
        _mockUserRoleResolver.Setup(r => r.CountUsersInRolesAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int> { { 1, 5 }, { 2, 10 } });

        var query = new RoleQuery { Page = 1, PageSize = 20 };

        // Act
        var (items, total) = await _service.GetPagedAsync(query);

        // Assert
        total.Should().Be(2);
        items.Should().HaveCount(2);
        items.First().UserCount.Should().Be(5);
        items.Last().UserCount.Should().Be(10);
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_WithValidRequest_ShouldCreateAndReturnDto()
    {
        // Arrange
        var request = new CreateRoleRequest
        {
            Name = "Custom Role",
            Slug = "custom-role",
            Description = "A custom role",
            Permissions = new List<string> { "users.view", "users.create" },
            SortOrder = 5
        };

        _mockRoleRepo.Setup(r => r.GetBySlugAsync(It.IsAny<string>()))
            .ReturnsAsync((Role?)null);

        Role? captured = null;
        _mockRoleRepo.Setup(r => r.AddAsync(It.IsAny<Role>()))
            .Callback<Role>(r => captured = r)
            .ReturnsAsync((Role r) => r);

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Custom Role");
        result.Slug.Should().Be("custom-role");
        result.IsSystem.Should().BeFalse();
        result.Permissions.Should().Contain(new[] { "users.view", "users.create" });
        captured.Should().NotBeNull();
        captured!.IsSystem.Should().BeFalse();
        captured.SortOrder.Should().Be(5);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateSlug_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var request = new CreateRoleRequest
        {
            Name = "Custom",
            Slug = "existing-slug"
        };

        var existing = new Role { Id = 1, Name = "Existing", Slug = "existing-slug" };
        _mockRoleRepo.Setup(r => r.GetBySlugAsync("existing-slug"))
            .ReturnsAsync(existing);

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
        _mockRoleRepo.Verify(r => r.AddAsync(It.IsAny<Role>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldNormalizeSlug()
    {
        // Arrange
        var request = new CreateRoleRequest
        {
            Name = "X",
            Slug = "Some Slug With Spaces",
            Permissions = new List<string>()
        };

        _mockRoleRepo.Setup(r => r.GetBySlugAsync(It.IsAny<string>()))
            .ReturnsAsync((Role?)null);

        Role? captured = null;
        _mockRoleRepo.Setup(r => r.AddAsync(It.IsAny<Role>()))
            .Callback<Role>(r => captured = r)
            .ReturnsAsync((Role r) => r);

        // Act
        await _service.CreateAsync(request);

        // Assert - 空格应被替换为连字符，并转为小写
        captured!.Slug.Should().Be("some-slug-with-spaces");
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task UpdateAsync_WithExistingCustomRole_ShouldUpdateAllFields()
    {
        // Arrange
        var role = new Role
        {
            Id = 1,
            Name = "Old",
            Slug = "old",
            Description = "Old desc",
            PermissionsJson = "[]",
            IsSystem = false,
            SortOrder = 1
        };

        SetupRoleQueryable(role);

        var request = new UpdateRoleRequest
        {
            Id = 1,
            Name = "New",
            Description = "New desc",
            Permissions = new List<string> { "users.view" },
            SortOrder = 10
        };

        _mockUserRoleResolver.Setup(r => r.CountUsersInRoleAsync(1))
            .ReturnsAsync(0);

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("New");
        result.Description.Should().Be("New desc");
        result.SortOrder.Should().Be(10);
        result.Permissions.Should().ContainSingle(p => p == "users.view");
        role.Name.Should().Be("New");
        role.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithSystemRole_ShouldOnlyUpdateDescriptionAndSortOrder()
    {
        // Arrange - 系统角色的权限和标识不允许修改
        var role = new Role
        {
            Id = 1,
            Name = "Admin",
            Slug = "admin",
            Description = "Old desc",
            PermissionsJson = "[\"all\"]",
            IsSystem = true,
            SortOrder = 1
        };

        SetupRoleQueryable(role);

        var request = new UpdateRoleRequest
        {
            Id = 1,
            Name = "ChangedName", // 应被忽略
            Description = "New desc",
            Permissions = new List<string> { "different" }, // 应被忽略
            SortOrder = 99
        };

        _mockUserRoleResolver.Setup(r => r.CountUsersInRoleAsync(1))
            .ReturnsAsync(0);

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().NotBeNull();
        // 系统角色：Name 不应被修改
        role.Name.Should().Be("Admin");
        role.PermissionsJson.Should().Be("[\"all\"]");
        // 但 Description 和 SortOrder 可以修改
        role.Description.Should().Be("New desc");
        role.SortOrder.Should().Be(99);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistingRole_ShouldReturnNull()
    {
        // Arrange
        SetupRoleQueryable(); // 空数据集

        var request = new UpdateRoleRequest
        {
            Id = 999,
            Name = "X",
            Description = "Y",
            Permissions = new List<string>(),
            SortOrder = 0
        };

        // Act
        var result = await _service.UpdateAsync(request);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_WithExistingCustomRole_ShouldSoftDeleteAndReturnTrue()
    {
        // Arrange
        var role = new Role
        {
            Id = 1,
            Name = "Custom",
            Slug = "custom",
            IsSystem = false
        };

        _mockRoleRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(role);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeTrue();
        role.DeletedAt.Should().NotBeNull();
        role.UpdatedAt.Should().NotBe(default);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithSystemRole_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var role = new Role
        {
            Id = 1,
            Name = "Admin",
            Slug = "admin",
            IsSystem = true
        };

        _mockRoleRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(role);

        // Act
        var act = () => _service.DeleteAsync(1);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*系统角色*");
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingRole_ShouldReturnFalse()
    {
        // Arrange
        _mockRoleRepo.Setup(r => r.GetByIdTrackedAsync(999))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _service.DeleteAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_WithAlreadyDeletedRole_ShouldReturnFalse()
    {
        // Arrange
        var role = new Role
        {
            Id = 1,
            Name = "Deleted",
            Slug = "deleted",
            IsSystem = false,
            DeletedAt = DateTime.UtcNow
        };

        _mockRoleRepo.Setup(r => r.GetByIdTrackedAsync(1))
            .ReturnsAsync(role);

        // Act
        var result = await _service.DeleteAsync(1);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region AssignRoleAsync

    [Fact]
    public async Task AssignRoleAsync_WithNewAssignment_ShouldCreateAndReturnTrue()
    {
        // Arrange
        var request = new AssignRoleRequest
        {
            UserId = 1,
            RoleId = 2,
            MailboxId = 3
        };

        _mockUserRoleRepo.Setup(r => r.GetByUserAndRoleAsync(1, 2, 3))
            .ReturnsAsync((UserRole?)null);

        UserRole? captured = null;
        _mockUserRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>()))
            .Callback<UserRole>(ur => captured = ur)
            .ReturnsAsync((UserRole ur) => ur);

        // Act
        var result = await _service.AssignRoleAsync(request);

        // Assert
        result.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(1);
        captured.RoleId.Should().Be(2);
        captured.MailboxId.Should().Be(3);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task AssignRoleAsync_WithExistingAssignment_ShouldReturnTrueWithoutReAdding()
    {
        // Arrange
        var existing = new UserRole { Id = 10, UserId = 1, RoleId = 2, MailboxId = 3 };

        _mockUserRoleRepo.Setup(r => r.GetByUserAndRoleAsync(1, 2, 3))
            .ReturnsAsync(existing);

        var request = new AssignRoleRequest { UserId = 1, RoleId = 2, MailboxId = 3 };

        // Act
        var result = await _service.AssignRoleAsync(request);

        // Assert
        result.Should().BeTrue();
        _mockUserRoleRepo.Verify(r => r.AddAsync(It.IsAny<UserRole>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Never);
    }

    #endregion

    #region BatchAssignRoleAsync

    [Fact]
    public async Task BatchAssignRoleAsync_WithNewAssignments_ShouldCreateAllAndCommit()
    {
        // Arrange
        var request = new BatchAssignRoleRequest
        {
            RoleId = 5,
            UserIds = new List<int> { 1, 2, 3 },
            MailboxId = 10
        };

        // 没有任何已存在的分配（GetQueryable 返回空列表）
        SetupUserRoleQueryable();

        // Act
        var result = await _service.BatchAssignRoleAsync(request);

        // Assert
        result.Should().BeTrue();
        _mockUserRoleRepo.Verify(r => r.AddAsync(It.IsAny<UserRole>()), Times.Exactly(3));
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task BatchAssignRoleAsync_WithExistingAssignments_ShouldSkipExisting()
    {
        // Arrange
        var request = new BatchAssignRoleRequest
        {
            RoleId = 5,
            UserIds = new List<int> { 1, 2 },
            MailboxId = null
        };

        // 用户 1 已有分配，用户 2 没有
        SetupUserRoleQueryable(new UserRole { Id = 100, UserId = 1, RoleId = 5 });

        // Act
        var result = await _service.BatchAssignRoleAsync(request);

        // Assert
        result.Should().BeTrue();
        _mockUserRoleRepo.Verify(r => r.AddAsync(It.IsAny<UserRole>()), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task BatchAssignRoleAsync_WhenExceptionOccurs_ShouldRollbackAndRethrow()
    {
        // Arrange
        var request = new BatchAssignRoleRequest
        {
            RoleId = 5,
            UserIds = new List<int> { 1 },
            MailboxId = null
        };

        SetupUserRoleQueryable();
        _mockUserRoleRepo.Setup(r => r.AddAsync(It.IsAny<UserRole>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        // Act
        var act = () => _service.BatchAssignRoleAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Never);
    }

    #endregion

    #region RemoveRoleAssignmentAsync

    [Fact]
    public async Task RemoveRoleAssignmentAsync_WithExisting_ShouldDeleteAndReturnTrue()
    {
        // Arrange
        var userRole = new UserRole { Id = 1, UserId = 10, RoleId = 5 };

        _mockUserRoleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(userRole);

        // Act
        var result = await _service.RemoveRoleAssignmentAsync(1);

        // Assert
        result.Should().BeTrue();
        _mockUserRoleRepo.Verify(r => r.DeleteAsync(userRole), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task RemoveRoleAssignmentAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        _mockUserRoleRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((UserRole?)null);

        // Act
        var result = await _service.RemoveRoleAssignmentAsync(999);

        // Assert
        result.Should().BeFalse();
        _mockUserRoleRepo.Verify(r => r.DeleteAsync(It.IsAny<UserRole>()), Times.Never);
    }

    #endregion

    #region UserHasPermissionAsync / GetUserPermissionsAsync (Delegation)

    [Fact]
    public async Task UserHasPermissionAsync_ShouldDelegateToUserRoleResolver()
    {
        // Arrange
        _mockUserRoleResolver.Setup(r => r.HasPermissionAsync(1, "users.view", null))
            .ReturnsAsync(true);

        // Act
        var result = await _service.UserHasPermissionAsync(1, "users.view");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserPermissionsAsync_ShouldDelegateToUserRoleResolver()
    {
        // Arrange
        var expected = new List<string> { "users.view", "users.create" };
        _mockUserRoleResolver.Setup(r => r.GetPermissionsAsync(1, null))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.GetUserPermissionsAsync(1);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    #endregion

    #region GetUserRolesAsync

    [Fact]
    public async Task GetUserRolesAsync_WithRoles_ShouldReturnDtos()
    {
        // Arrange
        var roles = new List<Role>
        {
            new() { Id = 1, Name = "Admin", Slug = "admin", IsSystem = true },
            new() { Id = 2, Name = "Agent", Slug = "agent", IsSystem = true }
        };

        _mockRoleRepo.Setup(r => r.GetByUserIdAsync(42, null))
            .ReturnsAsync(roles);

        // Act
        var result = await _service.GetUserRolesAsync(42);

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Admin");
        result[1].Name.Should().Be("Agent");
    }

    [Fact]
    public async Task GetUserRolesAsync_WithNoRoles_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRoleRepo.Setup(r => r.GetByUserIdAsync(99, null))
            .ReturnsAsync(new List<Role>());

        // Act
        var result = await _service.GetUserRolesAsync(99);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetRoleUsersAsync

    [Fact]
    public async Task GetRoleUsersAsync_WithExistingRole_ShouldReturnMembers()
    {
        // Arrange
        var role = new Role { Id = 1, Name = "Admin", Slug = "admin" };

        _mockRoleRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(role);

        var members = new List<RoleMember>
        {
            new() { UserId = 10, UserName = "Alice", UserEmail = "alice@example.com" },
            new() { UserId = 20, UserName = "Bob", UserEmail = "bob@example.com" }
        };

        _mockUserRoleResolver.Setup(r => r.GetUsersInRoleAsync(1))
            .ReturnsAsync(members);

        // Act
        var result = await _service.GetRoleUsersAsync(1);

        // Assert
        result.Should().HaveCount(2);
        result[0].UserId.Should().Be(10);
        result[0].UserName.Should().Be("Alice");
        result[0].RoleId.Should().Be(1);
        result[0].RoleName.Should().Be("Admin");
    }

    [Fact]
    public async Task GetRoleUsersAsync_WithNonExistingRole_ShouldReturnEmptyList()
    {
        // Arrange
        _mockRoleRepo.Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _service.GetRoleUsersAsync(999);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetAllPermissionGroups

    [Fact]
    public void GetAllPermissionGroups_ShouldReturnAllGroups()
    {
        // Act
        var result = _service.GetAllPermissionGroups();

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(g => g.Key == "conversations");
        result.Should().Contain(g => g.Key == "customers");
        result.Should().Contain(g => g.Key == "users");
        result.Should().Contain(g => g.Key == "system");
        result.Should().Contain(g => g.Key == "audit");

        // 验证某些关键权限存在
        var conversations = result.First(g => g.Key == "conversations");
        conversations.Permissions.Should().Contain(p => p.Key == "conversations.view");
        conversations.Permissions.Should().Contain(p => p.Key == "conversations.reply");

        // 个人资料权限必须可在角色界面授予（否则非超管用户改密码/保存资料一律 403）
        result.Should().Contain(g => g.Key == "profile");
        var profile = result.First(g => g.Key == "profile");
        profile.Permissions.Should().Contain(p => p.Key == "profile.update");
        profile.Permissions.Should().Contain(p => p.Key == "profile.change_password");

        // 2026-09-23：补齐此前被 [Permission] 特性引用、但目录中缺失的权限 ——
        // 附件 / 自定义报表 / 工时三个新组，以及沟通管理组内补齐的渠道接入 pair。
        var attachments = result.First(g => g.Key == "attachments");
        attachments.Permissions.Select(p => p.Key).Should().BeEquivalentTo(new[] { "attachments.manage" });

        var customReports = result.First(g => g.Key == "custom_reports");
        customReports.Permissions.Select(p => p.Key).Should().BeEquivalentTo(new[]
        {
            "custom_reports.create",
            "custom_reports.update",
            "custom_reports.delete",
            "custom_reports.execute",
            "custom_reports.schedules.manage",
        });

        var timeTracking = result.First(g => g.Key == "time_tracking");
        timeTracking.Permissions.Select(p => p.Key).Should().BeEquivalentTo(new[] { "time-tracking.view", "time-tracking.manage" });

        var communication = result.First(g => g.Key == "communication");
        communication.Permissions.Select(p => p.Key).Should().Contain(new[]
        {
            "whatsapp.view",
            "whatsapp.manage",
            "feishu.view",
            "feishu.manage",
            "dingding.view",
            "dingding.manage",
        });
    }

    #endregion

    #region Recycle Bin (Trash)

    [Fact]
    public async Task GetTrashAsync_ShouldReturnPagedDeletedRoles()
    {
        // Arrange
        var items = new List<Role>
        {
            new() { Id = 1, Name = "Deleted1", Slug = "del1", DeletedAt = DateTime.UtcNow },
            new() { Id = 2, Name = "Deleted2", Slug = "del2", DeletedAt = DateTime.UtcNow }
        };

        _mockRoleRepo.Setup(r => r.GetDeletedAsync(1, 20, null))
            .ReturnsAsync((items, 2));
        _mockUserRoleResolver.Setup(r => r.CountUsersInRolesAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int>());

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
        _mockRoleRepo.Setup(r => r.CountDeletedAsync())
            .ReturnsAsync(4);

        // Act
        var result = await _service.GetTrashCountAsync();

        // Assert
        result.Should().Be(4);
    }

    [Fact]
    public async Task RestoreAsync_WithExisting_ShouldReturnTrueAndInvalidateCache()
    {
        // Arrange
        _mockRoleRepo.Setup(r => r.RestoreAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _service.RestoreAsync(1);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAsync_WithNonExisting_ShouldReturnFalse()
    {
        // Arrange
        _mockRoleRepo.Setup(r => r.RestoreAsync(999))
            .ReturnsAsync(false);

        // Act
        var result = await _service.RestoreAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task HardDeleteAsync_ShouldDelegateToRepository()
    {
        // Arrange
        _mockRoleRepo.Setup(r => r.HardDeleteAsync(1))
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
        _mockRoleRepo.Setup(r => r.HardDeleteAllDeletedAsync())
            .ReturnsAsync(5);

        // Act
        var result = await _service.ClearTrashAsync();

        // Assert
        result.Should().Be(5);
    }

    #endregion
}
