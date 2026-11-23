using FluentAssertions;
using Xunit;

namespace NoneadCSS.Tests.Infrastructure;

/// <summary>
/// 健康检查端点守卫回归测试（M-04）。
///
/// 背景：/health（liveness）与 /health/ready（readiness）是无认证的匿名端点，
/// 若输出内部依赖的 description / data（可能包含 SQL 片段、内部路径），会被公网探测到，
/// 进而暴露应用内部结构。历史上 WriteHealthCheckResponseAsync 会输出完整 entries。
///
/// 本测试锁定不可退化的约束：
/// 1. /health 必须是不执行任何检查的最小化 liveness 探针（Predicate = _ => false）；
/// 2. liveness 写入器只输出 { status }，绝不引用 entries/description/data；
/// 3. readiness 在 Production 环境只输出整体状态，仅在其他环境输出 entries 详情；
/// 4. 完整细节的 WriteHealthCheckResponseAsync 不得再被映射到任何健康探针。
/// </summary>
public class HealthEndpointGuardRailTests
{
    private const string SourceFile = "Program.cs";
    private const string SolutionFile = "NoneadCSS.slnx";

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFile)))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"无法找到解决方案根目录（{SolutionFile}）");
    }

    private static string ReadSource()
    {
        var path = Path.Combine(FindSolutionRoot(), "NoneadCSS.Api", SourceFile);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 提取从 startMarker 开始、到下一个 "static async Task" 或文件末尾的方法体源码片段。
    /// </summary>
    private static string ExtractMethodBody(string source, string startMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"源码中应包含方法定义：{startMarker}");

        var nextMethod = source.IndexOf("static async Task", start + startMarker.Length, StringComparison.Ordinal);
        var end = nextMethod >= 0 ? nextMethod : source.Length;
        return source.Substring(start, end - start);
    }

    [Fact]
    public void LivenessEndpoint_ShouldMapToMinimalWriter()
    {
        var source = ReadSource();

        source.Should().Contain("MapHealthChecks(\"/health\", new HealthCheckOptions",
            "/health 必须映射为健康检查端点");
        source.Should().Contain("Predicate = _ => false",
            "liveness 探针应不执行任何检查（Predicate = _ => false），仅探测进程存活");
        source.Should().Contain("ResponseWriter = WriteLivenessResponseAsync",
            "/health 应使用最小化 liveness 写入器 WriteLivenessResponseAsync");
    }

    [Fact]
    public void ReadinessEndpoint_ShouldMapToReadinessWriter()
    {
        var source = ReadSource();

        source.Should().Contain("MapHealthChecks(\"/health/ready\", new HealthCheckOptions",
            "/health/ready 必须映射为健康检查端点");
        source.Should().Contain("Predicate = check => check.Tags.Contains(\"ready\")",
            "readiness 探针应仅检查带 \"ready\" 标签的依赖");
        source.Should().Contain("ResponseWriter = WriteReadinessResponseAsync",
            "/health/ready 应使用分级披露写入器 WriteReadinessResponseAsync");
    }

    [Fact]
    public void LivenessWriter_ShouldNotLeakEntryDetails()
    {
        var source = ReadSource();
        var liveness = ExtractMethodBody(source, "static async Task WriteLivenessResponseAsync");

        liveness.Should().Contain("{ status = report.Status.ToString() }",
            "liveness 应只输出最简 JSON { status: ... }");

        // liveness 绝不应序列化 entries —— 那会泄露依赖结构 / SQL 片段 / 内部路径
        liveness.Should().NotContain("Entries",
            "liveness 探针不应序列化 entries，防止内部依赖结构泄漏");
        liveness.Should().NotContain(".Description",
            "liveness 探针不应输出 description（可能含内部路径/依赖提示）");
        liveness.Should().NotContain("data =",
            "liveness 探针不应输出 data（可能含 SQL 片段/堆栈等内部信息）");
    }

    [Fact]
    public void ReadinessWriter_ShouldHideDetailsInProduction()
    {
        var source = ReadSource();
        var readiness = ExtractMethodBody(source, "static async Task WriteReadinessResponseAsync");

        readiness.Should().NotContain("WriteHealthCheckResponseAsync",
            "方法边界提取异常，不应混入通用写入器");
        readiness.Should().Contain("env.IsProduction()",
            "readiness 写入器必须根据环境（Production）分级披露");

        // Production 分支必须早于 entries 序列化，即"整体状态"优先返回
        var productionReturn = readiness.IndexOf("{ status = report.Status.ToString() }", StringComparison.Ordinal);
        var entriesSerialization = readiness.IndexOf("var entries = report.Entries", StringComparison.Ordinal);
        productionReturn.Should().BeGreaterThanOrEqualTo(0,
            "readiness 写入器应包含只输出整体状态的分支");
        entriesSerialization.Should().BeGreaterThan(productionReturn,
            "readiness 在 Production 环境应在 entries 序列化之前就返回整体状态，避免详情泄漏");
    }

    [Fact]
    public void FullDetailWriter_ShouldNotBeUsedByAnonymousProbes()
    {
        var source = ReadSource();

        // 完整细节写入器 WriteHealthCheckResponseAsync 必须在 M-04 已移除（它是未使用的死代码），
        // 以确保任何匿名健康探针都不可能映射到输出 entries/description/data 的写入器。
        source.Should().NotContain("WriteHealthCheckResponseAsync",
            "不应再存在通用完整细节写入器 WriteHealthCheckResponseAsync；它曾将 entries/description/data 输出到匿名健康探针，存在内部信息泄漏风险");
    }
}
