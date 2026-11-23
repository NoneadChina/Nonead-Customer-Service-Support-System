using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// PasswordPolicyService 单元测试
/// 覆盖密码策略读取、缓存、校验逻辑、弱密码检测、常见模式识别
/// </summary>
public class PasswordPolicyServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepo;
    private readonly Mock<ILogger<PasswordPolicyService>> _mockLogger;
    private readonly IMemoryCache _cache;
    private readonly PasswordPolicyService _service;

    public PasswordPolicyServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockSystemOptionRepo = new Mock<ISystemOptionRepository>();
        _mockLogger = new Mock<ILogger<PasswordPolicyService>>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepo.Object);

        // 默认所有策略项返回 null，使服务使用默认值
        _mockSystemOptionRepo
            .Setup(r => r.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        _service = new PasswordPolicyService(
            _mockUnitOfWork.Object,
            _cache,
            _mockLogger.Object,
            Options.Create(new NoneadCSS.Application.Configuration.CacheOptions()));
    }

    #region GetPolicyAsync

    [Fact]
    public async Task GetPolicyAsync_WithNoStoredOptions_ShouldUseDefaults()
    {
        // Arrange - 默认 Setup 中已配置返回 null

        // Act
        var result = await _service.GetPolicyAsync();

        // Assert
        result.Should().NotBeNull();
        result.MinLength.Should().Be(10);
        result.RequireUppercase.Should().BeTrue();
        result.RequireLowercase.Should().BeTrue();
        result.RequireDigit.Should().BeTrue();
        result.RequireNonAlphanumeric.Should().BeTrue();
        result.CheckWeakPassword.Should().BeTrue();
        result.CheckCommonPatterns.Should().BeTrue();
        result.Requirements.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetPolicyAsync_WithCustomMinLength_ShouldUseCustomValue()
    {
        // Arrange
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync("security.password_min_length"))
            .ReturnsAsync("16");

        // Act
        var result = await _service.GetPolicyAsync();

        // Assert
        result.MinLength.Should().Be(16);
        result.Requirements.Should().Contain(r => r.Contains("16"));
    }

    [Fact]
    public async Task GetPolicyAsync_WithInvalidMinLength_ShouldFallbackToDefault()
    {
        // Arrange
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync("security.password_min_length"))
            .ReturnsAsync("not-a-number");

        // Act
        var result = await _service.GetPolicyAsync();

        // Assert
        result.MinLength.Should().Be(10);
    }

    [Fact]
    public async Task GetPolicyAsync_WithZeroMinLength_ShouldFallbackToDefault()
    {
        // Arrange
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync("security.password_min_length"))
            .ReturnsAsync("0");

        // Act
        var result = await _service.GetPolicyAsync();

        // Assert
        result.MinLength.Should().Be(10);
    }

    [Fact]
    public async Task GetPolicyAsync_WithNegativeMinLength_ShouldFallbackToDefault()
    {
        // Arrange
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync("security.password_min_length"))
            .ReturnsAsync("-5");

        // Act
        var result = await _service.GetPolicyAsync();

        // Assert
        result.MinLength.Should().Be(10);
    }

    [Fact]
    public async Task GetPolicyAsync_WithRequireUppercaseFalse_ShouldReflectFalse()
    {
        // Arrange
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync("security.password_requires_uppercase"))
            .ReturnsAsync("false");

        // Act
        var result = await _service.GetPolicyAsync();

        // Assert
        result.RequireUppercase.Should().BeFalse();
    }

    [Fact]
    public async Task GetPolicyAsync_WithRequireUppercaseTrue_ShouldReflectTrue()
    {
        // Arrange
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync("security.password_requires_uppercase"))
            .ReturnsAsync("true");

        // Act
        var result = await _service.GetPolicyAsync();

        // Assert
        result.RequireUppercase.Should().BeTrue();
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldCacheResult()
    {
        // Arrange
        var callCount = 0;
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string _) =>
            {
                callCount++;
                return null;
            });

        // Act
        await _service.GetPolicyAsync();
        await _service.GetPolicyAsync();
        await _service.GetPolicyAsync();

        // Assert - 只有第一次调用时查询数据库
        // Note: 7 个策略项 × 1 次 = 7 次数据库调用
        callCount.Should().BeLessOrEqualTo(7);
    }

    [Fact]
    public async Task GetPolicyAsync_AfterInvalidateCache_ShouldReloadFromStore()
    {
        // Arrange
        var callCount = 0;
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(It.IsAny<string>()))
            .ReturnsAsync((string _) =>
            {
                callCount++;
                return null;
            });

        // Act
        await _service.GetPolicyAsync(); // 加载并缓存
        var firstCount = callCount;
        _service.InvalidateCache();
        await _service.GetPolicyAsync(); // 应重新加载

        // Assert
        callCount.Should().BeGreaterThan(firstCount);
    }

    [Fact]
    public async Task GetPolicyAsync_WhenRepositoryThrows_ShouldFallbackToDefaults()
    {
        // Arrange
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("DB connection failed"));

        // Act
        var result = await _service.GetPolicyAsync();

        // Assert
        result.Should().NotBeNull();
        result.MinLength.Should().Be(10); // 默认值
        result.RequireUppercase.Should().BeTrue();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("读取密码策略失败")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ValidateAsync - Length

    [Fact]
    public async Task ValidateAsync_WithEmptyPassword_ShouldFailWithError()
    {
        // Act
        var result = await _service.ValidateAsync("");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("不能为空"));
    }

    [Fact]
    public async Task ValidateAsync_WithNullPassword_ShouldFailWithError()
    {
        // Act
        var result = await _service.ValidateAsync(null!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("不能为空"));
    }

    [Fact]
    public async Task ValidateAsync_WithTooShortPassword_ShouldFailWithError()
    {
        // Act - 默认策略要求 10 位
        var result = await _service.ValidateAsync("Aa1!aaaa");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("长度") && e.Contains("10"));
    }

    [Fact]
    public async Task ValidateAsync_WithExactMinLength_ShouldPassLengthCheck()
    {
        // Act - 满足所有要求（10 位、大写、小写、数字、特殊字符、无弱密码/模式）
        var result = await _service.ValidateAsync("Abc123!xyz");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region ValidateAsync - Character Requirements

    [Fact]
    public async Task ValidateAsync_WithoutUppercase_ShouldFailWithError()
    {
        // Act
        var result = await _service.ValidateAsync("abc123!xyz");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("大写字母"));
    }

    [Fact]
    public async Task ValidateAsync_WithoutLowercase_ShouldFailWithError()
    {
        // Act
        var result = await _service.ValidateAsync("ABC123!XYZ");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("小写字母"));
    }

    [Fact]
    public async Task ValidateAsync_WithoutDigit_ShouldFailWithError()
    {
        // Act
        var result = await _service.ValidateAsync("Abc!xyzAbc!");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("数字"));
    }

    [Fact]
    public async Task ValidateAsync_WithoutSpecialChar_ShouldFailWithError()
    {
        // Act
        var result = await _service.ValidateAsync("Abc123XyzAbc");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("特殊字符"));
    }

    [Fact]
    public async Task ValidateAsync_WithAllRequirementsMet_ShouldPass()
    {
        // Act
        var result = await _service.ValidateAsync("StrongP@ssw0rd!");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region ValidateAsync - Weak Password Detection

    [Fact]
    public async Task ValidateAsync_WithWeakPassword_ShouldFailWithError()
    {
        // Act - "P@ssw0rd" 满足长度和字符要求，但 lowercase 后 "p@ssw0rd" 在弱密码列表中
        var result = await _service.ValidateAsync("P@ssw0rd");

        // Assert - "p@ssw0rd" 是弱密码
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("过于常见"));
    }

    [Fact]
    public async Task ValidateAsync_WithWeakPasswordDisabled_ShouldPassWeakCheck()
    {
        // Arrange - 关闭弱密码检查
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync("security.password_check_weak_password"))
            .ReturnsAsync("false");
        // 清除缓存确保重新加载策略
        _service.InvalidateCache();

        // Act
        var result = await _service.ValidateAsync("Password123!");

        // Assert - 关闭弱密码检查后应该通过
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidateAsync - Common Pattern Detection

    [Fact]
    public async Task ValidateAsync_WithConsecutiveDigits_ShouldFailWithError()
    {
        // Act - 1234 是连续数字
        var result = await _service.ValidateAsync("Abcd!1234xy");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("模式"));
    }

    [Fact]
    public async Task ValidateAsync_WithConsecutiveLetters_ShouldFailWithError()
    {
        // Act - abcd 是连续字母
        var result = await _service.ValidateAsync("1234!abcdXY");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("模式"));
    }

    [Fact]
    public async Task ValidateAsync_WithRepeatedCharacters_ShouldFailWithError()
    {
        // Act - aaaa 是重复字符
        var result = await _service.ValidateAsync("Baaaa!1234X");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("模式"));
    }

    [Fact]
    public async Task ValidateAsync_WithKeyboardPattern_ShouldFailWithError()
    {
        // Act - qwerty 是键盘行模式
        var result = await _service.ValidateAsync("1Qwerty!23X");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("模式"));
    }

    [Fact]
    public async Task ValidateAsync_WithCommonPatternsDisabled_ShouldPassPatternCheck()
    {
        // Arrange
        _mockSystemOptionRepo.Setup(r => r.GetValueAsync("security.password_check_common_patterns"))
            .ReturnsAsync("false");
        _service.InvalidateCache();

        // Act
        var result = await _service.ValidateAsync("Abcd!1234xy");

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region InvalidateCache

    [Fact]
    public void InvalidateCache_ShouldNotThrow()
    {
        // Act
        Action act = () => _service.InvalidateCache();

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}
