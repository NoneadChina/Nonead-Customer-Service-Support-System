using System.Text.RegularExpressions;

namespace NoneadCSS.Tests.Integration;

/// <summary>
/// 日历模块 C# DTO 与 TypeScript 类型字段一致性集成测试
/// 纯文件解析测试，不启动 ASP.NET
/// </summary>
public class CalendarTypeSyncTests
{
    private static readonly string SolutionRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static readonly string CSharpDtoPath = Path.Combine(
        SolutionRoot, "NoneadCSS.Application", "DTOs", "Responses", "CalendarEventDto.cs");

    private static readonly string CSharpSubTaskDtoPath = Path.Combine(
        SolutionRoot, "NoneadCSS.Application", "DTOs", "Responses", "SubTaskDto.cs");

    private static readonly string CSharpRecurrenceDtoPath = Path.Combine(
        SolutionRoot, "NoneadCSS.Application", "DTOs", "Responses", "RecurrenceExclusionsDto.cs");

    private static readonly string TSEngagementPath = Path.Combine(
        SolutionRoot, "frontend", "src", "api", "types", "engagement.ts");

    private static string ToCamelCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
            return pascal;
        if (pascal.Length == 1)
            return pascal.ToLowerInvariant();
        return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
    }

    private static HashSet<string> ParseCSharpClassProperties(string filePath, string className)
    {
        var properties = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"C# 文件不存在: {filePath}");

        var lines = File.ReadAllLines(filePath);
        bool inClass = false;
        bool seenOpenBrace = false;
        int braceDepth = 0;
        var classPattern = new Regex($@"(class|record)\s+{Regex.Escape(className)}\b");
        var propPattern = new Regex(
            @"^\s*public\s+(?:(?:override|sealed|virtual|abstract|static|new|unsafe|readonly|extern|partial)\s+)*[\w<>\?,\[\]\.\s]+?\s+(\w+)\s*\{[^}]*\}",
            RegexOptions.Compiled);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("//") || line.StartsWith("///"))
                continue;

            if (!inClass)
            {
                if (classPattern.IsMatch(rawLine))
                {
                    inClass = true;
                    seenOpenBrace = false;
                    braceDepth = 0;
                }
                continue;
            }

            if (!seenOpenBrace)
            {
                foreach (var ch in rawLine)
                {
                    if (ch == '{')
                    {
                        braceDepth = 1;
                        seenOpenBrace = true;
                        break;
                    }
                }
                if (!seenOpenBrace)
                    continue;
                var match = propPattern.Match(rawLine);
                if (match.Success)
                    properties.Add(match.Groups[1].Value);
                continue;
            }

            var propMatch = propPattern.Match(rawLine);
            if (propMatch.Success)
                properties.Add(propMatch.Groups[1].Value);

            foreach (var ch in rawLine)
            {
                if (ch == '{') braceDepth++;
                else if (ch == '}') braceDepth--;
            }

            if (braceDepth == 0)
                break;
        }

        return properties;
    }

    private static HashSet<string> ParseTSInterfaceProperties(string filePath, string interfaceName)
    {
        var properties = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"TypeScript 文件不存在: {filePath}");

        var content = File.ReadAllText(filePath);
        var interfacePattern = new Regex(
            $@"export\s+interface\s+{Regex.Escape(interfaceName)}\s*\{{",
            RegexOptions.Multiline);

        var match = interfacePattern.Match(content);
        if (!match.Success)
            throw new InvalidOperationException($"未找到接口 {interfaceName} 在文件 {filePath}");

        int startIdx = match.Index + match.Length;
        int depth = 1;
        int i = startIdx;
        while (i < content.Length && depth > 0)
        {
            if (content[i] == '{') depth++;
            else if (content[i] == '}') depth--;
            i++;
        }

        var body = content.Substring(startIdx, i - startIdx - 1);
        var lines = body.Split('\n');

        var fieldPattern = new Regex(@"^\s*(\w+)\s*\??\s*:", RegexOptions.Compiled);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*"))
                continue;
            var fieldMatch = fieldPattern.Match(line);
            if (fieldMatch.Success)
            {
                properties.Add(fieldMatch.Groups[1].Value);
            }
        }

        return properties;
    }

    private static void AssertFieldsSynced(
        HashSet<string> csharpFields,
        HashSet<string> tsFields,
        string csharpClassName,
        string tsInterfaceName,
        bool useCamelCaseMapping = true,
        int allowedExtraTSFields = 2)
    {
        var convertedCsharp = useCamelCaseMapping
            ? csharpFields.Select(ToCamelCase).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(csharpFields, StringComparer.OrdinalIgnoreCase);

        var missingInTS = convertedCsharp.Where(f => !tsFields.Contains(f)).OrderBy(f => f).ToList();
        var extraInTS = tsFields.Where(f => !convertedCsharp.Contains(f)).OrderBy(f => f).ToList();

        if (extraInTS.Count > 0)
        {
            Console.WriteLine($"[WARNING] {tsInterfaceName} 比 {csharpClassName} 多出 {extraInTS.Count} 个字段:");
            foreach (var f in extraInTS)
                Console.WriteLine($"  + {f}");
        }

        if (missingInTS.Count > 0)
        {
            Assert.Fail($"{csharpClassName} 有 {missingInTS.Count} 个字段在 {tsInterfaceName} 中缺失:\n" +
                        $"  缺失列表: {string.Join(", ", missingInTS)}\n" +
                        $"  C# 字段 ({csharpFields.Count}): {string.Join(", ", csharpFields.OrderBy(f => f))}\n" +
                        $"  TS 字段 ({tsFields.Count}): {string.Join(", ", tsFields.OrderBy(f => f))}");
        }

        if (extraInTS.Count > allowedExtraTSFields)
        {
            Console.WriteLine($"[WARNING] TS 额外字段数量 ({extraInTS.Count}) 超过允许上限 ({allowedExtraTSFields})");
        }
    }

    [Fact]
    public void CSharp_CalendarEventDto_Fields_Should_Exist_In_Typescript_CalendarEventDto()
    {
        var csharpFields = ParseCSharpClassProperties(CSharpDtoPath, "CalendarEventDto");
        var tsFields = ParseTSInterfaceProperties(TSEngagementPath, "CalendarEventDto");

        Console.WriteLine($"C# CalendarEventDto 字段 ({csharpFields.Count}): {string.Join(", ", csharpFields.OrderBy(f => f))}");
        Console.WriteLine($"TS CalendarEventDto 字段 ({tsFields.Count}): {string.Join(", ", tsFields.OrderBy(f => f))}");

        AssertFieldsSynced(csharpFields, tsFields, "CalendarEventDto", "CalendarEventDto",
            useCamelCaseMapping: true, allowedExtraTSFields: 2);
    }

    [Fact]
    public void CSharp_CalendarOverviewResponse_Fields_Should_Exist_In_Typescript()
    {
        var csharpFields = ParseCSharpClassProperties(CSharpDtoPath, "CalendarOverviewResponse");
        var tsFields = ParseTSInterfaceProperties(TSEngagementPath, "CalendarOverviewResponse");

        Console.WriteLine($"C# CalendarOverviewResponse 字段 ({csharpFields.Count}): {string.Join(", ", csharpFields.OrderBy(f => f))}");
        Console.WriteLine($"TS CalendarOverviewResponse 字段 ({tsFields.Count}): {string.Join(", ", tsFields.OrderBy(f => f))}");

        AssertFieldsSynced(csharpFields, tsFields, "CalendarOverviewResponse", "CalendarOverviewResponse",
            useCamelCaseMapping: true, allowedExtraTSFields: 0);
    }

    [Fact]
    public void CSharp_SubTaskDto_And_TS_SubTask_Fields_Sync()
    {
        var csharpFields = ParseCSharpClassProperties(CSharpSubTaskDtoPath, "SubTaskDto");
        var tsFields = ParseTSInterfaceProperties(TSEngagementPath, "SubTask");

        Console.WriteLine($"C# SubTaskDto 字段 ({csharpFields.Count}): {string.Join(", ", csharpFields.OrderBy(f => f))}");
        Console.WriteLine($"TS SubTask 字段 ({tsFields.Count}): {string.Join(", ", tsFields.OrderBy(f => f))}");

        AssertFieldsSynced(csharpFields, tsFields, "SubTaskDto", "SubTask",
            useCamelCaseMapping: true, allowedExtraTSFields: 0);
    }

    [Fact]
    public void CSharp_RecurrenceExclusionsDto_And_TS_RecurrenceExclusions_Sync()
    {
        var csharpExclusionFields = ParseCSharpClassProperties(CSharpRecurrenceDtoPath, "RecurrenceExclusionsDto");
        var tsExclusionFields = ParseTSInterfaceProperties(TSEngagementPath, "RecurrenceExclusions");

        Console.WriteLine($"C# RecurrenceExclusionsDto 字段 ({csharpExclusionFields.Count}): {string.Join(", ", csharpExclusionFields.OrderBy(f => f))}");
        Console.WriteLine($"TS RecurrenceExclusions 字段 ({tsExclusionFields.Count}): {string.Join(", ", tsExclusionFields.OrderBy(f => f))}");

        AssertFieldsSynced(csharpExclusionFields, tsExclusionFields, "RecurrenceExclusionsDto", "RecurrenceExclusions",
            useCamelCaseMapping: true, allowedExtraTSFields: 0);

        var csharpAddedFields = ParseCSharpClassProperties(CSharpRecurrenceDtoPath, "AddedRecurrenceDto");
        var tsAddedFields = ParseTSInterfaceProperties(TSEngagementPath, "AddedRecurrence");

        Console.WriteLine($"C# AddedRecurrenceDto 字段 ({csharpAddedFields.Count}): {string.Join(", ", csharpAddedFields.OrderBy(f => f))}");
        Console.WriteLine($"TS AddedRecurrence 字段 ({tsAddedFields.Count}): {string.Join(", ", tsAddedFields.OrderBy(f => f))}");

        AssertFieldsSynced(csharpAddedFields, tsAddedFields, "AddedRecurrenceDto", "AddedRecurrence",
            useCamelCaseMapping: true, allowedExtraTSFields: 0);
    }
}
