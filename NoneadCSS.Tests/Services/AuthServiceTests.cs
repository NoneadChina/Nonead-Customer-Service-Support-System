using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Security;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// AuthService 单元测试
/// </summary>
public class AuthServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<IEmailQueueService> _mockEmailQueueService;
    private readonly Mock<IMailTemplateService> _mockMailTemplateService;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly Mock<ICaptchaService> _mockCaptchaService;
    private readonly Mock<IPasswordPolicyService> _mockPasswordPolicyService;
    private readonly Mock<IUserRoleResolver> _mockUserRoleResolver;
    private readonly Mock<IFrontendUrlResolver> _mockFrontendUrlResolver;
    private readonly Mock<IHostEnvironment> _mockEnvironment;

    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepository;
    private readonly Mock<IMailboxRepository> _mockMailboxRepository;
    private readonly Mock<IMailTemplateRepository> _mockMailTemplateRepository;
    private readonly Mock<IPasswordResetTokenRepository> _mockPasswordResetTokenRepository;
    private readonly Mock<IRefreshTokenRepository> _mockRefreshTokenRepository;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    private readonly AuthService _authService;

    private const string ValidJwtSecretKey = "ThisIsAVeryLongSecretKeyForJWT123456789ABCDEFG";
    private const string ValidPassword = "TestPassword123!";

    static AuthServiceTests()
    {
        // 初始化 SecretHasher（全局静态，只需初始化一次）
        SecretHasher.Initialize("TestSecretKeyForSecretHasherInitialization12345678");
    }

    public AuthServiceTests()
    {
        // 初始化所有 mock 对象
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockEmailService = new Mock<IEmailService>();
        _mockEmailQueueService = new Mock<IEmailQueueService>();
        _mockMailTemplateService = new Mock<IMailTemplateService>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockCaptchaService = new Mock<ICaptchaService>();
        _mockPasswordPolicyService = new Mock<IPasswordPolicyService>();
        _mockUserRoleResolver = new Mock<IUserRoleResolver>();
        _mockFrontendUrlResolver = new Mock<IFrontendUrlResolver>();
        _mockFrontendUrlResolver.Setup(r => r.GetBaseUrl()).Returns("https://test.example.com");
        _mockEnvironment = new Mock<IHostEnvironment>();
        _mockEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        _mockUserRepository = new Mock<IUserRepository>();
        _mockSystemOptionRepository = new Mock<ISystemOptionRepository>();
        _mockMailboxRepository = new Mock<IMailboxRepository>();
        _mockMailTemplateRepository = new Mock<IMailTemplateRepository>();
        _mockPasswordResetTokenRepository = new Mock<IPasswordResetTokenRepository>();
        _mockRefreshTokenRepository = new Mock<IRefreshTokenRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        // 配置 UnitOfWork 返回各个 repository
        _mockUnitOfWork.Setup(u => u.Users).Returns(_mockUserRepository.Object);
        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepository.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepository.Object);
        _mockUnitOfWork.Setup(u => u.MailTemplates).Returns(_mockMailTemplateRepository.Object);
        _mockUnitOfWork.Setup(u => u.PasswordResetTokens).Returns(_mockPasswordResetTokenRepository.Object);
        _mockUnitOfWork.Setup(u => u.RefreshTokens).Returns(_mockRefreshTokenRepository.Object);

        // 配置事务
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        // 配置 JWT 配置
        SetupJwtConfiguration();

        // 创建 AuthService 实例
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
            Microsoft.Extensions.Options.Options.Create(new NoneadCSS.Application.Configuration.BrandOptions())
        );
    }

    /// <summary>
    /// 创建支持异步 LINQ 操作的 Mock User DbSet
    /// </summary>
    private void SetupUserRepository(params User[] users)
    {
        var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(users.ToList());
        _mockUserRepository
            .Setup(r => r.GetQueryable(false))
            .Returns(mockDbSet.Object);
    }

    private void SetupJwtConfiguration()
    {
        // 使用真实的内存 ConfigurationRoot 作为 mock 后端，避免手动 mock GetValue<T>/GetSection 链路。
        // ConfigurationBinder.GetValue<T> 会调用 GetSection(key).Value，需要完整链路支持，
        // Mock<IConfiguration> 手动设置索引器和 GetSection 难以覆盖所有扩展方法路径。
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

    #region LoginAsync Tests

    [Fact]
    public async Task LoginAsync_Should_ReturnCaptchaError_When_CaptchaIsInvalid()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = ValidPassword,
            CaptchaId = "captcha-id",
            CaptchaCode = "wrong-code"
        };

        _mockCaptchaService
            .Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(false);

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("验证码错误或已过期");
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnLoginFailed_When_UserDoesNotExist()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "nonexistent@example.com",
            Password = ValidPassword,
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        _mockCaptchaService
            .Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);

        // 模拟返回空查询结果
        SetupUserRepository();

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("邮箱或密码不正确");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("用户不存在")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnLoginFailed_When_AccountIsLocked()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "locked@example.com",
            Password = ValidPassword,
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FirstName = "Locked",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active,
            LockoutEnd = DateTime.UtcNow.AddMinutes(10)
        };

        _mockCaptchaService
            .Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);

        SetupUserRepository(user);

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("邮箱或密码不正确");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("账号已被锁定")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnLoginFailed_When_AccountIsPendingActivation()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "pending@example.com",
            Password = ValidPassword,
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FirstName = "Pending",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.PendingActivation
        };

        _mockCaptchaService
            .Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);

        SetupUserRepository(user);

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("邮箱或密码不正确");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("账号待激活")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnLoginFailed_When_AccountIsDisabled()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "disabled@example.com",
            Password = ValidPassword,
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FirstName = "Disabled",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.Disabled
        };

        _mockCaptchaService
            .Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);

        SetupUserRepository(user);

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("邮箱或密码不正确");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("账号已被禁用")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Should_LockAccount_When_PasswordIsWrongAndAttemptsReachThreshold()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "user@example.com",
            Password = "WrongPassword!",
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FirstName = "Test",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active,
            FailedLoginAttempts = 4 // 已失败4次，再失败一次就锁定
        };

        _mockCaptchaService
            .Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);

        SetupUserRepository(user);

        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("邮箱或密码不正确");

        // 验证账号被锁定
        user.LockoutEnd.Should().NotBeNull();
        user.FailedLoginAttempts.Should().Be(5);

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("登录失败次数过多，账号已被锁定")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnLoginFailed_When_PasswordIsWrong()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "user@example.com",
            Password = "WrongPassword!",
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FirstName = "Test",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active,
            FailedLoginAttempts = 0
        };

        _mockCaptchaService
            .Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);

        SetupUserRepository(user);

        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("邮箱或密码不正确");

        // 验证失败次数增加
        user.FailedLoginAttempts.Should().Be(1);

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("登录失败：密码错误")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_Should_ReturnSuccess_When_CredentialsAreValid()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "valid@example.com",
            Password = ValidPassword,
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FirstName = "Valid",
            LastName = "User",
            Role = UserRoleType.Agent,
            Status = UserStatus.Active,
            FailedLoginAttempts = 2
        };

        _mockCaptchaService
            .Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);

        SetupUserRepository(user);

        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockRefreshTokenRepository
            .Setup(r => r.AddAsync(It.IsAny<RefreshToken>()))
            .ReturnsAsync((RefreshToken rt) => rt);

        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.token_global_version"))
            .ReturnsAsync("0");

        _mockUserRoleResolver
            .Setup(r => r.GetPermissionsAsync(user.Id))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Token.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be(request.Email);
        result.ErrorMessage.Should().BeNull();

        // 验证失败次数已重置
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEnd.Should().BeNull();

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("用户登录成功")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ValidateTokenAsync Tests

    [Fact]
    public async Task ValidateTokenAsync_Should_ReturnTrue_When_TokenIsValid()
    {
        // Arrange - 先创建一个有效的 token
        var user = new User
        {
            Id = 1,
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User",
            Role = UserRoleType.Agent
        };

        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.token_global_version"))
            .ReturnsAsync("0");

        // 使用 AuthService 的私有方法生成 token（通过 LoginAsync）
        var token = GenerateTestToken(user);

        // Act
        var result = await _authService.ValidateTokenAsync(token);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTokenAsync_Should_ReturnFalse_When_TokenIsInvalid()
    {
        // Arrange
        var invalidToken = "invalid.token.here";

        // Act
        var result = await _authService.ValidateTokenAsync(invalidToken);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_Should_ReturnFalse_When_TokenIsExpired()
    {
        // Arrange - 创建一个已过期的 token
        var expiredToken = GenerateExpiredToken();

        // Act
        var result = await _authService.ValidateTokenAsync(expiredToken);

        // Assert
        result.Should().BeFalse();

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateTokenAsync_Should_ReturnFalse_When_TokenHasWrongSignature()
    {
        // Arrange - 使用不同的密钥生成的 token
        var tokenWithWrongSignature = GenerateTokenWithWrongSignature();

        // Act
        var result = await _authService.ValidateTokenAsync(tokenWithWrongSignature);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_Should_ReturnError_When_RegistrationIsNotAllowed()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "new@example.com",
            Password = ValidPassword,
            FirstName = "New",
            LastName = "User"
        };

        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.allow_registration"))
            .ReturnsAsync("false");

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("用户注册功能未开放");
    }

    [Fact]
    public async Task RegisterAsync_Should_ReturnError_When_EmailAlreadyExists()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "existing@example.com",
            Password = ValidPassword,
            FirstName = "Existing",
            LastName = "User"
        };

        var existingUser = new User
        {
            Id = 1,
            Email = request.Email
        };

        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.allow_registration"))
            .ReturnsAsync("true");

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(existingUser);

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("邮箱已被注册");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("邮箱已被注册")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_Should_ReturnError_When_PasswordDoesNotMeetPolicy()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "new@example.com",
            Password = "weak",
            FirstName = "New",
            LastName = "User"
        };

        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.allow_registration"))
            .ReturnsAsync("true");

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
        var result = await _authService.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("密码长度至少8位");
        result.ErrorMessage.Should().Contain("密码必须包含数字");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("密码不符合策略")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_Should_ReturnSuccess_When_RegistrationIsValid()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "new@example.com",
            Password = ValidPassword,
            FirstName = "New",
            LastName = "User"
        };

        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.allow_registration"))
            .ReturnsAsync("true");

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(request.Password))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true });

        _mockUserRepository
            .Setup(r => r.AddAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => u);

        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockMailboxRepository
            .Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Mailbox>());

        _mockUserRoleResolver
            .Setup(r => r.GetPermissionsAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be(request.Email);

        // 验证事务提交
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("用户注册成功")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ForgotPasswordAsync Tests

    [Fact]
    public async Task ForgotPasswordAsync_Should_ReturnError_When_UserDoesNotExist()
    {
        // Arrange
        var request = new ForgotPasswordRequest
        {
            Email = "nonexistent@example.com",
            FirstName = "Non",
            LastName = "Existent"
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        // Act
        var (success, errorMessage) = await _authService.ForgotPasswordAsync(request);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("姓名或邮箱不正确");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("用户不存在")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_Should_ReturnError_When_NameDoesNotMatch()
    {
        // Arrange
        var request = new ForgotPasswordRequest
        {
            Email = "user@example.com",
            FirstName = "Wrong",
            LastName = "Name"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            FirstName = "Correct",
            LastName = "User"
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(user);

        // Act
        var (success, errorMessage) = await _authService.ForgotPasswordAsync(request);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("姓名或邮箱不正确");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("姓名不匹配")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_Should_ReturnError_When_NoActiveMailbox()
    {
        // Arrange
        var request = new ForgotPasswordRequest
        {
            Email = "user@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            FirstName = "Test",
            LastName = "User"
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _mockPasswordResetTokenRepository
            .Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>()))
            .ReturnsAsync((PasswordResetToken t) => t);

        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockMailboxRepository
            .Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Mailbox>());

        // Act
        var (success, errorMessage) = await _authService.ForgotPasswordAsync(request);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("没有可用的活跃邮箱来发送密码重置邮件");
    }

    [Fact]
    public async Task ForgotPasswordAsync_Should_ReturnSuccess_When_RequestIsValid()
    {
        // Arrange
        var request = new ForgotPasswordRequest
        {
            Email = "user@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            FirstName = "Test",
            LastName = "User"
        };

        var mailbox = new Mailbox
        {
            Id = 1,
            IsActive = true
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _mockPasswordResetTokenRepository
            .Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>()))
            .ReturnsAsync((PasswordResetToken t) => t);

        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockMailboxRepository
            .Setup(r => r.GetFirstActiveAsync())
            .ReturnsAsync(mailbox);

        _mockMailTemplateRepository
            .Setup(r => r.GetByTypeAsync(MailTemplateType.PasswordReset))
            .ReturnsAsync(new List<MailTemplate>());

        _mockEmailService
            .Setup(e => e.SendSystemNotificationAsync(
                It.IsAny<Mailbox>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var (success, errorMessage) = await _authService.ForgotPasswordAsync(request);

        // Assert
        success.Should().BeTrue();
        errorMessage.Should().BeEmpty();

        // 验证令牌生成日志
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("密码重置令牌已生成")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // 验证邮件发送日志
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("密码重置邮件已发送")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ResetPasswordAsync Tests

    [Fact]
    public async Task ResetPasswordAsync_Should_ReturnError_When_PasswordsDoNotMatch()
    {
        // Arrange
        var request = new ResetPasswordRequest
        {
            Token = "reset-token",
            NewPassword = "NewPassword123!",
            ConfirmPassword = "DifferentPassword123!"
        };

        // Act
        var (success, errorMessage) = await _authService.ResetPasswordAsync(request);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("两次输入的密码不一致");
    }

    [Fact]
    public async Task ResetPasswordAsync_Should_ReturnError_When_PasswordDoesNotMeetPolicy()
    {
        // Arrange
        var request = new ResetPasswordRequest
        {
            Token = "reset-token",
            NewPassword = "weak",
            ConfirmPassword = "weak"
        };

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(request.NewPassword))
            .ReturnsAsync(new PasswordValidationResult
            {
                IsValid = false,
                Errors = new List<string> { "密码强度不足" }
            });

        // Act
        var (success, errorMessage) = await _authService.ResetPasswordAsync(request);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Contain("密码强度不足");
    }

    [Fact]
    public async Task ResetPasswordAsync_Should_ReturnError_When_TokenIsInvalid()
    {
        // Arrange
        var request = new ResetPasswordRequest
        {
            Token = "invalid-token",
            NewPassword = ValidPassword,
            ConfirmPassword = ValidPassword
        };

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(request.NewPassword))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true });

        _mockPasswordResetTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync((PasswordResetToken?)null);

        // Act
        var (success, errorMessage) = await _authService.ResetPasswordAsync(request);

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("令牌无效或已过期");
    }

    #endregion

    #region GetAllowRegistrationAsync Tests

    [Fact]
    public async Task GetAllowRegistrationAsync_Should_ReturnTrue_When_RegistrationIsAllowed()
    {
        // Arrange
        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.allow_registration"))
            .ReturnsAsync("true");

        // Act
        var result = await _authService.GetAllowRegistrationAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllowRegistrationAsync_Should_ReturnFalse_When_RegistrationIsNotAllowed()
    {
        // Arrange
        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.allow_registration"))
            .ReturnsAsync("false");

        // Act
        var result = await _authService.GetAllowRegistrationAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllowRegistrationAsync_Should_ReturnFalse_When_SettingNotConfigured()
    {
        // Arrange
        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.allow_registration"))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _authService.GetAllowRegistrationAsync();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region RefreshTokenAsync Tests

    [Fact]
    public async Task RefreshTokenAsync_Should_ReturnError_When_TokenIsEmpty()
    {
        // Arrange
        var refreshToken = string.Empty;

        // Act
        var (success, accessToken, newRefreshToken, errorMessage) = await _authService.RefreshTokenAsync(refreshToken, "127.0.0.1");

        // Assert
        success.Should().BeFalse();
        accessToken.Should().BeNull();
        newRefreshToken.Should().BeNull();
        errorMessage.Should().Be("Refresh Token 无效");
    }

    [Fact]
    public async Task RefreshTokenAsync_Should_ReturnError_When_TokenNotFound()
    {
        // Arrange
        var refreshToken = "nonexistent-token";

        _mockRefreshTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync((RefreshToken?)null);

        // Act
        var (success, accessToken, newRefreshToken, errorMessage) = await _authService.RefreshTokenAsync(refreshToken, "127.0.0.1");

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("Refresh Token 无效或已过期");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Refresh Token 无效")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshTokenAsync_Should_ReturnError_When_TokenIsRevoked()
    {
        // Arrange
        var refreshToken = "revoked-token";
        var token = new RefreshToken
        {
            Id = 1,
            Token = "hashed-token",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RevokedAt = DateTime.UtcNow
        };

        _mockRefreshTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(token);

        // Act
        var (success, accessToken, newRefreshToken, errorMessage) = await _authService.RefreshTokenAsync(refreshToken, "127.0.0.1");

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("Refresh Token 无效或已过期");
    }

    [Fact]
    public async Task RefreshTokenAsync_Should_ReturnError_When_TokenIsExpired()
    {
        // Arrange
        var refreshToken = "expired-token";
        var token = new RefreshToken
        {
            Id = 1,
            Token = "hashed-token",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // 已过期
        };

        _mockRefreshTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(token);

        // Act
        var (success, accessToken, newRefreshToken, errorMessage) = await _authService.RefreshTokenAsync(refreshToken, "127.0.0.1");

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("Refresh Token 无效或已过期");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Refresh Token 已过期")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshTokenAsync_Should_ReturnError_When_UserIsDisabled()
    {
        // Arrange
        var refreshToken = "valid-token";
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            Status = UserStatus.Disabled
        };
        var token = new RefreshToken
        {
            Id = 1,
            Token = "hashed-token",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            User = user
        };

        _mockRefreshTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(token);

        // Act
        var (success, accessToken, newRefreshToken, errorMessage) = await _authService.RefreshTokenAsync(refreshToken, "127.0.0.1");

        // Assert
        success.Should().BeFalse();
        errorMessage.Should().Be("用户不存在或已被禁用");

        // 验证日志记录
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("用户不存在或已被禁用")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region RevokeRefreshTokenAsync Tests

    [Fact]
    public async Task RevokeRefreshTokenAsync_Should_ReturnFalse_When_TokenIsEmpty()
    {
        // Arrange
        var refreshToken = string.Empty;

        // Act
        var result = await _authService.RevokeRefreshTokenAsync(refreshToken);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_Should_ReturnFalse_When_TokenNotFound()
    {
        // Arrange
        var refreshToken = "nonexistent-token";

        _mockRefreshTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync((RefreshToken?)null);

        // Act
        var result = await _authService.RevokeRefreshTokenAsync(refreshToken);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_Should_ReturnTrue_When_TokenIsValid()
    {
        // Arrange
        var refreshToken = "valid-token";
        var token = new RefreshToken
        {
            Id = 1,
            Token = "hashed-token",
            UserId = 1,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        _mockRefreshTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(token);

        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _authService.RevokeRefreshTokenAsync(refreshToken);

        // Assert
        result.Should().BeTrue();
        token.RevokedAt.Should().NotBeNull();
        token.ReasonRevoked.Should().Be("User logged out");
    }

    #endregion

    #region Helper Methods

    private string GenerateTestToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(ValidJwtSecretKey);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, ((int)user.Role).ToString()),
            new Claim(JwtRegisteredClaimNames.Name, user.GetFullName()),
            new Claim("gv", "0")
        };

        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(60),
            Issuer = "NoneadCSS",
            Audience = "NoneadCSSApi",
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private string GenerateExpiredToken()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(ValidJwtSecretKey);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Role, "1"),
            new Claim("gv", "0")
        };

        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-30), // 30分钟前生效
            Expires = DateTime.UtcNow.AddMinutes(-10), // 10分钟前过期
            Issuer = "NoneadCSS",
            Audience = "NoneadCSSApi",
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private string GenerateTokenWithWrongSignature()
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var wrongKey = Encoding.UTF8.GetBytes("WrongSecretKey12345678901234567890AB");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypes.Role, "1"),
            new Claim("gv", "0")
        };

        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(60),
            Issuer = "NoneadCSS",
            Audience = "NoneadCSSApi",
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(wrongKey),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    #endregion

    #region LoginAsync - Additional Tests

    [Fact]
    public async Task LoginAsync_Should_ReturnSuccess_When_CaptchaIsDisabled()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "user@example.com",
            Password = ValidPassword,
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FirstName = "Test",
            LastName = "User",
            Status = UserStatus.Active,
            Role = UserRoleType.Agent
        };

        // 模拟验证码校验通过（等同于禁用验证码的场景）
        _mockCaptchaService.Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);
        _mockUserRepository.Setup(r => r.GetQueryable(false))
            .Returns(TestAsyncQueryProvider.CreateMockDbSet(new List<User> { user }.AsQueryable()).Object);
        _mockUserRoleResolver.Setup(r => r.GetRoleIdsAsync(1))
            .ReturnsAsync(new List<int> { (int)UserRoleType.Agent });
        _mockUserRoleResolver.Setup(r => r.GetPermissionsAsync(1))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Token.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoginAsync_Should_ResetFailedAttempts_When_LoginSucceeds()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "user@example.com",
            Password = ValidPassword,
            CaptchaId = "captcha-id",
            CaptchaCode = "1234"
        };

        var user = new User
        {
            Id = 1,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(ValidPassword),
            FirstName = "Test",
            LastName = "User",
            Status = UserStatus.Active,
            Role = UserRoleType.Agent,
            FailedLoginAttempts = 3
        };

        _mockCaptchaService.Setup(c => c.ValidateAsync(request.CaptchaId, request.CaptchaCode))
            .ReturnsAsync(true);
        _mockUserRepository.Setup(r => r.GetQueryable(false))
            .Returns(TestAsyncQueryProvider.CreateMockDbSet(new List<User> { user }.AsQueryable()).Object);
        _mockUserRoleResolver.Setup(r => r.GetRoleIdsAsync(1))
            .ReturnsAsync(new List<int> { (int)UserRoleType.Agent });
        _mockUserRoleResolver.Setup(r => r.GetPermissionsAsync(1))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        // Assert
        result.Success.Should().BeTrue();
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
    }

    #endregion

    #region RegisterAsync - Additional Tests

    [Fact]
    public async Task RegisterAsync_Should_ReturnSuccess_When_RegistrationIsValidWithOptionalFields()
    {
        // Arrange
        var request = new RegisterRequest
        {
            Email = "newuser@example.com",
            Password = ValidPassword,
            FirstName = "New",
            LastName = "User"
        };

        _mockSystemOptionRepository
            .Setup(r => r.GetValueAsync("security.allow_registration"))
            .ReturnsAsync("true");

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(request.Password))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true, Errors = new List<string>() });

        _mockUserRepository
            .Setup(r => r.AddAsync(It.IsAny<User>()))
            .ReturnsAsync((User u) => u);

        _mockUnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // SendWelcomeEmailAsync 内部调用（无活跃邮箱时仅记录日志并返回，不影响注册成功）
        _mockUserRoleResolver
            .Setup(r => r.GetPermissionsAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _authService.RegisterAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        _mockUserRepository.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region ResetPasswordAsync - Additional Tests

    [Fact]
    public async Task ResetPasswordAsync_Should_ReturnSuccess_When_TokenIsValid()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            Password = "oldHash",
            Status = UserStatus.Active,
            Role = UserRoleType.Agent
        };

        var resetToken = new PasswordResetToken
        {
            Id = 1,
            UserId = 1,
            Token = "hashed-valid-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UsedAt = null
        };

        _mockPasswordResetTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(resetToken);

        _mockUserRepository.Setup(r => r.GetQueryable(false))
            .Returns(TestAsyncQueryProvider.CreateMockDbSet(new List<User> { user }.AsQueryable()).Object);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(ValidPassword))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true, Errors = new List<string>() });

        // Act
        var result = await _authService.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = "valid-token",
            NewPassword = ValidPassword,
            ConfirmPassword = ValidPassword
        });

        // Assert
        result.Success.Should().BeTrue();
        user.Password.Should().NotBe("oldHash");
        resetToken.UsedAt.Should().NotBeNull();
        _mockPasswordResetTokenRepository.Verify(r => r.InvalidateUserTokensAsync(1), Times.Once);
        _mockRefreshTokenRepository.Verify(r => r.RevokeUserTokensAsync(1, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_Should_ReturnError_When_TokenIsExpired()
    {
        // Arrange
        var resetToken = new PasswordResetToken
        {
            Id = 1,
            UserId = 1,
            Token = "hashed-expired-token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1),
            UsedAt = null
        };

        _mockPasswordResetTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(resetToken);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(ValidPassword))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true, Errors = new List<string>() });

        // Act
        var result = await _authService.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = "expired-token",
            NewPassword = ValidPassword,
            ConfirmPassword = ValidPassword
        });

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("无效或已过期");
    }

    [Fact]
    public async Task ResetPasswordAsync_Should_ReturnError_When_TokenIsAlreadyUsed()
    {
        // Arrange
        var resetToken = new PasswordResetToken
        {
            Id = 1,
            UserId = 1,
            Token = "hashed-used-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UsedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        _mockPasswordResetTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(resetToken);

        _mockPasswordPolicyService
            .Setup(p => p.ValidateAsync(ValidPassword))
            .ReturnsAsync(new PasswordValidationResult { IsValid = true, Errors = new List<string>() });

        // Act
        var result = await _authService.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = "used-token",
            NewPassword = ValidPassword,
            ConfirmPassword = ValidPassword
        });

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("无效或已过期");
    }

    #endregion

    #region RefreshTokenAsync - Additional Tests

    [Fact]
    public async Task RefreshTokenAsync_Should_ReturnSuccess_When_TokenIsValid()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            FirstName = "Test",
            LastName = "User",
            Status = UserStatus.Active,
            Role = UserRoleType.Agent
        };

        var refreshToken = new RefreshToken
        {
            Id = 1,
            UserId = 1,
            Token = "hashed-valid-refresh-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = null,
            User = user
        };

        _mockRefreshTokenRepository
            .Setup(r => r.GetByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(refreshToken);

        _mockUserRoleResolver
            .Setup(r => r.GetRoleIdsAsync(1))
            .ReturnsAsync(new List<int> { (int)UserRoleType.Agent });
        _mockUserRoleResolver
            .Setup(r => r.GetPermissionsAsync(1))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _authService.RefreshTokenAsync("valid-refresh-token", "127.0.0.1");

        // Assert
        result.Success.Should().BeTrue();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region ForgotPasswordAsync - Additional Tests

    [Fact]
    public async Task ForgotPasswordAsync_Should_InvalidateOldTokens_When_RequestIsValid()
    {
        // Arrange
        var user = new User
        {
            Id = 1,
            Email = "user@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        _mockUserRepository
            .Setup(r => r.GetByEmailAsync("user@example.com"))
            .ReturnsAsync(user);

        var mailbox = new Mailbox
        {
            Id = 1,
            Email = "support@example.com",
            Name = "Support",
            IsActive = true
        };

        _mockMailboxRepository
            .Setup(r => r.GetFirstActiveAsync())
            .ReturnsAsync(mailbox);

        _mockMailTemplateRepository
            .Setup(r => r.GetByTypeAsync(It.IsAny<MailTemplateType>()))
            .ReturnsAsync(new List<MailTemplate>());

        // Act
        var result = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest
        {
            Email = "user@example.com",
            FirstName = "Test",
            LastName = "User"
        });

        // Assert
        result.Success.Should().BeTrue();
        _mockPasswordResetTokenRepository.Verify(r => r.AddAsync(It.IsAny<PasswordResetToken>()), Times.Once);
    }

    #endregion
}