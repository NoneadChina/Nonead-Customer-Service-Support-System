using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Tests.Infrastructure;

/// <summary>
/// DbContext 配置回归测试。
///
/// 历史背景：全局 UseQuerySplittingBehavior(SplitQuery) 在 SQLite 上会触发
/// Error 5 'unable to delete/modify user-function due to active statements'，
/// 污染连接池后级联导致 ObjectDisposedException 和配置加载失败（回退到空基线）。
///
/// 这些测试确保：
/// 1. 生产代码不重新引入全局 SplitQuery 配置
/// 2. MultipleCollectionIncludeWarning 被正确抑制（避免日志噪音）
/// 3. SQLite 下多集合 Include 查询使用 SingleQuery 行为
/// </summary>
public class DbContextConfigurationRegressionTests
{
    private const string SourceFileName = "DependencyInjection.cs";
    private const string SolutionFile = "NoneadCSS.slnx";

    /// <summary>
    /// 从测试程序集位置向上查找解决方案根目录。
    /// </summary>
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

    private static string ReadSourceFile()
    {
        var path = Path.Combine(FindSolutionRoot(), "NoneadCSS.Infrastructure", SourceFileName);
        return File.ReadAllText(path);
    }

    [Fact]
    public void DependencyInjection_ShouldNotConfigureGlobalSplitQuery()
    {
        // 防止回归：全局 SplitQuery 在 SQLite 上触发 Error 5，
        // 污染连接池后级联导致 ObjectDisposedException 和配置加载失败
        var source = ReadSourceFile();

        source.Should().NotContain("UseQuerySplittingBehavior",
            "全局 UseQuerySplittingBehavior(SplitQuery) 在 SQLite 上会触发 Error 5 'unable to delete/modify user-function due to active statements'，导致连接池污染和 ObjectDisposedException");

        source.Should().NotContain("QuerySplittingBehavior.SplitQuery",
            "禁止全局配置 SplitQuery 查询拆分行为。如需对特定查询使用 SplitQuery，应在查询级别使用 AsSplitQuery()");
    }

    [Fact]
    public void DependencyInjection_ShouldSuppressMultipleCollectionIncludeWarning()
    {
        // 防止回归：移除全局 SplitQuery 后需抑制 EF Core 的 MultipleCollectionIncludeWarning 警告
        var source = ReadSourceFile();

        source.Should().Contain("MultipleCollectionIncludeWarning",
            "移除全局 SplitQuery 后必须抑制 MultipleCollectionIncludeWarning 警告，避免日志噪音");

        source.Should().Contain("ConfigureWarnings",
            "必须通过 ConfigureWarnings 配置警告行为");
    }

    [Fact]
    public async Task SqliteDbContext_WithSingleQuery_ShouldExecuteMultiCollectionIncludeWithoutError()
    {
        // 行为测试：验证 SQLite 下多集合 Include 查询使用 SingleQuery 能正常工作
        // 不抛出 SQLite Error 5 或其他连接相关异常
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        await using var context = new NoneadCSSDbContext(options);
        await context.Database.EnsureCreatedAsync();

        // 执行多集合 Include 查询（与 ConversationRepository.ApplyFullIncludes 类似）
        // SingleQuery 模式下应正常执行，不抛出 SQLite Error 5
        var query = context.Conversations
            .Include(c => c.Threads)
            .Include(c => c.Followers)
            .Include(c => c.ConversationTags)
            .AsSingleQuery();

        // Act — 迭代查询结果不应抛出 SQLite Error 5（空表也应正常查询）
        var result = await query.FirstOrDefaultAsync();

        // Assert
        result.Should().BeNull(); // 空表，无数据
    }

    /// <summary>
    /// 生产隐患（2026-09-21 审计）：模型声明 CK_SendLog_Status CHECK (Status IN (1,2,3,4,5))，
    /// 但 SendLogRepository 实际定义并使用 6/7/8/9/10
    /// （Opened / Clicked / Unsubscribed / Complained / SendIntermediateError）。
    /// 生产库当前因该约束未落表而侥幸正常——一旦 EnsureCreated 或 SQLite 表重建让约束生效，
    /// 邮件发送队列与打开/点击追踪会同时整表写入失败。
    ///
    /// 本用例锁定：CHECK 约束必须覆盖 SendLogStatus 中的**每一个**常量；
    /// 将来新增状态常量而未同步约束时，本用例立即失败。
    /// </summary>
    [Fact]
    public void SendLogStatusCheckConstraint_MustAllowEveryStatusValueUsedByCode()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(connection)
            .Options;
        using var context = new NoneadCSSDbContext(options);

        // CHECK 约束不存放在运行期只读模型中，必须取 design-time 模型
        var model = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var entityType = model.FindEntityType(typeof(SendLog));
        entityType.Should().NotBeNull();

        var constraint = entityType!.GetCheckConstraints()
            .SingleOrDefault(c => c.Name == "CK_SendLog_Status");
        constraint.Should().NotBeNull("SendLogs.Status 必须保留 CHECK 约束声明");
        var sql = constraint!.Sql!;

        // 解析约束中允许的取值集合（精确比较，避免 "1" 被 "10" 子串误判为通过）
        var allowed = System.Text.RegularExpressions.Regex
            .Matches(sql, @"\d+")
            .Select(m => int.Parse(m.Value))
            .ToHashSet();

        var statuses = typeof(NoneadCSS.Infrastructure.Repositories.SendLogStatus)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int)
                        && f.Name != nameof(NoneadCSS.Infrastructure.Repositories.SendLogStatus.MaxRetryCount))
            .Select(f => (Name: f.Name, Value: (int)f.GetRawConstantValue()!))
            .ToList();

        statuses.Should().NotBeEmpty("必须能从 SendLogStatus 枚举出状态常量");

        foreach (var (name, value) in statuses)
        {
            allowed.Should().Contain(value,
                $"SendLogStatus.{name}={value} 会被写入 SendLogs.Status，CHECK 约束必须允许它，" +
                "否则该约束一旦生效（新建库 / SQLite 表重建）将导致整表写入失败");
        }
    }
}
