using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using Xunit;

namespace NoneadCSS.Tests.Filters;

/// <summary>
/// M-02 出站响应消毒过滤器单元测试（不连 DB）。
/// 验证 HtmlSanitizingResponseFilter 对 ObjectResult 值做递归消毒：
/// 移除危险标签/事件属性、保守跳过纯文本、跳过敏感属性、递归处理嵌套/集合/字典。
/// </summary>
public class HtmlSanitizingResponseFilterTests
{
    private class NestedDto
    {
        public string? Title { get; set; }
    }

    private class TestDto
    {
        public string? Content { get; set; }
        public string? Plain { get; set; }
        public string? Token { get; set; }
        public NestedDto? Nested { get; set; }
        public List<TestDto>? Items { get; set; }
        public Dictionary<string, string>? Map { get; set; }
    }

    private static async Task<ObjectResult> RunFilter(object? value)
    {
        var filter = new HtmlSanitizingResponseFilter(NullLogger<HtmlSanitizingResponseFilter>.Instance);
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var result = new ObjectResult(value);
        var context = new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, null!);

        ResultExecutionDelegate next = () => Task.FromResult<ResultExecutedContext>(null!);

        await filter.OnResultExecutionAsync(context, next);
        return result;
    }

    [Fact]
    public async Task Filter_SanitizesHtmlInDtoStringProperty()
    {
        var dto = new TestDto
        {
            Content = "<script>alert(1)</script><b>Hello</b>",
            Plain = "just text a < b",
        };

        await RunFilter(dto);

        dto.Content.Should().NotContain("<script", "script 块应被移除");
        dto.Content.Should().Contain("<b>Hello</b>", "白名单标签应保留");
        dto.Plain.Should().Be("just text a < b", "无完整 HTML 标签的纯文本不应被改写");
    }

    [Fact]
    public async Task Filter_RemovesEventHandlerAttributes()
    {
        var dto = new TestDto
        {
            Content = "<a href=\"https://example.com\" onclick=\"alert(1)\">click</a>",
        };

        await RunFilter(dto);

        dto.Content.Should().NotContain("onclick", "on* 事件属性应被移除");
        dto.Content.Should().Contain("href=\"https://example.com\"", "白名单链接属性应保留");
    }

    [Fact]
    public async Task Filter_SkipsSensitiveProperty()
    {
        var dto = new TestDto
        {
            Token = "<script>alert(1)</script>",
        };

        await RunFilter(dto);

        dto.Token.Should().Be("<script>alert(1)</script>", "敏感属性（Token）不得被消毒改写");
    }

    [Fact]
    public async Task Filter_SanitizesNestedObject()
    {
        var dto = new TestDto
        {
            Nested = new NestedDto { Title = "<img src=x onerror=alert(1)>" },
        };

        await RunFilter(dto);

        dto.Nested.Should().NotBeNull();
        dto.Nested!.Title.Should().NotContain("onerror", "嵌套对象字符串也应被消毒");
        dto.Nested.Title.Should().NotContain("onerror=alert");
    }

    [Fact]
    public async Task Filter_SanitizesCollectionItems()
    {
        var dto = new TestDto
        {
            Items = new List<TestDto>
            {
                new() { Content = "<script>alert(1)</script>item1" },
                new() { Plain = "no tags here" },
            },
        };

        await RunFilter(dto);

        dto.Items.Should().NotBeNull();
        dto.Items![0].Content.Should().NotContain("<script");
        dto.Items[1].Plain.Should().Be("no tags here");
    }

    [Fact]
    public async Task Filter_SanitizesDictionaryValues()
    {
        var dto = new TestDto
        {
            Map = new Dictionary<string, string>
            {
                ["html"] = "<script>alert(1)</script>",
                ["plain"] = "text only",
            },
        };

        await RunFilter(dto);

        dto.Map.Should().NotBeNull();
        dto.Map!["html"].Should().NotContain("<script");
        dto.Map["plain"].Should().Be("text only");
    }

    [Fact]
    public async Task Filter_SanitizesAlreadyWrappedApiResponseData()
    {
        var dto = new TestDto
        {
            Content = "<script>alert(1)</script><i>ok</i>",
        };

        var wrapped = new ApiResponse<TestDto>(dto) { Message = "操作成功" };
        var result = await RunFilter(wrapped);

        result.Value.Should().BeSameAs(wrapped, "消毒应原位改写，保持同一引用");
        dto.Content.Should().NotContain("<script");
        dto.Content.Should().Contain("<i>ok</i>");
    }

    [Fact]
    public async Task Filter_PlainTextWithoutTags_StaysUnchanged()
    {
        var dto = new TestDto
        {
            Content = "a & b < c > d",
        };

        var result = await RunFilter(dto);

        // 孤立 < / > 不构成完整 HTML 标签，应原样保留
        dto.Content.Should().Be("a & b < c > d");
        result.Value.Should().BeSameAs(dto);
    }
}
