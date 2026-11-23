using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// UserService 单元测试
/// </summary>
public class UserServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IPasswordPolicyService> _mockPasswordPolicyService;
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver;
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IRoleRepository> _mockRoleRepository;
    private readonly Mock<IUserRoleRepository> _mockUserRoleRepository;
    private readonly Mock<IMailboxRepository> _mockMailboxRepository;
    private readonly Mock<IConversationRepository> _mockConversationRepository;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    private readonly UserService _userService;

    private const string ValidPassword = "TestPassword123!";

    public UserServiceTests()
    {
        // 初始化所有 mock 对象
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockPasswordPolicyService = new Mock<IPasswordPolicyService>();
        _mockUserRoleResolver = new Mock<IUserRoleResolver>();
        _mockUserRepository = new Mock<IUserRepository>();
        _mockRoleRepository = new Mock<IRoleRepository>();
        _mockUserRoleRepository = new Mock<IUserRoleRepository>();
        _mockMailboxRepository = new Mock<IMailboxRepository>();
        _mockConversationRepository = new Mock<IConversationRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        // 配置 UnitOfWork 返回各个 repository
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepository.Object);
        _mockUnitOfWork.Setup(u => u.Roles).Returns(_mockRoleRepository.Object);
        _mockUnitOfWork.Setup(u => u.UserRoles).Returns(_mockUserRoleRepository.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepository.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepository.Object);

        // 配置 SaveChangesAsync（两个重载都设置，覆盖参数化与无参调用）
        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);
        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync())
            .ReturnsAsync(1);

        // M10: 用户创建+角色分配在事务中完成，模拟事务提交/回滚
        _mockUnitOfWork
            .Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(_mockTransaction.Object);

        // 创建 UserService 实例
        _userService = new UserService(
            _mockUnitOfWork.Object,
            _mockPasswordPolicyService.Object,
            _mockUserRoleResolver.Object);
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock User DbSet
    /// </summary>
    private void SetupUserRepositoryQueryable(params User[] users)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(users.ToList());
        _mockUserRepository
            .Setup(r => r.GetQueryable(false))
            .Returns(mockDbSet.Object);
    }

    #region GetUserByIdAsync Tests

    [Fact]
    public async Task GetUserByIdAsync_Should_ReturnUser_When_UserExists()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active
        };

        _mockUserRepository
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(user);

        // Act
        var result = await _userService.GetUserByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Email.Should().Be("test@example.com");
        result.FirstName.Should().Be("Test");
        result.LastName.Should().Be("User");
    }

    [Fact]
    public async Task GetUserByIdAsync_Should_ReturnNull_When_UserDoesNotExist()
    {
        // Arrange
        _mockUserRepository
            .Setup(r => r.GetByIdAsync(999))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _userService.GetUserByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_Should_ReturnNull_When_EmailAlreadyExists()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            FirstName = "New",
            LastName = "User",
            Email = "existing@example.com",
            Password = ValidPassword,
            Role = 1
        };

        var existingUser = new User
        {
            Id = 1,
            Email = request.Email
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(existingUser);

        // Act
        var result = await _userService.CreateUserAsync(request);

        // Assert
        result.Should().BeNull();
        _mockUserRepository.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task CreateUserAsync_Should_ThrowException_When_PasswordDoesNotMeetPolicy()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            FirstName = "New",
            LastName = "User",
            Email = "new@example.com",
            Password = "weak",
            Role = 1
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(request.Password))
            .ReturnsAsync(new PasswordValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "密码长度至少8位", "密码必须包含数字" }
            });

        // Act
        var act = () => _userService.CreateUserAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*密码长度至少8位*");
    }

    [Fact]
    public async Task CreateUserAsync_Should_CreateUser_When_RequestIsValid()
    {
        // Arrange
        var request = new CreateUserRequest
        {
            FirstName = "New",
            LastName = "User",
            Email = "new@example.com",
            Password = ValidPassword,
            Role = 1 // ReadOnly 系统角色（1-4），不触发自定义角色查询
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(request.Password))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true });

        _mockUserRepository
            .Setup(r => r.AddAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => u);

        // Act
        var result = await _userService.CreateUserAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.Email.Should().Be(request.Email);
        result.FirstName.Should().Be("New");
        result.LastName.Should().Be("User");

        _mockUserRepository.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
    }

    #endregion

    #region UpdateUserAsync Tests

    [Fact]
    public async Task UpdateUserAsync_Should_ReturnNull_When_UserDoesNotExist()
    {
        // Arrange
        var request = new UpdateUserRequest
        {
            FirstName = "Updated",
            LastName = "User",
            Email = "updated@example.com",
            Role = 1
        };

        SetupUserRepositoryQueryable(); // 空查询结果

        // Act
        var result = await _userService.UpdateUserAsync(999, request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateUserAsync_Should_ReturnNull_When_EmailAlreadyUsedByAnotherUser()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "user1@example.com",
            FirstName = "User",
            LastName = "One",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active
        };

        SetupUserRepositoryQueryable(user);

        var existingUserWithEmail = new User
        {
            Id = 2, // 不同 ID 表示邮箱已被其他用户占用
            Email = "user2@example.com"
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync("user2@example.com"))
            .ReturnsAsync(existingUserWithEmail);

        var request = new UpdateUserRequest
        {
            FirstName = "Updated",
            LastName = "User",
            Email = "user2@example.com",
            Role = 1
        };

        // Act
        var result = await _userService.UpdateUserAsync(1, request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateUserAsync_Should_UpdateUser_When_RequestIsValid()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "old@example.com",
            FirstName = "Old",
            LastName = "Name",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active
        };

        SetupUserRepositoryQueryable(user);

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync("new@example.com"))
            .ReturnsAsync((User?)null);

        var request = new UpdateUserRequest
        {
            FirstName = "New",
            LastName = "Name",
            Email = "new@example.com",
            Role = 1 // 系统角色，不触发自定义角色查询
        };

        // Act
        var result = await _userService.UpdateUserAsync(1, request);

        // Assert
        result.Should().NotBeNull();
        result!.FirstName.Should().Be("New");
        result.LastName.Should().Be("Name");
        result.Email.Should().Be("new@example.com");

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region DeleteUserAsync Tests

    [Fact]
    public async Task DeleteUserAsync_Should_ReturnFalse_When_UserDoesNotExist()
    {
        // Arrange
        SetupUserRepositoryQueryable(); // 空查询结果

        // Act
        var (success, errorMessage) = await _userService.DeleteUserAsync(999);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteUserAsync_Should_SoftDeleteUser_When_UserExists()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "delete@example.com",
            FirstName = "Delete",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active
        };

        SetupUserRepositoryQueryable(user);

        // Act
        var (success, errorMessage) = await _userService.DeleteUserAsync(1);

        // Assert
        success.Should().BeTrue();
        errorMessage.Should().BeNull();
        user.Status.Should().Be(UserStatus.Deleted);
        user.DeletedAt.Should().NotBeNull();
        user.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    #endregion

    #region ChangePasswordAsync Tests

    [Fact]
    public async Task ChangePasswordAsync_Should_ReturnFalse_When_PasswordsDoNotMatch()
    {
        // Arrange
        var request = new ChangePasswordRequest
        {
            CurrentPassword = ValidPassword,
            NewPassword = "NewPassword123!",
            ConfirmPassword = "DifferentPassword123!"
        };

        // Act
        var result = await _userService.ChangePasswordAsync(1, request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_Should_ReturnFalse_When_UserDoesNotExist()
    {
        // Arrange
        var request = new ChangePasswordRequest
        {
            CurrentPassword = ValidPassword,
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };

        SetupUserRepositoryQueryable(); // 空查询结果

        // Act
        var result = await _userService.ChangePasswordAsync(1, request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_Should_ReturnFalse_When_CurrentPasswordIsIncorrect()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            Role = UserRoleType.Agent,
            Status = UserStatus.Active
        };

        SetupUserRepositoryQueryable(user);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "WrongPassword123!",
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };

        // Act
        var result = await _userService.ChangePasswordAsync(1, request);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_Should_ReturnTrue_When_PasswordChangedSuccessfully()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            Role = UserRoleType.Agent,
            Status = UserStatus.Active
        };

        SetupUserRepositoryQueryable(user);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync("NewPassword123!"))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true });

        var request = new ChangePasswordRequest
        {
            CurrentPassword = ValidPassword,
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };

        // Act
        var result = await _userService.ChangePasswordAsync(1, request);

        // Assert
        result.Should().BeTrue();
        user.Password.Should().NotBeEmpty();
        user.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_Should_ThrowException_When_NewPasswordDoesNotMeetPolicy()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            Role = UserRoleType.Agent,
            Status = UserStatus.Active
        };

        SetupUserRepositoryQueryable(user);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync("weak"))
            .ReturnsAsync(new PasswordValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "密码强度不足" }
            });

        var request = new ChangePasswordRequest
        {
            CurrentPassword = ValidPassword,
            NewPassword = "weak",
            ConfirmPassword = "weak"
        };

        // Act
        var act = () => _userService.ChangePasswordAsync(1, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*密码强度不足*");
    }

    #endregion

    #region GetAssignableUsersAsync Tests

    [Fact]
    public async Task GetAssignableUsersAsync_Should_ReturnOnlyActiveUsers()
    {
        // Arrange: Service 层通过 FindAsync 在数据库层过滤 Active 用户
        var activeUser1 = new User
        {
            Id = 1,
            Email = "active1@example.com",
            FirstName = "Active",
            LastName = "One",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active
        };
        var activeUser2 = new User
        {
            Id = 2,
            Email = "active2@example.com",
            FirstName = "Active",
            LastName = "Two",
            Role = UserRoleType.AgentSupervisor,
            Status = UserStatus.Active
        };

        _mockUserRepository
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()))
            .ReturnsAsync(new List<User> { activeUser1, activeUser2 });

        // Act
        var result = await _userService.GetAssignableUsersAsync();

        // Assert: 返回数据库过滤后的 Active 用户
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().OnlyContain(u => u.Id == 1 || u.Id == 2);

        _mockUserRepository.Verify(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()), Times.Once);
    }

    [Fact]
    public async Task GetAssignableUsersAsync_Should_ReturnEmpty_When_NoActiveUsers()
    {
        // Arrange: 数据库无 Active 用户
        _mockUserRepository
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()))
            .ReturnsAsync(new List<User>());

        // Act
        var result = await _userService.GetAssignableUsersAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion
}
