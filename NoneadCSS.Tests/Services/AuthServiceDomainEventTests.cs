using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Security;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// AuthService 领域事件接线测试。
/// 验证登录失败 / 触发锁定 / 登录成功三条路径会分发强类型领域事件：
/// LoginFailedEvent 与 LockoutEvent 的监听器是审计里 action="loginfailed" / "lockout" 的唯一来源
///（AuditActionFilter 明确跳过 Auth 控制器的 login 动作），仪表盘「异常事件数」依赖这两个计数。
/// UserLoggedInEvent 的监听器负责把本次登录语言回写 Users.Locale。
/// </summary>
public class AuthServiceDomainEventTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly Mock<IConfiguration> _mockConfiguration = new();
    private readonly Mock<IEmailService> _mockEmailService = new();
    private readonly Mock<IEmailQueueService> _mockEmailQueueService = new();
    private readonly Mock<IMailTemplateService> _mockMailTemplateService = new();
    private readonly Mock<ILogger<AuthService>> _mockLogger = new();
    private readonly Mock<ICaptchaService> _mockCaptchaService = new();
    private readonly Mock<IPasswordPolicyService> _mockPasswordPolicyService = new();
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver = new();
    private readonly Mock<IFrontendUrlResolver> _mockFrontendUrlResolver = new();
    private readonly Mock<IHostEnvironment> _mockEnvironment = new();

    private readonly Mock<IUserRepository> _mockUserRepository = new();
    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepository = new();
    private readonly Mock<IMailboxRepository> _mockMailboxRepository = new();
    private readonly Mock<IMailTemplateRepository> _mockMailTemplateRepository = new();
    private readonly Mock<IPasswordResetTokenRepository> _mockPasswordResetTokenRepository = new();
    private readonly Mock<IRefreshTokenRepository> _mockRefreshTokenRepository = new();
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction = new();

    /// <summary>被接线进 AuthService 的强类型领域事件分发器（本测试的核心断言对象）。</summary>
    private readonly Mock<IDomainEventDispatcher> _mockDispatcher = new();

    private readonly AuthService _authService;

    private const string ValidJwtSecretKey = "ThisIsAVeryLongSecretKeyForJWT123456789ABCDEFG";
    private const string ValidPassword = "TestPassword123!";

    static AuthServiceDomainEventTests()
    {
        // 登录成功路径会哈希刷新令牌，SecretHasher 是全局静态，需先初始化
        SecretHasher.Initialize("TestSecretKeyForSecretHasherInitialization12345678");
    }

    public AuthServiceDomainEventTests()
    {
        _mockFrontendUrlResolver.Setup(r => r.GetBaseUrl()).Returns("https://test.example.com");
        _mockEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepository.Object);
        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepository.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepository.Object);
        _mockUnitOfWork.Setup(u => u.MailTemplates).Returns(_mockMailTemplateRepository.Object);
        _mockUnitOfWork.Setup(u => u.PasswordResetTokens).Returns(_mockPasswordResetTokenRepository.Object);
        _mockUnitOfWork.Setup(u => u.RefreshTokens).Returns(_mockRefreshTokenRepository.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        SetupJwtConfiguration();

        _authService = new AuthService(
            _mockUnitOfWork.Object,
            _mockConfiguration.Object,
            _mockEmailService.Object,
            _mockEmailQueueService.Object,
            _mockMailTemplateService.Object,
            _mockLogger.Object,
            _mockCaptchaService.Object,
            _mockPasswordPolicyService.Object,
            _mockUserRoleResolver.Object,
            _mockFrontendUrlResolver.Object,
            _mockEnvironment.Object,
            new NoneadCSS.Application.Templates.TemplateRenderer(),
            Microsoft.Extensions.Options.Options.Create(new NoneadCSS.Application.Configuration.BrandOptions()),
            _mockDispatcher.Object);
    }

    /// <summary>装配可按异步 LINQ 查询的用户仓储数据源。</summary>
    private void SetupUsers(params User[] users)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(users.ToList());
        _mockUserRepository.Setup(r => r.GetQueryable(false)).Returns(mockDbSet.Object);
    }

    /// <summary>登录成功路径需要可用的 Jwt 配置，用真实内存配置作为 mock 后端。</summary>
    private void SetupJwtConfiguration()
    {
        var realConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = ValidJwtSecretKey,
                ["Jwt:Issuer"] = "NoneadCSS",
                ["Jwt:Audience"] = "NoneadCSSApi",
                ["Jwt:AccessTokenExpireMinutes"] = "60",
                ["Jwt:RefreshTokenExpireDays"] = "7",
                ["App:BaseUrl"] = "https://test.example.com",
                ["App:CompanyName"] = "Test Company"
            })
            .Build();

        _mockConfiguration.Setup(c => c.GetSection(It.IsAny<string>()))
            .Returns<string>(key => realConfig.GetSection(key));
        _mockConfiguration.Setup(c => c[It.IsAny<string>()])
            .Returns<string?>(key => key != null ? realConfig[key] : null);
    }

    private LoginRequest BuildRequest(string email, string password) => new()
    {
        Email = email,
        Password = password,
        CaptchaId = "captcha-id",
        CaptchaCode = "1234"
    };

    private User BuildUser(string email, string? hashedPassword, int failedAttempts = 0) => new()
    {
        Id = 1,
        Email = email,
        Password = hashedPassword ?? string.Empty,
        FirstName = "Test",
        LastName = "User",
        Role = UserRoleType.Agent,
        Status = UserStatus.Active,
        FailedLoginAttempts = failedAttempts
    };

    [Fact]
    public async Task LoginAsync_Should_DispatchLoginFailedEvent_WithReasonAndContext_When_PasswordIsWrong()
    {
        // Arrange：先装好捕获，再触发一次密码错误登录
        var user = BuildUser("user@example.com", BCrypt.Net.BCrypt.HashPassword(ValidPassword));
        _mockCaptchaService.Setup(c => c.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        SetupUsers(user);

        LoginFailedEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<LoginFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<LoginFailedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act
        await _authService.LoginAsync(BuildRequest(user.Email, "WrongPassword!"), "203.0.113.7", "UnitTestAgent");

        // Assert：事件字段完整，供监听器写审计与 IP/UA 上下文
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(user.Id);
        captured.Email.Should().Be(user.Email);
        captured.Reason.Should().Be("密码错误");
        captured.IpAddress.Should().Be("203.0.113.7");
        captured.UserAgent.Should().Be("UnitTestAgent");
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<LockoutEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_Should_DispatchLockoutEvent_When_FailedAttemptsReachThreshold()
    {
        // Arrange：已失败 4 次，再错一次即达到默认阈值 5
        var user = BuildUser("user@example.com", BCrypt.Net.BCrypt.HashPassword(ValidPassword), failedAttempts: 4);
        _mockCaptchaService.Setup(c => c.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        SetupUsers(user);

        // Act
        var result = await _authService.LoginAsync(BuildRequest(user.Email, "WrongPassword!"), "203.0.113.7");

        // Assert：失败事件与锁定事件各一次，且失败计数已提交
        result.Success.Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(5);
        user.LockoutEnd.Should().NotBeNull();
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<LoginFailedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<LockoutEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Should_DispatchLockoutEvent_When_AccountIsAlreadyLocked()
    {
        // Arrange：处于锁定期内的账号
        var user = BuildUser("user@example.com", BCrypt.Net.BCrypt.HashPassword(ValidPassword), failedAttempts: 5);
        user.LockoutEnd = DateTime.UtcNow.AddMinutes(10);
        _mockCaptchaService.Setup(c => c.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        SetupUsers(user);

        // Act
        var result = await _authService.LoginAsync(BuildRequest(user.Email, ValidPassword), "203.0.113.7");

        // Assert：锁定期间的尝试也计入 action="lockout"，且不再重复发失败事件
        result.Success.Should().BeFalse();
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<LockoutEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<LoginFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_Should_DispatchLoginFailedEvent_WithNullUserId_When_UserNotFound()
    {
        // Arrange：邮箱不存在（此时没有 UserId 可记，审计只留尝试邮箱）
        SetupUsers();
        _mockCaptchaService.Setup(c => c.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        LoginFailedEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<LoginFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<LoginFailedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _authService.LoginAsync(BuildRequest("ghost@example.com", ValidPassword), "203.0.113.7");

        // Assert
        result.Success.Should().BeFalse();
        captured.Should().NotBeNull();
        captured!.UserId.Should().BeNull();
        captured.Email.Should().Be("ghost@example.com");
        captured.Reason.Should().Be("用户不存在");
    }

    [Fact]
    public async Task LoginAsync_Should_PreferRequestLocale_When_LoginSucceeds()
    {
        // Arrange：请求体带 Locale 时优先使用请求体的语言
        var user = BuildUser("valid@example.com", BCrypt.Net.BCrypt.HashPassword(ValidPassword), failedAttempts: 2);
        _mockCaptchaService.Setup(c => c.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        SetupUsers(user);
        _mockRefreshTokenRepository.Setup(r => r.AddAsync(It.IsAny<RefreshToken>())).ReturnsAsync((RefreshToken rt) => rt);
        _mockSystemOptionRepository.Setup(r => r.GetValueAsync("security.token_global_version")).ReturnsAsync("0");
        _mockUserRoleResolver.Setup(r => r.GetPermissionsAsync(user.Id)).ReturnsAsync(new List<string>());

        UserLoggedInEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserLoggedInEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act：请求体 en 覆盖控制器端解析出的 zh-Hans
        var request = BuildRequest(user.Email, ValidPassword);
        request.Locale = "en";
        var result = await _authService.LoginAsync(request, "203.0.113.7", "UnitTestAgent", "zh-Hans");

        // Assert
        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(user.Id);
        captured.Locale.Should().Be("en");
        captured.IpAddress.Should().Be("203.0.113.7");
        captured.UserAgent.Should().Be("UnitTestAgent");
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<LoginFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_Should_FallbackToRequestCultureLocale_When_RequestHasNoLocale()
    {
        // Arrange：请求体未带 Locale，应回落到控制器端传入的请求文化
        var user = BuildUser("valid@example.com", BCrypt.Net.BCrypt.HashPassword(ValidPassword));
        _mockCaptchaService.Setup(c => c.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        SetupUsers(user);
        _mockRefreshTokenRepository.Setup(r => r.AddAsync(It.IsAny<RefreshToken>())).ReturnsAsync((RefreshToken rt) => rt);
        _mockSystemOptionRepository.Setup(r => r.GetValueAsync("security.token_global_version")).ReturnsAsync("0");
        _mockUserRoleResolver.Setup(r => r.GetPermissionsAsync(user.Id)).ReturnsAsync(new List<string>());

        UserLoggedInEvent? captured = null;
        _mockDispatcher.Setup(d => d.DispatchAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserLoggedInEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _authService.LoginAsync(BuildRequest(user.Email, ValidPassword), "203.0.113.7", null, "zh-Hans");

        // Assert
        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Locale.Should().Be("zh-Hans");
    }

    [Fact]
    public async Task LoginAsync_Should_NotDispatchAnyDomainEvent_When_CaptchaIsInvalid()
    {
        // Arrange：验证码失败时没有用户上下文，不应产生审计类事件
        _mockCaptchaService.Setup(c => c.ValidateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
        SetupUsers();

        // Act
        var result = await _authService.LoginAsync(BuildRequest("user@example.com", ValidPassword), "203.0.113.7");

        // Assert
        result.Success.Should().BeFalse();
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<LoginFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDispatcher.Verify(d => d.DispatchAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDispatcher.VerifyNoOtherCalls();
    }
}
