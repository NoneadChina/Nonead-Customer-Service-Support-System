using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace NoneadCSS.Tests.Infrastructure;

/// <summary>
/// Serilog 滚动保留策略守卫回归测试（M-09）。
///
/// 背景：金融/医疗等合规审计要求日志保留 180 天以上。若 retainedFileCountLimit 过小（如 30），
/// 高日志量下 30 天前的日志会被提前滚动删除，且 Serilog 文件日志用于排错。
/// 同时需要将 Warning 及以上级别复制到独立 archive 文件（不滚动删除），便于长期归档冷存储。
///
/// 本测试锁定：
/// 1. 主 File sink 的 retainedFileCountLimit >= 180（满足合规保留期）；
/// 2. 存在一个 archive File sink，restrictedToMinimumLevel = Warning，且不设置滚动删除上限；
/// 3. 主日志路径仍按天滚动。
/// </summary>
public class SerilogRetentionGuardRailTests
{
    private const string ConfigFile = "appsettings.json";
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

    // JsonElement 依赖其所属 JsonDocument 的生存期，因此在测试类内缓存该文档，
    // 避免 Using 释放后返回的 JsonElement 变成悬空引用。
    private static JsonDocument? _doc;

    private static JsonElement ReadSerilogWriteTo()
    {
        if (_doc != null)
            return _doc.RootElement.GetProperty("Serilog").GetProperty("WriteTo");

        var path = Path.Combine(FindSolutionRoot(), "NoneadCSS.Api", ConfigFile);
        // appsettings.json 顶部包含 // 注释，System.Text.Json 默认拒绝注释，需显式跳过。
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
        _doc = JsonDocument.Parse(File.ReadAllText(path), options);

        var writeTo = _doc.RootElement.GetProperty("Serilog").GetProperty("WriteTo");
        writeTo.ValueKind.Should().Be(JsonValueKind.Array,
            "Serilog:WriteTo 应为数组，实际为 {0}", writeTo.ValueKind);
        return writeTo;
    }

    private static JsonElement? FindFileSink(JsonElement writeTo, Func<JsonElement, bool> predicate)
    {
        foreach (var sink in writeTo.EnumerateArray())
        {
            if (sink.TryGetProperty("Name", out var name)
                && name.GetString() == "File"
                && predicate(sink))
            {
                return sink;
            }
        }
        return null;
    }

    private static JsonElement GetArgs(JsonElement sink)
    {
        return sink.GetProperty("Args");
    }

    [Fact]
    public void MainLogSink_ShouldRetainAtLeast180Days()
    {
        var writeTo = ReadSerilogWriteTo();

        // 主日志 sink：path 为 logs/log-.log，retainedFileCountLimit >= 180
        var main = FindFileSink(writeTo, s =>
            s.TryGetProperty("Args", out var args)
            && args.TryGetProperty("path", out var path)
            && path.GetString()?.Contains("log-.log", StringComparison.Ordinal) == true);

        main.Should().NotBeNull("应存在主日志 File sink（路径为 logs/log-.log）");

        var args = GetArgs(main!.Value);
        args.TryGetProperty("retainedFileCountLimit", out var limit)
            .Should().BeTrue("主日志应配置 retainedFileCountLimit");

        limit.GetInt32().Should().BeGreaterThanOrEqualTo(180,
            "合规审计要求日志保留 >= 180 天，retainedFileCountLimit 过小会导致早于保留期的日志被滚动删除");

        args.TryGetProperty("rollingInterval", out var rolling).Should().BeTrue();
        rolling.GetString().Should().Be("Day", "主日志应按天滚动");
    }

    [Fact]
    public void ArchiveSink_ShouldCollectWarningAndAbove_WithoutRollingDeletion()
    {
        var writeTo = ReadSerilogWriteTo();

        var archive = FindFileSink(writeTo, s =>
            s.TryGetProperty("Args", out var args)
            && args.TryGetProperty("path", out var path)
            && path.GetString()?.Contains("archive-", StringComparison.Ordinal) == true
            && args.TryGetProperty("restrictedToMinimumLevel", out var level)
            && level.GetString() == "Warning");

        archive.Should().NotBeNull(
            "应存在 archive File sink：路径含 archive-，且 restrictedToMinimumLevel = Warning，用于长期归档 Warning 及以上日志");

        var args = GetArgs(archive!.Value);

        // 归档 sink 不应设置滚动删除上限（retainedFileCountLimit 为 null），实现"不滚动删除"
        if (args.TryGetProperty("retainedFileCountLimit", out var limit))
        {
            limit.ValueKind.Should().Be(JsonValueKind.Null,
                "归档 sink 不应设置 retainedFileCountLimit，避免长期归档被自动删除");
        }

        args.TryGetProperty("path", out var path2).Should().BeTrue();
        path2.GetString().Should().Contain("archive-", "归档 sink 路径应包含 archive- 前缀");
    }
}
