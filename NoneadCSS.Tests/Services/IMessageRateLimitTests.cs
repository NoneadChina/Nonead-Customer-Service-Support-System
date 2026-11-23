using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Domain.Services;
using NoneadCSS.Infrastructure.Services.IMessage;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// iMessage 出站限流（审计 #12）—— 覆盖三层钳制里能被单测证明的两层：
/// 数据库读到 0/负数时 Provider 必须回落到代码默认值（否则 <c>TryAcquire</c> 会把全部出站静默拒掉），
/// 以及设置请求 DTO 的 <c>[Range(1, 600)]</c> 会把 0/负数/超限值挡在写入之前。
/// 第三层（前端输入框 min=1）是静态属性，见交付说明。
/// </summary>
public class IMessageRateLimitTests
{
    private static IMessageSettingsProvider CreateProvider(string? rateLimitInDatabase)
    {
        var systemOptions = new Mock<ISystemOptionRepository>();
        systemOptions.Setup(r => r.GetValueAsync(It.IsAny<string>())).ReturnsAsync((string key) =>
            key == IMessageOptionKeys.RateLimitPerMinute ? rateLimitInDatabase : null);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SystemOptions).Returns(systemOptions.Object);

        // Provider 通过 IServiceScopeFactory 开作用域取 IUnitOfWork（Singleton 不能捕获 Scoped 依赖）
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(IUnitOfWork))).Returns(unitOfWork.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new IMessageSettingsProvider(
            Options.Create(new IMessageOptions
            {
                Mode = IMessageModes.AmbMsp,
                Enabled = true,
                ApiBaseUrl = "https://msp.example.internal",
                BusinessIdentifier = "biz-0001",
                WebhookSecret = "webhook-secret",
            }),
            Options.Create(new CacheOptions { ChannelSettingsCacheMinutes = 10 }),
            scopeFactory.Object,
            new Mock<IEncryptionService>().Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<IMessageSettingsProvider>.Instance);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task SettingsProvider_NonPositiveRateLimitInDatabase_FallsBackToDefaultAndStillAllowsSend(string storedValue)
    {
        var provider = CreateProvider(storedValue);

        var settings = await provider.GetCurrentAsync();

        settings.RateLimitPerMinute.Should().Be(IMessageOptions.DefaultRateLimitPerMinute);

        // 钳制的实际效果：拿生效值去申请令牌必须放行，而不是像修复前那样整批返回 false
        var limiter = new IMessageRateLimiter();
        limiter.TryAcquire($"imessage:amb_msp:{storedValue}", settings.RateLimitPerMinute)
            .Should().BeTrue($"数据库里的 {storedValue} 不得让全部出站静默停发");
    }

    [Fact]
    public async Task SettingsProvider_PositiveRateLimitInDatabase_IsHonoredWithoutClamping()
    {
        var provider = CreateProvider("7");

        var settings = await provider.GetCurrentAsync();

        settings.RateLimitPerMinute.Should().Be(7);
    }

    [Fact]
    public void RateLimiter_NonPositiveLimit_FailsClosedByDesign()
    {
        // 限流器本身对 <= 0 一律拒绝（fail-closed）：这条断言锁住"为什么上层必须钳制"
        var limiter = new IMessageRateLimiter();
        limiter.TryAcquire("imessage:bridge:zero", 0).Should().BeFalse();
        limiter.TryAcquire("imessage:bridge:negative", -5).Should().BeFalse();
    }

    [Fact]
    public void RateLimiter_DefaultLimit_AllowsUpToThatCountWithinTheMinute()
    {
        var limiter = new IMessageRateLimiter();
        var scopeKey = "imessage:amb_msp:default-limit";

        var allowed = 0;
        for (var i = 0; i < IMessageOptions.DefaultRateLimitPerMinute + 1; i++)
        {
            if (limiter.TryAcquire(scopeKey, IMessageOptions.DefaultRateLimitPerMinute)) allowed++;
        }

        allowed.Should().Be(IMessageOptions.DefaultRateLimitPerMinute);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(601, false)]
    [InlineData(1, true)]
    [InlineData(600, true)]
    [InlineData(null, true)]
    public void UpdateSettingsRequest_RateLimitPerMinute_IsRangeValidated(int? value, bool expectValid)
    {
        var request = new UpdateIMessageSettingsRequest { RateLimitPerMinute = value };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(request, context, results, validateAllProperties: true);

        isValid.Should().Be(expectValid, string.Join("; ", results.Select(r => r.ErrorMessage)));
        if (!expectValid)
        {
            results.Should().Contain(r => r.MemberNames.Contains(nameof(UpdateIMessageSettingsRequest.RateLimitPerMinute)));
        }
    }
}
