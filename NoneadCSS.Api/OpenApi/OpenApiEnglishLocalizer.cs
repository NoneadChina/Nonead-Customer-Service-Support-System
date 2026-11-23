using System.Text.Json;
using Microsoft.OpenApi;

namespace NoneadCSS.Api.OpenApi;

/// <summary>
/// OpenAPI 文档英文本地化器。
/// 默认文档 <c>v1</c> 的文案来自 C# XML 文档注释（简体中文）；本类按词典把同一份文档结构里的
/// 中文文案整体替换成英文，供第二份文档 <c>en</c>（<c>/openapi/en.json</c>）使用。
/// <c>/docs?lang=en</c> 由 <c>ScalarCustomizationMiddleware</c> 把页面里的文档 URL 切到 <c>/openapi/en.json</c>。
/// 词典文件：<c>NoneadCSS.Api/OpenApi/openapi-en-dictionary.json</c>（简体中文 → 英文，构建时复制到输出目录）。
/// </summary>
public static class OpenApiEnglishLocalizer
{
    /// <summary>默认（简体中文）文档名。</summary>
    public const string ChineseDocumentName = "v1";

    /// <summary>英文文档名，对应 <c>/openapi/en.json</c>。</summary>
    public const string EnglishDocumentName = "en";

    /// <summary>词典相对输出目录/内容根的路径。</summary>
    public const string DictionaryRelativePath = "OpenApi/openapi-en-dictionary.json";

    private static readonly Lazy<LocalizationDictionary> DictionaryLazy = new(LoadDictionary);

    /// <summary>已加载的词典（首次访问时惰性加载并缓存）。</summary>
    public static LocalizationDictionary Dictionary => DictionaryLazy.Value;

    /// <summary>
    /// 用内置词典把文档中的中文文案替换为英文。
    /// 词典缺失时不抛异常（返回 0），文档仍可用，只是保持中文。
    /// </summary>
    /// <param name="document">待就地修改的 OpenAPI 文档。</param>
    /// <returns>实际替换的字符串条数。</returns>
    public static int ApplyEnglish(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Apply(document, Dictionary.Map);
    }

    /// <summary>用指定词典就地替换文档文案（单元测试可注入小词典）。</summary>
    /// <param name="document">待修改的文档。</param>
    /// <param name="map">简体中文 → 英文 词典。</param>
    /// <returns>实际替换的字符串条数。</returns>
    public static int Apply(OpenApiDocument document, IReadOnlyDictionary<string, string> map)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(map);

        var count = 0;

        if (document.Info is not null)
        {
            document.Info.Title = Map(document.Info.Title, map, ref count);
            document.Info.Summary = Map(document.Info.Summary, map, ref count);
            document.Info.Description = Map(document.Info.Description, map, ref count);
        }

        // Tag 的 Name 参与 operation.Tags 的引用匹配，只译 Description，避免破坏引用。
        if (document.Tags is not null)
        {
            foreach (var tag in document.Tags)
            {
                tag.Description = Map(tag.Description, map, ref count);
            }
        }

        WalkPathItems(document.Paths, map, ref count);
        WalkPathItems(document.Webhooks, map, ref count);

        var components = document.Components;
        if (components is not null)
        {
            var visited = new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance);

            if (components.Schemas is not null)
            {
                foreach (var schema in components.Schemas.Values)
                {
                    WalkSchema(schema, map, ref count, visited);
                }
            }

            if (components.Parameters is not null)
            {
                foreach (var parameter in components.Parameters.Values)
                {
                    WalkParameter(parameter, map, ref count);
                }
            }

            if (components.RequestBodies is not null)
            {
                foreach (var body in components.RequestBodies.Values)
                {
                    WalkRequestBody(body, map, ref count);
                }
            }

            if (components.Responses is not null)
            {
                foreach (var response in components.Responses.Values)
                {
                    WalkResponse(response, map, ref count);
                }
            }

            WalkPathItems(components.PathItems, map, ref count);
        }

        return count;
    }

    private static void WalkPathItems(
        IDictionary<string, IOpenApiPathItem>? items,
        IReadOnlyDictionary<string, string> map,
        ref int count)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items.Values)
        {
            WalkPathItem(item, map, ref count);
        }
    }

    private static void WalkPathItem(
        IOpenApiPathItem? item,
        IReadOnlyDictionary<string, string> map,
        ref int count)
    {
        if (item is not OpenApiPathItem pathItem)
        {
            return;
        }

        pathItem.Summary = Map(pathItem.Summary, map, ref count);
        pathItem.Description = Map(pathItem.Description, map, ref count);

        if (pathItem.Parameters is not null)
        {
            foreach (var parameter in pathItem.Parameters)
            {
                WalkParameter(parameter, map, ref count);
            }
        }

        if (pathItem.Operations is null)
        {
            return;
        }

        foreach (var operation in pathItem.Operations.Values)
        {
            operation.Summary = Map(operation.Summary, map, ref count);
            operation.Description = Map(operation.Description, map, ref count);

            if (operation.Parameters is not null)
            {
                foreach (var parameter in operation.Parameters)
                {
                    WalkParameter(parameter, map, ref count);
                }
            }

            WalkRequestBody(operation.RequestBody, map, ref count);

            if (operation.Responses is not null)
            {
                foreach (var response in operation.Responses.Values)
                {
                    WalkResponse(response, map, ref count);
                }
            }
        }
    }

    private static void WalkParameter(
        IOpenApiParameter? parameter,
        IReadOnlyDictionary<string, string> map,
        ref int count)
    {
        if (parameter is OpenApiParameter openApiParameter)
        {
            openApiParameter.Description = Map(openApiParameter.Description, map, ref count);
        }
    }

    private static void WalkRequestBody(
        IOpenApiRequestBody? body,
        IReadOnlyDictionary<string, string> map,
        ref int count)
    {
        if (body is OpenApiRequestBody requestBody)
        {
            requestBody.Description = Map(requestBody.Description, map, ref count);
        }
    }

    private static void WalkResponse(
        IOpenApiResponse? response,
        IReadOnlyDictionary<string, string> map,
        ref int count)
    {
        if (response is OpenApiResponse openApiResponse)
        {
            openApiResponse.Description = Map(openApiResponse.Description, map, ref count);
        }
    }

    private static void WalkSchema(
        IOpenApiSchema? schema,
        IReadOnlyDictionary<string, string> map,
        ref int count,
        HashSet<IOpenApiSchema> visited)
    {
        if (schema is null || !visited.Add(schema) || schema is not OpenApiSchema openApiSchema)
        {
            return;
        }

        openApiSchema.Title = Map(openApiSchema.Title, map, ref count);
        openApiSchema.Description = Map(openApiSchema.Description, map, ref count);

        WalkSchema(openApiSchema.Items, map, ref count, visited);
        WalkSchema(openApiSchema.AdditionalProperties, map, ref count, visited);
        WalkSchema(openApiSchema.Not, map, ref count, visited);

        if (openApiSchema.Properties is not null)
        {
            foreach (var property in openApiSchema.Properties.Values)
            {
                WalkSchema(property, map, ref count, visited);
            }
        }

        if (openApiSchema.PatternProperties is not null)
        {
            foreach (var property in openApiSchema.PatternProperties.Values)
            {
                WalkSchema(property, map, ref count, visited);
            }
        }

        WalkSchemaList(openApiSchema.AllOf, map, ref count, visited);
        WalkSchemaList(openApiSchema.OneOf, map, ref count, visited);
        WalkSchemaList(openApiSchema.AnyOf, map, ref count, visited);
    }

    private static void WalkSchemaList(
        IEnumerable<IOpenApiSchema>? schemas,
        IReadOnlyDictionary<string, string> map,
        ref int count,
        HashSet<IOpenApiSchema> visited)
    {
        if (schemas is null)
        {
            return;
        }

        foreach (var schema in schemas)
        {
            WalkSchema(schema, map, ref count, visited);
        }
    }

    private static string? Map(string? value, IReadOnlyDictionary<string, string> map, ref int count)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (map.TryGetValue(value, out var translation))
        {
            count++;
            return translation;
        }

        // XML 文档注释常在换行后留下缩进空格，去掉首尾空白再试一次。
        var trimmed = value.Trim();
        if (!ReferenceEquals(trimmed, value) && map.TryGetValue(trimmed, out translation))
        {
            count++;
            return translation;
        }

        return value;
    }

    private static LocalizationDictionary LoadDictionary()
    {
        foreach (var path in EnumerateDictionaryCandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(path);
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (raw is not null && raw.Count > 0)
                {
                    return new LocalizationDictionary(new Dictionary<string, string>(raw, StringComparer.Ordinal), path);
                }
            }
            catch (JsonException)
            {
                // 词典损坏时不阻断文档生成，保持中文并让调用方按 Count==0 记录警告。
                return new LocalizationDictionary(StringComparer.Ordinal);
            }
        }

        return new LocalizationDictionary(StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateDictionaryCandidatePaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, DictionaryRelativePath);
        yield return Path.Combine(Directory.GetCurrentDirectory(), DictionaryRelativePath);

        // dotnet test / IDE 直接从测试项目输出目录运行时，向上找源码里的 NoneadCSS.Api 目录。
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory is not null; depth++, directory = directory.Parent)
        {
            yield return Path.Combine(directory.FullName, "NoneadCSS.Api", DictionaryRelativePath);
        }
    }
}

/// <summary>
/// 词典本身 + 命中位置（用于诊断：文件缺失时 Count 为 0，可据此记录警告）。
/// </summary>
public sealed class LocalizationDictionary : IReadOnlyDictionary<string, string>
{
    private readonly Dictionary<string, string> _map;

    public LocalizationDictionary(IEqualityComparer<string> keyComparer)
    {
        _map = new Dictionary<string, string>(keyComparer);
    }

    public LocalizationDictionary(Dictionary<string, string> map, string? sourcePath)
    {
        _map = map;
        SourcePath = sourcePath;
    }

    /// <summary>实际读取的词典文件路径；未找到文件时为 null。</summary>
    public string? SourcePath { get; }

    /// <summary>词典条数；0 表示词典缺失或损坏。</summary>
    public int Count => _map.Count;

    public IReadOnlyDictionary<string, string> Map => _map;

    public string this[string key] => _map[key];

    public IEnumerable<string> Keys => _map.Keys;

    public IEnumerable<string> Values => _map.Values;

    public bool ContainsKey(string key) => _map.ContainsKey(key);

    public bool TryGetValue(string key, out string value) => _map.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _map.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _map.GetEnumerator();
}
