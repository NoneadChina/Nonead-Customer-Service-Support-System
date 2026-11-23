using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NoneadCSS.Api.Middleware;
using Xunit;

namespace NoneadCSS.Tests.Infrastructure;

/// <summary>
/// /docs（Scalar API 文档页）中英双语化的中间件测试：
/// 语言解析、OpenAPI 文档地址改写、标题改写、语言切换按钮注入、界面汉化脚本注入、品牌替换脚本保留、非 HTML 响应不受影响。
/// 中间件用 DefaultHttpContext + 一个写出真实 /docs HTML 形状（实测 2384 字节）片段的委托驱动。
/// </summary>
public class ScalarDocsLocalizationTests
{
    /// <summary>Scalar 原始页面（未注入品牌脚本之前的形状），取自 http://127.0.0.1:5008/docs 的实际响应。</summary>
    private const string DocsHtml = """
        <!doctype html>
        <html>
        <head>
            <title>NoneadCSS API 文档</title>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
        </head>
        <body>
            <script id="api-reference"></script>
            <script src="scalar.aspnetcore.js"></script>
            <script>
                const basePath = getBasePath('/docs/');
                const openApiUrl = `${window.location.origin}${basePath}/openapi/v1.json`;
                const reference = document.getElementById('api-reference');
                reference.dataset.url = openApiUrl;
                reference.dataset.configuration = JSON.stringify({"theme":"purple","layout":"modern","hideClientButton":false});
            </script>
            <script src="scalar.js"></script>
        </body>
        </html>
        """;

    private const string HtmlContentType = "text/html; charset=utf-8";

    // ===== 语言解析 =====

    [Fact]
    public async Task NoParamNoCookie_DefaultsToChinese_AndDoesNotWriteCookie()
    {
        var (html, context) = await RunAsync(query: string.Empty, cookie: null);

        html.Should().Contain("/openapi/v1.json");
        html.Should().NotContain("/openapi/en.json");
        html.Should().Contain("<title>NoneadCSS API 文档</title>");
        html.Should().Contain("id=\"ncss-zh-chrome-js\"");
        GetSetCookie(context).Should().BeEmpty("没有 ?lang= 时不得写 Cookie");
    }

    [Theory]
    [InlineData("?lang=en")]
    [InlineData("?lang=EN")]
    [InlineData("?lang=EN-US")]
    [InlineData("?lang=en-GB")]
    public async Task EnglishQuery_SelectsEnglishDocument_AndSwapsTitle(string query)
    {
        var (html, _) = await RunAsync(query, cookie: null);

        html.Should().Contain("/openapi/en.json");
        html.Should().NotContain("/openapi/v1.json");
        html.Should().Contain("<title>NoneadCSS API Documentation</title>");
        html.Should().NotContain("NoneadCSS API 文档");
    }

    [Theory]
    [InlineData("?lang=zh-Hans")]
    [InlineData("?lang=zh-CN")]
    [InlineData("?lang=zh")]
    [InlineData("?lang=ZH-HANS")]
    public async Task ChineseQuery_KeepsChineseDocument(string query)
    {
        var (html, _) = await RunAsync(query, cookie: null);

        html.Should().Contain("/openapi/v1.json");
        html.Should().NotContain("/openapi/en.json");
        html.Should().Contain("<title>NoneadCSS API 文档</title>");
    }

    [Fact]
    public async Task CookieFallback_WhenNoQueryParam_UsesCookieValue()
    {
        var (html, context) = await RunAsync(string.Empty, cookie: "ncss_docs_lang=en");

        html.Should().Contain("/openapi/en.json");
        html.Should().Contain("<title>NoneadCSS API Documentation</title>");
        GetSetCookie(context).Should().BeEmpty("Cookie 只是沿用上次选择，不需要再写一次");
    }

    [Fact]
    public async Task CookieValue_GovernsOnlyTheTwoNormalisedStrings()
    {
        // 伪造的 Cookie 内容只能被忽略，不能被当作语言值带进 HTML
        var (html, context) = await RunAsync(string.Empty, cookie: "ncss_docs_lang=<script>alert(1)</script>");

        html.Should().Contain("/openapi/v1.json");
        html.Should().NotContain("alert(1)");
        GetSetCookie(context).Should().BeEmpty();
    }

    [Theory]
    [InlineData("?lang=fr")]
    [InlineData("?lang=")]
    [InlineData("?lang=english")]
    public async Task UnknownQueryParam_FallsBackToChinese_AndWritesNoCookie(string query)
    {
        var (html, context) = await RunAsync(query, cookie: null);

        html.Should().Contain("/openapi/v1.json");
        html.Should().Contain("<title>NoneadCSS API 文档</title>");
        GetSetCookie(context).Should().BeEmpty("只有 ?lang= 命中两个约定值时才写 Cookie");
    }

    [Fact]
    public async Task ExplicitQueryParam_WritesCookieWithAgreedAttributes()
    {
        var (_, context) = await RunAsync("?lang=en", cookie: null);

        var cookie = GetSetCookie(context);
        cookie.Should().StartWithEquivalentOf("ncss_docs_lang=en");
        cookie.Should().ContainEquivalentOf("path=/docs");
        cookie.Should().ContainEquivalentOf("httponly");
        cookie.Should().ContainEquivalentOf("samesite=lax");
        cookie.Should().ContainEquivalentOf("max-age=2592000"); // 30 天
    }

    [Fact]
    public async Task ExplicitChineseQuery_WritesCookieWithValueZhHans()
    {
        var (html, context) = await RunAsync("?lang=zh-Hans", cookie: null);

        html.Should().Contain("/openapi/v1.json");
        GetSetCookie(context).Should().StartWithEquivalentOf("ncss_docs_lang=zh-Hans");
    }

    [Fact]
    public async Task EnglishQuery_OverridesChineseCookie()
    {
        var (html, _) = await RunAsync("?lang=en", cookie: "ncss_docs_lang=zh-Hans");

        html.Should().Contain("/openapi/en.json");
    }

    // ===== 注入内容：切换按钮 / 汉化脚本 / 品牌替换 =====

    [Theory]
    [InlineData("?lang=en")]
    [InlineData("?lang=zh-Hans")]
    [InlineData("")]
    public async Task LanguageSwitcherIsServerRendered_InBothLanguages(string query)
    {
        var (html, _) = await RunAsync(query, cookie: null);

        html.Should().Contain("id=\"ncss-lang-switch\"");
        html.Should().Contain("<a href=\"/docs?lang=en\"");
        html.Should().Contain("English</a>");
        html.Should().Contain("<a href=\"/docs?lang=zh-Hans\"");
        html.Should().Contain("简体中文</a>");
        html.Should().Contain("pointer-events:auto");
    }

    [Theory]
    [InlineData("?lang=en", "/docs?lang=en")]
    [InlineData("", "/docs?lang=zh-Hans")]
    [InlineData("?lang=zh-Hans", "/docs?lang=zh-Hans")]
    public async Task ActiveLanguageIsMarkedWithAriaCurrent(string query, string expectedActiveHref)
    {
        var (html, _) = await RunAsync(query, cookie: null);

        var anchorStart = html.IndexOf("<a href=\"" + expectedActiveHref, StringComparison.Ordinal);
        anchorStart.Should().BePositive($"未找到 {expectedActiveHref} 对应的链接");
        var anchorEnd = html.IndexOf("</a>", anchorStart, StringComparison.Ordinal);
        html[anchorStart..anchorEnd].Should().Contain("aria-current=\"true\"");
    }

    [Fact]
    public async Task ChineseMode_InjectsChromeDictionaryAndRequiredStyleOverride()
    {
        var (html, _) = await RunAsync(string.Empty, cookie: null);

        html.Should().Contain("id=\"ncss-zh-chrome-js\"");
        html.Should().Contain("MutationObserver");
        // Required 是 Scalar 用 CSS ::after 生成的，文本节点改不到，必须覆盖 content
        html.Should().Contain(".required::after{content:\"必填\"!important}");
    }

    [Fact]
    public async Task EnglishMode_DoesNotInjectChromeTranslation()
    {
        var (html, _) = await RunAsync("?lang=en", cookie: null);

        html.Should().NotContain("ncss-zh-chrome-js");
        html.Should().NotContain("ncss-zh-chrome-css");
    }

    [Fact]
    public async Task InjectedChromeScriptStaysUnderBudget()
    {
        var (html, _) = await RunAsync(string.Empty, cookie: null);

        var start = html.IndexOf("<script id=\"ncss-zh-chrome-js\"", StringComparison.Ordinal);
        start.Should().BePositive();
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        var injected = Encoding.UTF8.GetByteCount(html[start..(end + "</script>".Length)]);

        injected.Should().BeLessThan(12 * 1024, "注入脚本必须控制在 ~12 KB 以内");
    }

    [Theory]
    [InlineData("?lang=en")]
    [InlineData("?lang=zh-Hans")]
    [InlineData("")]
    public async Task BrandingReplacementIsStillInjected_InEveryLanguage(string query)
    {
        var (html, _) = await RunAsync(query, cookie: null);

        html.Should().Contain("replaceScalarBranding");
        html.Should().Contain("https://www.nonead.com");
        html.Should().Contain("Powered by Nonead");
        html.Should().Contain("MutationObserver");
    }

    [Theory]
    [InlineData("?lang=en")]
    [InlineData("?lang=zh-Hans")]
    [InlineData("")]
    public async Task InjectedMarkupGoesBeforeClosingHead(string query)
    {
        var (html, _) = await RunAsync(query, cookie: null);

        var headEnd = html.IndexOf("</head>", StringComparison.Ordinal);
        headEnd.Should().BePositive();
        html[..headEnd].Should().Contain("replaceScalarBranding");
        // 切换按钮落在 <body> 起始标签之后，不与 head 内容混在一起
        html.IndexOf("id=\"ncss-lang-switch\"", StringComparison.Ordinal)
            .Should().BeGreaterThan(html.IndexOf("<body", StringComparison.Ordinal));
    }

    // ===== 改写边界与响应处理 =====

    [Fact]
    public async Task NonHtmlResponse_PassesThroughUntouched()
    {
        const string json = "{\"openapi\":\"3.0.1\",\"info\":{\"title\":\"NoneadCSS API 文档\"}}";

        var (body, context) = await RunAsync("?lang=en", cookie: null, contentType: "application/json; charset=utf-8", writtenBody: json, downstreamSetsContentLength: true);

        body.Should().Be(json);
        body.Should().NotContain("ncss-lang-switch");
        body.Should().NotContain("replaceScalarBranding");
        body.Should().NotContain("/openapi/en.json");
        context.Response.ContentType.Should().StartWith("application/json");
        // 非 HTML 时中间件不得改动下游自己设定的 ContentLength
        context.Response.ContentLength.Should().Be(Encoding.UTF8.GetByteCount(json));
    }

    [Fact]
    public async Task EmptyHtmlBody_IsNotBrokenAndNothingIsInjected()
    {
        var (body, context) = await RunAsync("?lang=en", cookie: null, writtenBody: string.Empty);

        body.Should().BeEmpty();
        body.Should().NotContain("ncss-lang-switch");
    }

    [Fact]
    public async Task ResponseContentLengthMatchesRewrittenBytes()
    {
        var (body, context) = await RunAsync("?lang=en", cookie: null);

        var bytes = Encoding.UTF8.GetByteCount(body);
        context.Response.ContentLength.Should().Be(bytes);
        bytes.Should().BeGreaterThan(Encoding.UTF8.GetByteCount(DocsHtml), "注入后响应应变长");
    }

    [Fact]
    public async Task DocsSubPath_IsAlsoLocalised()
    {
        var (html, _) = await RunAsync("?lang=en", cookie: null, path: "/docs/index.html");

        html.Should().Contain("/openapi/en.json");
    }

    [Fact]
    public async Task OtherPaths_AreNotTouched()
    {
        var (body, context) = await RunAsync("?lang=en", cookie: null, path: "/api/openapi/v1.json");

        body.Should().Be(DocsHtml);
        body.Should().NotContain("ncss-lang-switch");
        GetSetCookie(context).Should().BeEmpty();
    }

    [Fact]
    public async Task ExceptionPath_RestoresBodyAndClearsContentLength()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/docs";
        context.Request.QueryString = new QueryString("?lang=en");

        var originalStream = new MemoryStream();
        context.Response.Body = originalStream;

        var middleware = new ScalarCustomizationMiddleware(_ => throw new InvalidOperationException("downstream failed"));

        var act = async () => await middleware.InvokeAsync(context);
        await act.Should().ThrowAsync<InvalidOperationException>();

        context.Response.Body.Should().BeSameAs(originalStream, "异常路径必须把响应流换回原始流");
        context.Response.ContentLength.Should().BeNull("异常路径不能留下改写后的 ContentLength");
        originalStream.ToArray().Should().BeEmpty();
    }

    // ===== 汉化对照表自身（NoneadCSS.Api 未开 InternalsVisibleTo，只能从注入结果里核对）=====

    [Fact]
    public async Task ChromeDictionary_CarriesConfirmedTranslations_AndIsScriptSafe()
    {
        var (html, _) = await RunAsync(string.Empty, cookie: null);
        var literal = ExtractDictionaryLiteral(html);

        // 这些键都是逐条从 Scalar 2.0.7 打包产物里核对出来的英文字面量
        literal.Should().Contain("\"Send Request\":\"发送请求\"");
        literal.Should().Contain("\"Query Parameters\":\"查询参数\"");
        literal.Should().Contain("\"Copy endpoint URL\":\"复制接口地址\"");
        literal.Should().Contain("\"Search\":\"搜索\"");

        PairRegex.Matches(literal).Count.Should().BeGreaterThan(60, "对照表应覆盖 Scalar 界面主要标签");
        literal.Should().NotContain("</script", "注入字典不得提前闭合 <script>");
        literal.Should().NotContain("\\u", "中文译文应原样输出，便于人工核对");
    }

    [Fact]
    public async Task ChromeDictionary_EveryEntryIsATranslatedPlainEnglishLabel()
    {
        var (html, _) = await RunAsync(string.Empty, cookie: null);
        var literal = ExtractDictionaryLiteral(html);

        var matches = PairRegex.Matches(literal);
        matches.Count.Should().BeGreaterThan(60);
        foreach (Match m in matches)
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value;
            key.Should().NotBeEmpty();
            value.Should().NotBeEmpty();
            value.Should().NotBe(key, $"条目 {key} 没有真正翻译");
            key.Should().NotContain("<", $"键 {key} 含可能破坏 HTML/脚本的字符");
            key.Should().Be(key.Trim(), "键不能带首尾空格（运行时按 trim 后精确匹配）");
        }
    }

    private static readonly System.Text.RegularExpressions.Regex PairRegex =
        new("\"([^\"]*)\":\"([^\"]*)\"", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>取注入脚本里 <c>var D={...}</c> 的字典字面量部分。</summary>
    private static string ExtractDictionaryLiteral(string html)
    {
        var marker = html.IndexOf("id=\"ncss-zh-chrome-js\"", StringComparison.Ordinal);
        marker.Should().BePositive("未找到汉化脚本");
        var varStart = html.IndexOf("var D=", marker, StringComparison.Ordinal);
        varStart.Should().BePositive("汉化脚本里找不到字典变量");
        var brace = html.IndexOf('{', varStart);
        brace.Should().BePositive();
        var end = html.IndexOf("\"};", brace, StringComparison.Ordinal);
        end.Should().BePositive("字典字面量未正常结束");
        return html[brace..(end + 1)];
    }

    // ===== 驱动工具 =====

    private static async Task<(string Body, HttpContext Context)> RunAsync(
        string query,
        string? cookie,
        string contentType = HtmlContentType,
        string path = "/docs",
        string? writtenBody = null,
        bool downstreamSetsContentLength = false)
    {
        var body = writtenBody ?? DocsHtml;

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        if (cookie is not null)
        {
            context.Request.Headers["Cookie"] = cookie;
        }

        var originalStream = new MemoryStream();
        context.Response.Body = originalStream;

        var middleware = new ScalarCustomizationMiddleware(ctx =>
        {
            ctx.Response.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(body);
            if (downstreamSetsContentLength)
            {
                ctx.Response.ContentLength = bytes.Length;
            }

            return ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length);
        });

        await middleware.InvokeAsync(context);

        return (Encoding.UTF8.GetString(originalStream.ToArray()), context);
    }

    private static string GetSetCookie(HttpContext context)
        => context.Response.Headers["Set-Cookie"].ToString();
}
