using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;
using System.Linq;

namespace NoneadCSS.Tests.Repositories;

/// <summary>
/// 用户仓储单元测试
/// </summary>
public class UserRepositoryTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private UserRepository _repository = null!;

    public Task InitializeAsync()
    {
        // 创建 In-Memory 数据库
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        _context = new NoneadCSSDbContext(options);
        _repository = new UserRepository(_context);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    #region GetByEmailAsync Tests

    [Fact]
    public async Task GetByEmailAsync_WithExistingEmail_ShouldReturnUser()
    {
        // Arrange
        var email = "test@example.com";
        var user = new User
        {
            Email = email,
            FirstName = "Test",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active,
            Password = "hashedPassword"
        };

        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByEmailAsync(email);

        // Assert
        result.Should().NotBeNull();
        result!.Email.Should().Be(email);
        result.FirstName.Should().Be("Test");
        result.LastName.Should().Be("User");
    }

    [Fact]
    public async Task GetByEmailAsync_WithNonExistingEmail_ShouldReturnNull()
    {
        // Arrange
        var email = "nonexistent@example.com";

        // Act
        var result = await _repository.GetByEmailAsync(email);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region EmailExistsAsync Tests

    [Fact]
    public async Task EmailExistsAsync_WithExistingEmail_ShouldReturnTrue()
    {
        // Arrange
        var email = "existing@example.com";
        var user = new User
        {
            Email = email,
            FirstName = "Existing",
            LastName = "User",
            Password = "hashedPassword"
        };

        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.EmailExistsAsync(email);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task EmailExistsAsync_WithNonExistingEmail_ShouldReturnFalse()
    {
        // Arrange
        var email = "nonexisting@example.com";

        // Act
        var result = await _repository.EmailExistsAsync(email);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetByRoleAsync Tests

    [Fact]
    public async Task GetByRoleAsync_WithExistingRole_ShouldReturnUsers()
    {
        // Arrange
        var users = new List<User>
        {
            new() { Email = "admin1@example.com", FirstName = "Admin1", LastName = "User", Role = UserRoleType.SuperAdmin, Password = "hash" },
            new() { Email = "admin2@example.com", FirstName = "Admin2", LastName = "User", Role = UserRoleType.SuperAdmin, Password = "hash" },
            new() { Email = "user1@example.com", FirstName = "User1", LastName = "User", Role = UserRoleType.Agent, Password = "hash" }
        };

        await _context.Users.AddRangeAsync(users);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByRoleAsync((int)UserRoleType.SuperAdmin);

        // Assert
        result.Should().HaveCount(2);
        result.All(u => u.Role == UserRoleType.SuperAdmin).Should().BeTrue();
    }

    [Fact]
    public async Task GetByRoleAsync_WithNonExistingRole_ShouldReturnEmptyList()
    {
        // Arrange - 不添加任何用户

        // Act
        var result = await _repository.GetByRoleAsync((int)UserRoleType.SuperAdmin);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_ShouldAddUserToDatabase()
    {
        // Arrange
        var user = new User
        {
            Email = "newuser@example.com",
            FirstName = "New",
            LastName = "User",
            Password = "hashedPassword",
            Role = UserRoleType.Agent
        };

        // Act
        await _repository.AddAsync(user);
        await _context.SaveChangesAsync();

        // Assert
        var savedUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == user.Email);
        savedUser.Should().NotBeNull();
        savedUser!.Email.Should().Be(user.Email);
    }

    #endregion

    #region GetDeletedAsync Tests

    [Fact]
    public async Task GetDeletedAsync_ShouldReturnEmptyWhenNoDeletedUsers()
    {
        // Arrange - add only active users
        var activeUser = new User
        {
            Email = "active@example.com",
            FirstName = "Active",
            LastName = "User",
            Password = "hash",
            DeletedAt = null
        };

        await _context.Users.AddAsync(activeUser);
        await _context.SaveChangesAsync();

        // Clear the change tracker to ensure fresh data
        _context.ChangeTracker.Clear();

        // Act - get deleted users (should be empty due to global query filter)
        var result = await _repository.GetDeletedAsync();
        
        // Assert - returns empty when no deleted users exist
        result.Should().BeEmpty();
    }

    #endregion

    #region HardDeleteAsync Tests

    [Fact(Skip = "In-Memory database does not support ExecuteDeleteAsync")]
    public async Task HardDeleteAsync_ShouldPermanentlyDeleteUser()
    {
        // Arrange
        var user = new User
        {
            Email = "toDelete@example.com",
            FirstName = "ToDelete",
            LastName = "User",
            Password = "hash",
            DeletedAt = DateTime.UtcNow
        };

        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        var userId = user.Id;

        // Clear the change tracker
        _context.ChangeTracker.Clear();

        // Act - HardDeleteAsync already calls SaveChangesAsync internally
        await _repository.HardDeleteAsync(userId);

        // Assert
        var deletedUser = await _context.Users.FindAsync(userId);
        deletedUser.Should().BeNull();
    }

    #endregion
}