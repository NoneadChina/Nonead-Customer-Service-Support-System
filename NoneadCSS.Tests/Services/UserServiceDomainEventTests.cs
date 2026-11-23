using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// UserService 领域事件接线测试。
/// 验证改密成功与激活成功两条路径会分发强类型领域事件：
/// PasswordChangedEvent → SendPasswordChangedListener（站内通知 + 安全提醒邮件，此前完全没有等价实现）；
/// UserActivatedEvent → ActivateUserListener（只发「账号已激活」站内通知，激活动作本身已在服务层完成）。
/// </summary>
public class UserServiceDomainEventTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<IPasswordPolicyService> _mockPasswordPolicyService = new();
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver = new();
    private readonly Mock<IUserRepository> _mockUserRepository = new();
    private readonly Mock<IRoleRepository> _mockRoleRepository = new();
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction = new();
    private readonly Mock<ILogger<UserService>> _mockLogger = new();

    /// <summary>被接线进 UserService 的强类型领域事件分发器（本测试的核心断言对象）。</summary>
    private readonly Mock<IDomainEventDispatcher> _mockDispatcher = new();

    private readonly UserService _userService;

    private const string ValidPassword = "TestPassword123!";

    public UserServiceDomainEventTests()
    {
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepository.Object);
        _mockUnitOfWork.Setup(u => u.Roles).Returns(_mockRoleRepository.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        // UserService 走的是 IUnitOfWork 的无参重载（NoneadCSS.Domain/IUnitOfWork.cs:69），
        // 带 CancellationToken 的是另一个重载（:75）：Setup/Verify 必须打在服务真正调用的那个重载上。
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);

        _userService = new UserService(
            _mockUnitOfWork.Object,
            _mockPasswordPolicyService.Object,
            _mockUserRoleResolver.Object,
            currentUserAccessor: null,
            logger: _mockLogger.Object,
            domainEventDispatcher: _mockDispatcher.Object);
    }

    private void SetupUsers(params User[] users)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(users.ToList());
        _mockUserRepository.Setup(r => r.GetQueryable(false)).Returns(mockDbSet.Object);
    }

    private static User BuildUser(UserStatus status = UserStatus.Active) => new()
    {
        Id = 1,
        Email = "user@example.com",
        Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
        FirstName = "Test",
        LastName = "User",
        Role = UserRoleType.Agent,
        Status = status
    };

    [Fact]
    public async Task ChangePasswordAsync_Should_DispatchPasswordChangedEvent_When_PasswordChangedSuccessfully()
    {
        // Arrange
        var user = BuildUser();
        SetupUsers(user);
        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync("NewPassword123!"))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true });

        PasswordChangedEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<PasswordChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PasswordChangedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = ValidPassword,
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };

        // Act
        var result = await _userService.ChangePasswordAsync(user.Id, request);

        // Assert：事件在保存之后发布，携带已更新的用户实体
        result.Should().BeTrue();
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        captured.Should().NotBeNull();
        captured!.User.Should().BeSameAs(user);
        captured.User.Email.Should().Be(user.Email);
    }

    [Fact]
    public async Task ChangePasswordAsync_Should_NotDispatchPasswordChangedEvent_When_CurrentPasswordIsWrong()
    {
        // Arrange
        var user = BuildUser();
        SetupUsers(user);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "WrongPassword!",
            NewPassword = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        };

        // Act
        var result = await _userService.ChangePasswordAsync(user.Id, request);

        // Assert：改密未成功，不能发布事件（否则会给用户发误报的安全提醒邮件）
        result.Should().BeFalse();
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<PasswordChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangePasswordAsync_Should_NotDispatchPasswordChangedEvent_When_NewPasswordViolatesPolicy()
    {
        // Arrange
        var user = BuildUser();
        SetupUsers(user);
        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync("weak"))
            .ReturnsAsync(new PasswordValidationResult { IsValid = false, Errors = new List<string> { "密码强度不足" } });

        var request = new ChangePasswordRequest
        {
            CurrentPassword = ValidPassword,
            NewPassword = "weak",
            ConfirmPassword = "weak"
        };

        // Act
        var act = () => _userService.ChangePasswordAsync(user.Id, request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*密码强度不足*");
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<PasswordChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivateUserAsync_Should_DispatchUserActivatedEvent_AfterSaving()
    {
        // Arrange：待激活用户
        var user = BuildUser(UserStatus.PendingActivation);
        SetupUsers(user);

        UserActivatedEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<UserActivatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserActivatedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act
        await _userService.ActivateUserAsync(user.Id);

        // Assert：激活已在服务层完成并保存，事件只用于站内通知
        user.Status.Should().Be(UserStatus.Active);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task ActivateUserAsync_Should_NotDispatchUserActivatedEvent_When_UserNotFound()
    {
        // Arrange
        SetupUsers();

        // Act
        await _userService.ActivateUserAsync(999);

        // Assert
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<UserActivatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDispatcher.VerifyNoOtherCalls();
    }
}
