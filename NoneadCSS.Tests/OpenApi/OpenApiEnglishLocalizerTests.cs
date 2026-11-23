using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.OpenApi;
using NoneadCSS.Api.OpenApi;
using Xunit;
using HttpMethod = System.Net.Http.HttpMethod;

namespace NoneadCSS.Tests.OpenApi;

/// <summary>
/// OpenAPI 文档国际化（/docs 英文与简体中文）的回归测试：
/// 词典装载、字段遍历覆盖、以及 info 段中英文文案的正确性。
/// </summary>
public class OpenApiEnglishLocalizerTests
{
    private static readonly Regex CjkPattern = new("[\u3400-\u9fff\u3000-\u303f\uff00-\uffef]", RegexOptions.Compiled);

    private static OpenApiDocument BuildDocument(
        string summary,
        string description,
        string parameterDescription,
        string requestBodyDescription,
        string responseDescription)
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = "NoneadCSS API - 拓德客户服务与支持系统",
                Version = "1.0.0",
            },
            Paths = new OpenApiPaths(),
        };

        var pathItem = new OpenApiPathItem
        {
            Summary = summary,
            Description = description,
            // Microsoft.OpenApi 2.0.0 的无参构造不会初始化 Operations，必须显式给出
            Operations = new Dictionary<HttpMethod, OpenApiOperation>(),
            Parameters = new List<IOpenApiParameter>
            {
                new OpenApiParameter
                {
                    Name = "userId",
                    In = ParameterLocation.Query,
                    Description = parameterDescription,
                },
            },
        };

        pathItem.Operations[HttpMethod.Get] = new OpenApiOperation
        {
            Summary = summary,
            Description = description,
            RequestBody = new OpenApiRequestBody { Description = requestBodyDescription },
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse { Description = responseDescription },
            },
        };

        document.Paths["/api/users"] = pathItem;
        return document;
    }

    [Fact]
    public void Apply_ReplacesSummaryDescriptionParameterRequestBodyAndResponse()
    {
        var document = BuildDocument(
            "获取 API 密钥概览统计",
            "支持将附件关联到指定的消息线程（threadId 可选）。",
            "用户 ID 筛选",
            "创建请求",
            "概览统计数据");

        var map = new Dictionary<string, string>
        {
            ["获取 API 密钥概览统计"] = "Get API key overview statistics",
            ["支持将附件关联到指定的消息线程（threadId 可选）。"] = "Supports attaching media to a given message thread (threadId optional).",
            ["用户 ID 筛选"] = "Filter by user ID",
            ["创建请求"] = "Create request",
            ["概览统计数据"] = "Overview statistics",
        };

        var count = OpenApiEnglishLocalizer.Apply(document, map);

        // 夹具把同一段中文同时写在 path item 与 operation 上，两层都要翻译，故为 7
        count.Should().Be(7, "path item summary/description + operation summary/description + parameter + request body + response");
        document.Paths["/api/users"].Summary.Should().Be("Get API key overview statistics");
        var operation = document.Paths["/api/users"].Operations[HttpMethod.Get];
        operation.Summary.Should().Be("Get API key overview statistics");
        operation.Description.Should().Be("Supports attaching media to a given message thread (threadId optional).");
        document.Paths["/api/users"].Parameters.Should().ContainSingle()
            .Which.Description.Should().Be("Filter by user ID");
        operation.RequestBody!.Description.Should().Be("Create request");
        operation.Responses["200"].Description.Should().Be("Overview statistics");
        document.Info.Title.Should().Be("NoneadCSS API - 拓德客户服务与支持系统", "Info 段由 OpenApiDocumentationTexts 负责，词典不命中时保持原样");
    }

    [Fact]
    public void Apply_LeavesUnmatchedStringsUntouched_AndCountsZero()
    {
        var document = BuildDocument("未知文案", "另一段未收录说明", "同样未收录", "未收录请求", "未收录响应");

        var count = OpenApiEnglishLocalizer.Apply(document, new Dictionary<string, string>
        {
            ["完全无关"] = "unrelated",
        });

        count.Should().Be(0);
        document.Paths["/api/users"].Operations[HttpMethod.Get].Summary.Should().Be("未知文案");
    }

    [Fact]
    public void Apply_TranslatesOperationLevelParametersAsWellAsPathLevel()
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1.0.0" },
            Paths = new OpenApiPaths(),
        };
        var pathItem = new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>(),
        };
        pathItem.Operations[HttpMethod.Get] = new OpenApiOperation
        {
            Parameters = new List<IOpenApiParameter>
            {
                new OpenApiParameter { Name = "from", In = ParameterLocation.Query, Description = "起始时间（UTC，ISO 8601）" },
            },
        };
        pathItem.Parameters = new List<IOpenApiParameter>
        {
            new OpenApiParameter { Name = "userId", In = ParameterLocation.Query, Description = "用户 ID 筛选" },
        };
        document.Paths["/api/audit-logs"] = pathItem;

        var map = new Dictionary<string, string>
        {
            ["起始时间（UTC，ISO 8601）"] = "Start time (UTC, ISO 8601)",
            ["用户 ID 筛选"] = "Filter by user ID",
        };

        OpenApiEnglishLocalizer.Apply(document, map).Should().Be(2);
        document.Paths["/api/audit-logs"].Operations[HttpMethod.Get].Parameters![0].Description
            .Should().Be("Start time (UTC, ISO 8601)");
        document.Paths["/api/audit-logs"].Parameters[0].Description.Should().Be("Filter by user ID");
    }

    [Fact]
    public void Apply_ToleratesSurroundingWhitespaceFromXmlDocComments()
    {
        var document = BuildDocument("  获取 API 密钥概览统计  ", "x", "x", "x", "x");
        var map = new Dictionary<string, string> { ["获取 API 密钥概览统计"] = "Get API key overview statistics" };

        // 同一条带空白的文案出现在 path item 与 operation 两层，两层都应命中
        OpenApiEnglishLocalizer.Apply(document, map).Should().Be(2);
        document.Paths["/api/users"].Operations[HttpMethod.Get].Summary
            .Should().Be("Get API key overview statistics");
    }

    [Fact]
    public void Apply_TranslatesSchemaTitlesAndDescriptionsInComponents()
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["PagedResult"] = new OpenApiSchema
                    {
                        Title = "分页结果",
                        Description = "分页元信息",
                        Items = new OpenApiSchema { Description = "列表项" },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["total"] = new OpenApiSchema { Description = "总记录数" },
                        },
                        AllOf = new List<IOpenApiSchema>
                        {
                            new OpenApiSchema { Description = "组合基类" },
                        },
                    },
                },
            },
        };

        var map = new Dictionary<string, string>
        {
            ["分页结果"] = "Paged result",
            ["分页元信息"] = "Pagination metadata",
            ["列表项"] = "List item",
            ["总记录数"] = "Total record count",
            ["组合基类"] = "Composed base",
        };

        OpenApiEnglishLocalizer.Apply(document, map).Should().Be(5);
        var schema = (OpenApiSchema)document.Components.Schemas["PagedResult"];
        schema.Title.Should().Be("Paged result");
        schema.Description.Should().Be("Pagination metadata");
        ((OpenApiSchema)schema.Items!).Description.Should().Be("List item");
        ((OpenApiSchema)schema.Properties["total"]).Description.Should().Be("Total record count");
        ((OpenApiSchema)schema.AllOf[0]).Description.Should().Be("Composed base");
    }

    [Fact]
    public void Dictionary_IsShippedNextToTheApiProject_AndHasAtLeast900Entries()
    {
        var dictionary = OpenApiEnglishLocalizer.Dictionary;

        dictionary.Count.Should().BeGreaterThanOrEqualTo(900,
            "词典是 906 条中->英映射，随构建拷贝（NoneadCSS.Api.csproj 的 Content Update）；条数骤降说明词典没被找到或被截断");
        dictionary.SourcePath.Should().NotBeNullOrWhiteSpace("找不到词典文件时 Count 为 0，这里必须真的读到文件");
        File.Exists(dictionary.SourcePath).Should().BeTrue();
    }

    [Fact]
    public void Dictionary_ContainsNoCjkInAnyValue()
    {
        var offenders = OpenApiEnglishLocalizer.Dictionary.Map
            .Where(pair => CjkPattern.IsMatch(pair.Value))
            .Select(pair => pair.Key)
            .Take(10)
            .ToList();

        offenders.Should().BeEmpty("译文值里残留中文/全角字符会让英文文档出现中英混排");
    }

    [Fact]
    public void ApplyEnglish_UsesTheShippedDictionaryOnADocWithKnownChineseStrings()
    {
        var dictionary = OpenApiEnglishLocalizer.Dictionary;
        var knownKey = dictionary.Map.Keys.First(key => key.Length is > 4 and < 40 && !key.Contains('\n'));
        var document = BuildDocument(knownKey, "未被词典收录的说明文字", "未被词典收录的参数", "未被词典收录的请求", "未被词典收录的响应");

        var count = OpenApiEnglishLocalizer.ApplyEnglish(document);

        count.Should().BeGreaterThanOrEqualTo(1);
        document.Paths["/api/users"].Operations[HttpMethod.Get].Summary
            .Should().NotBe(knownKey).And.NotMatch(".*[\u3400-\u9fff].*");
    }

    [Fact]
    public void ApplyChineseInfo_SetsChineseHeadAndNeutralizesInternalContactUrl()
    {
        var document = new OpenApiDocument();

        OpenApiDocumentationTexts.ApplyChineseInfo(document);

        document.Info.Title.Should().Contain("拓德");
        document.Info.Description.Should().Contain("## 快速开始").And.Contain("{ success, code, data, message }");
        document.Info.Version.Should().Be(OpenApiDocumentationTexts.DocumentVersion);
        document.Info.Contact!.Name.Should().Be(OpenApiDocumentationTexts.ContactName);
        document.Info.Contact.Url.Should().BeNull("开源仓库不应保留内部占位站点地址");
        document.Info.License!.Name.Should().Be("AGPL-3.0");
    }

    [Fact]
    public void ApplyEnglishInfo_SetsEnglishHeadWithNoCjk()
    {
        var document = new OpenApiDocument();

        OpenApiDocumentationTexts.ApplyEnglishInfo(document);

        document.Info.Title.Should().Be("NoneadCSS API - Tuode Customer Service and Support System");
        document.Info.Description.Should().Contain("## Quick Start")
            .And.Contain("## Response Format")
            .And.Contain("## Internationalization");
        CjkPattern.IsMatch(document.Info.Title).Should().BeFalse();
        CjkPattern.IsMatch(document.Info.Description).Should().BeFalse();
    }

    [Fact]
    public void DocumentNames_MatchTheRoutesScalarIsConfiguredWith()
    {
        OpenApiEnglishLocalizer.ChineseDocumentName.Should().Be("v1");
        OpenApiEnglishLocalizer.EnglishDocumentName.Should().Be("en");
        OpenApiEnglishLocalizer.DictionaryRelativePath.Should().Be("OpenApi/openapi-en-dictionary.json");
    }
}
