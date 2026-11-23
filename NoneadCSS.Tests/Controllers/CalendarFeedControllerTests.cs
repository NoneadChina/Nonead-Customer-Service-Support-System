using System;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NoneadCSS.Application.Security;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using Xunit;

namespace NoneadCSS.Tests.Controllers;

/// <summary>
/// M-26：CalendarFeedController 订阅令牌单元测试
/// 覆盖：令牌结构解析、过期失效、用户版本吊销、无效哈希拦截
/// </summary>
public class CalendarFeedControllerTests
{
    private readonly Mock<ICalendarExportService> _mockExport;
    private readonly Mock<IUnitOfWork> _mockUoW;
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly NoneadCSS.Api.Controllers.CalendarFeedController _controller;

    // 确保 SecretHasher 初始化（一次跨测试即可，静态类只需初始化一次）
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2211", Justification = "测试用共享 flag")]
    private static bool _hasherInitialized;

    public CalendarFeedControllerTests()
    {
        if (!_hasherInitialized)
        {
            SecretHasher.Initialize("test-secret-key-which-must-be-at-least-32-bytes-long-xyz");
            _hasherInitialized = true;
        }

        _mockExport = new Mock<ICalendarExportService>();
        _mockUoW = new Mock<IUnitOfWork>();
        _mockUserRepo = new Mock<IUserRepository>();
        _mockUoW.Setup(u => u.Users).Returns(_mockUserRepo.Object);
        _controller = new NoneadCSS.Api.Controllers.CalendarFeedController(_mockExport.Object, _mockUoW.Object);
        // GetFeed 内会写入 Response.Headers，需提供 HttpContext 避免 NRE
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
    }

    private static User CreateActiveUser(int id = 1, DateTime? updatedAtOverride = null)
    {
        var now = updatedAtOverride ?? new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        return new User
        {
            Id = id,
            FirstName = "测试",
            LastName = "用户",
            Email = "test@example.com",
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    #region GenerateToken

    [Fact]
    public void GenerateToken_ValidInput_HasFourPartsSeparatedByDot()
    {
        // Arrange
        var now = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, now);

        // Assert
        token.Should().NotBeNullOrEmpty();
        var parts = token.Split('.');
        parts.Should().HaveCount(4);
        parts[0].Should().Be("1");
        // version = UpdatedAt.Ticks
        parts[1].Should().Be(now.Ticks.ToString());
        // expiresAt = today + 365 days, yyyyMMddHHmmss 格式，14 位
        parts[2].Should().HaveLength(14);
    }

    [Fact]
    public void GenerateToken_CustomValidityDays_ExpiresCorrectly()
    {
        // Arrange: 固定时间保证可预测
        var createdAt = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        // 模拟 UtcNow：无法直接改 DateTime.UtcNow，验证解析后过期时间合理即可
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(
            userId: 5,
            userUpdatedAt: createdAt,
            validityDays: 7);

        // Act: 解析返回值
        var parts = token.Split('.');
        var expiresPart = parts[2];
        var expiresAt = DateTime.ParseExact(
            expiresPart,
            "yyyyMMddHHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);

        // Assert: 7 天有效期（相对于 UtcNow）
        expiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(6).AddHours(23));
        expiresAt.Should().BeBefore(DateTime.UtcNow.AddDays(8));
    }

    #endregion

    #region ParseToken (通过反射 + GetFeed 间接验证)

    private static (int UserId, long Version, DateTime ExpiresAt)? InvokeParseToken(string token)
    {
        var method = typeof(NoneadCSS.Api.Controllers.CalendarFeedController).GetMethod(
            "ParseToken",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (ValueTuple<int, long, DateTime>?)method!.Invoke(null, new object[] { token });
    }

    [Fact]
    public void ParseToken_NullOrEmpty_ReturnsNull()
    {
        InvokeParseToken(null!).Should().BeNull();
        InvokeParseToken("").Should().BeNull();
        InvokeParseToken("   ").Should().BeNull();
    }

    [Fact]
    public void ParseToken_WrongPartCount_ReturnsNull()
    {
        InvokeParseToken("a.b.c").Should().BeNull();
        InvokeParseToken("a.b").Should().BeNull();
    }

    [Fact]
    public void ParseToken_InvalidUserId_ReturnsNull()
    {
        // 正常令牌但首段非数字
        var baseToken = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, DateTime.UtcNow);
        var parts = baseToken.Split('.');
        var bad = $"not-a-number.{parts[1]}.{parts[2]}.{parts[3]}";
        InvokeParseToken(bad).Should().BeNull();
    }

    [Fact]
    public void ParseToken_InvalidVersion_ReturnsNull()
    {
        var baseToken = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, DateTime.UtcNow);
        var parts = baseToken.Split('.');
        var bad = $"{parts[0]}.not-long.{parts[2]}.{parts[3]}";
        InvokeParseToken(bad).Should().BeNull();
    }

    [Fact]
    public void ParseToken_InvalidExpiresAtFormat_ReturnsNull()
    {
        var baseToken = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, DateTime.UtcNow);
        var parts = baseToken.Split('.');
        var bad = $"{parts[0]}.{parts[1]}.bad-date.{parts[3]}";
        InvokeParseToken(bad).Should().BeNull();
    }

    [Fact]
    public void ParseToken_TamperedHash_ReturnsNull()
    {
        // 篡改最后一段（哈希）
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, DateTime.UtcNow);
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{parts[2]}.{Convert.ToBase64String(Encoding.UTF8.GetBytes("fake"))}";
        InvokeParseToken(tampered).Should().BeNull();
    }

    [Fact]
    public void ParseToken_ValidToken_ReturnsCorrectValues()
    {
        // Arrange
        var updatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(
            userId: 42, userUpdatedAt: updatedAt, validityDays: 30);

        // Act
        var result = InvokeParseToken(token);

        // Assert
        result.Should().NotBeNull();
        var (userId, version, expiresAt) = result!.Value;
        userId.Should().Be(42);
        version.Should().Be(updatedAt.Ticks);
        expiresAt.Kind.Should().Be(DateTimeKind.Utc);
        expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), precision: TimeSpan.FromMinutes(1));
    }

    #endregion

    #region GetFeed 端到端行为

    [Fact]
    public async Task GetFeed_InvalidToken_ReturnsUnauthorized()
    {
        // Act
        var result = await _controller.GetFeed(token: "completely-invalid-token", months: 3);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
        ((UnauthorizedObjectResult)result).Value.Should().Be("无效的订阅令牌");
    }

    [Fact]
    public async Task GetFeed_ExpiredToken_ReturnsUnauthorized()
    {
        // Arrange: 用过去的 UpdatedAt + 0 天有效期构造过期令牌（注意实际 GenerateToken 是 AddDays）
        // 这里用反射调用私有的 ParseToken 方式过于繁琐，我们直接构造一个已过期的 token：
        // 生成一个 validityDays=0 的令牌，再立即让时钟经过
        // 由于 validityDays<=0 时 AddDays(0) 仍在"今天"，我们使用特殊方式：构造 token 让 expiresAt 在过去
        var updatedAt = DateTime.UtcNow.AddYears(-2);
        // 生成 token 然后替换 expiresAt 为过去时间 + 重算 hash
        // 简化：直接验证 GetFeed 对于"已过期 10 年前的 token"会返回过期（用内部构造方式）
        // 简单办法：生成一个 validityDays=-10000 的 token（注意内部 AddDays 可以接受负数）
        var expiredToken = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(
            userId: 1,
            userUpdatedAt: DateTime.UtcNow,
            validityDays: -365);

        // Act
        var result = await _controller.GetFeed(expiredToken, months: 3);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
        ((UnauthorizedObjectResult)result).Value.Should().Be("订阅令牌已过期，请重新获取");
    }

    [Fact]
    public async Task GetFeed_NonExistingUser_ReturnsUnauthorized()
    {
        // Arrange
        var user = CreateActiveUser(1);
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(99, user.UpdatedAt);
        _mockUserRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((User?)null);

        // Act
        var result = await _controller.GetFeed(token, months: 3);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
        ((UnauthorizedObjectResult)result).Value.Should().Be("用户不存在或已禁用");
    }

    [Fact]
    public async Task GetFeed_UserVersionChanged_ReturnsRevoked()
    {
        // Arrange: 令牌生成时的 UpdatedAt 与 DB 中的不一致（例如用户改密码/角色）
        var user = CreateActiveUser(1, updatedAtOverride: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var tokenUpdatedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc); // 旧版本
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, tokenUpdatedAt);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        // Act
        var result = await _controller.GetFeed(token, months: 3);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
        ((UnauthorizedObjectResult)result).Value.Should().Be("订阅令牌已失效，请重新获取");
    }

    [Fact]
    public async Task GetFeed_DisabledUser_ReturnsUnauthorized()
    {
        // Arrange
        var user = CreateActiveUser(1);
        user.Status = UserStatus.Disabled;
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, user.UpdatedAt);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);

        // Act
        var result = await _controller.GetFeed(token, months: 3);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
        ((UnauthorizedObjectResult)result).Value.Should().Be("用户不存在或已禁用");
    }

    [Fact]
    public async Task GetFeed_ValidToken_ClampsMonthsBetween1And12()
    {
        // Arrange
        var user = CreateActiveUser(1);
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, user.UpdatedAt);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _mockExport.Setup(e => e.ExportRangeForFeedAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 1))
            .ReturnsAsync("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");

        // Act (months=0 -> 内部应该 clamp 到 1)
        await _controller.GetFeed(token, months: 0);

        // Assert: 确保 ExportRangeForFeedAsync 被调用（0<1 取 1，范围 >= 1 月）
        _mockExport.Verify(e => e.ExportRangeForFeedAsync(
            It.Is<DateTime>(s => s < DateTime.UtcNow),
            It.Is<DateTime>(e => e > DateTime.UtcNow),
            1), Times.Once);
    }

    [Fact]
    public async Task GetFeed_MonthsTooLarge_ClampedTo12()
    {
        // Arrange
        var user = CreateActiveUser(1);
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, user.UpdatedAt);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        _mockExport.Setup(e => e.ExportRangeForFeedAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 1))
            .ReturnsAsync("ICS");

        // Act
        await _controller.GetFeed(token, months: 9999);

        // Assert: endDate 应在不超过 13 个月后（加 1 个月 + 当月）
        _mockExport.Verify(e => e.ExportRangeForFeedAsync(
            It.IsAny<DateTime>(),
            It.Is<DateTime>(end => end <= DateTime.UtcNow.AddMonths(14)),
            1), Times.Once);
    }

    [Fact]
    public async Task GetFeed_Success_ReturnsTextCalendarFileResult()
    {
        // Arrange
        var user = CreateActiveUser(1);
        var token = NoneadCSS.Api.Controllers.CalendarFeedController.GenerateToken(1, user.UpdatedAt);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(user);
        var expectedIcs = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n";
        _mockExport.Setup(e => e.ExportRangeForFeedAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 1))
            .ReturnsAsync(expectedIcs);

        // Act
        var result = await _controller.GetFeed(token, months: 3);

        // Assert
        var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("text/calendar; charset=utf-8");
        // T000098 修复：不再传 fileName 参数，避免 Content-Disposition: attachment 导致 Outlook 视为下载文件
        fileResult.FileDownloadName.Should().BeNullOrEmpty();
        Encoding.UTF8.GetString(fileResult.FileContents).Should().Be(expectedIcs);
        // 同时验证已写入 Content-Disposition: inline 头
        _controller.Response.Headers["Content-Disposition"].ToString().Should().Be("inline");
    }

    #endregion
}
