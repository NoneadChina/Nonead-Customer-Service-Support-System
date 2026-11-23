using FluentAssertions;
using Xunit;

namespace NoneadCSS.Tests.Infrastructure;

/// <summary>
/// SQLite 连接 PRAGMA 守卫回归测试（M-06）。
///
/// 背景：AuditSaveChangesInterceptor 的原始审计 SQL 使用 "AuditLogs" / "EntityId" / "Id"
/// 双引号包裹标识符。若连接未启用 sql_standard_strings（SQLite 某些连接的默认值为 OFF），
/// 双引号会被当作字符串字面量解析，导致 ExecuteSqlRaw 生成语法错误。
/// 因此连接打开时必须统一设置 PRAGMA sql_standard_strings = ON。
///
/// 本测试锁定该约束在异步与同步两条连接打开路径上都不可退化。
/// </summary>
public class SqlitePragmaGuardRailTests
{
    private const string SourceFile = "SqliteConnectionInterceptor.cs";
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
        var path = Path.Combine(FindSolutionRoot(), "NoneadCSS.Infrastructure", "Persistence", SourceFile);
        return File.ReadAllText(path);
    }

    [Fact]
    public void ConnectionOpened_ShouldEnableSqlStandardStrings()
    {
        var source = ReadSource();

        source.Should().Contain("PRAGMA sql_standard_strings=ON;",
            "连接打开时必须启用 sql_standard_strings，保证双引号按标准 SQL 标识符解析而非字符串字面量");
    }

    [Fact]
    public void SyncConnectionOpened_ShouldAlsoSetPragma()
    {
        var source = ReadSource();

        source.Should().Contain("public override void ConnectionOpened(",
            "同步连接打开路径应存在，避免仅异步路径生效导致部分场景未启用 PRAGMA");
        source.Should().Contain("public override async Task ConnectionOpenedAsync(",
            "异步连接打开路径应存在");
    }

    [Fact]
    public void SqlStandardStrings_ShouldNotBeCommentedOut()
    {
        var source = ReadSource();

        // 说明性注释（以 // 开头）也可能包含关键字，需先排除注释行再检查可执行代码。
        var activeCodeLines = source.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("//"))
            .ToList();

        activeCodeLines.Should().Contain(l => l.Contains("PRAGMA sql_standard_strings=ON;"),
            "sql_standard_strings 的设置必须是可执行代码而非被注释掉，否则双引号标识符可能被解析为字符串导致审计 SQL 语法错误");
    }
}
