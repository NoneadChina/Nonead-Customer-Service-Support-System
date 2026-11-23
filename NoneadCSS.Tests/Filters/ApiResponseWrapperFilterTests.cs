using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Xunit;

namespace NoneadCSS.Tests.Filters;

/// <summary>
/// 响应信封过滤器单元测试（不连 DB）。2026-09-26 批次 12 切片②：把信封级 message
/// （成功 `操作成功` / 状态码错误文案表）从硬编码中文改为按请求文化取词后补的回归。
///
/// 锁三条既有语义 + 一条新语义：
/// ① 2xx → 本地化的成功文案；
/// ② 非 2xx 且**值自带 message** → 仍以内层文案为准（内层优先，不能被状态码文案顶掉）；
/// ③ 非 2xx 且没有内层文案 → 本地化的状态码文案（404 → Resource not found / 资源未找到）；
/// ④ `StatusCodeResult`（如 `return NotFound();`）与 `EmptyResult` 两条分支同样走本地化。
/// </summary>
public class ApiResponseWrapperFilterTests
{
    /// <summary>用"语言前缀 + key"的假词条，证明过滤器确实经本地化服务取词（而不是硬编码）。</summary>
    private static Mock<ILocalizationService> Localizer(string prefix)
    {
        var map = new Dictionary<string, string>
        {
            ["ApiSuccess"] = $"{prefix}:success",
            ["ApiErrorNotFound"] = $"{prefix}:not-found",
            ["ApiErrorBadRequest"] = $"{prefix}:bad-request",
            ["ApiErrorGeneric"] = $"{prefix}:generic",
        };
        var mock = new Mock<ILocalizationService>();
        mock.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => map.TryGetValue(k, out var v) ? v : k);
        return mock;
    }

    private static async Task<ApiResponse> Run(IActionResult result, ILocalizationService localizer)
    {
        var filter = new ApiResponseWrapperFilter(localizer);
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, null!);
        ResultExecutionDelegate next = () => Task.FromResult<ResultExecutedContext>(null!);

        await filter.OnResultExecutionAsync(context, next);

        return (ApiResponse)((ObjectResult)context.Result).Value!;
    }

    [Fact]
    public async Task ObjectResult_2xx_UsesLocalizedSuccessMessage()
    {
        var wrapped = await Run(new ObjectResult(new { ok = true }) { StatusCode = 200 }, Localizer("en").Object);

        wrapped.Success.Should().BeTrue();
        wrapped.Code.Should().Be(200);
        wrapped.Message.Should().Be("en:success");
    }

    [Fact]
    public async Task ObjectResult_NonSuccess_WithoutInnerMessage_UsesLocalizedStatusMessage()
    {
        var wrapped = await Run(new ObjectResult(new { detail = "x" }) { StatusCode = 404 }, Localizer("en").Object);

        wrapped.Success.Should().BeFalse();
        wrapped.Message.Should().Be("en:not-found");
    }

    [Fact]
    public async Task ObjectResult_NonSuccess_WithInnerMessage_KeepsInnerMessage()
    {
        // 内层业务文案必须优先：控制器已给出具体原因时不能被"资源未找到"这类通用状态码文案顶掉
        var wrapped = await Run(new ObjectResult(new { message = "内层业务文案" }) { StatusCode = 400 }, Localizer("en").Object);

        wrapped.Message.Should().Be("内层业务文案");
    }

    [Fact]
    public async Task StatusCodeResult_UsesLocalizedStatusMessage()
    {
        // `return NotFound();` 走的就是这条分支（AttachmentsController:217 等）
        var wrapped = await Run(new NotFoundResult(), Localizer("en").Object);

        wrapped.Success.Should().BeFalse();
        wrapped.Code.Should().Be(404);
        wrapped.Message.Should().Be("en:not-found");
    }

    [Fact]
    public async Task EmptyResult_ReturnsLocalizedSuccessEnvelope()
    {
        var wrapped = await Run(new EmptyResult(), Localizer("zh-Hans").Object);

        wrapped.Code.Should().Be(200);
        wrapped.Message.Should().Be("zh-Hans:success");
    }

    [Fact]
    public async Task DifferentCulture_YieldsDifferentMessage_SameCode()
    {
        var en = await Run(new NotFoundResult(), Localizer("en").Object);
        var zh = await Run(new NotFoundResult(), Localizer("zh-Hans").Object);

        en.Code.Should().Be(zh.Code);
        en.Message.Should().Be("en:not-found");
        zh.Message.Should().Be("zh-Hans:not-found");
    }
}
