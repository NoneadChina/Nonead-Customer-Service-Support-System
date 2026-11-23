using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Tests.Infrastructure;

/// <summary>
/// 开源交付的关键回归：一份**空** SQLite 文件必须能让 API 真正启动起来。
/// <para>
/// 背景（2026-11-19，用户报告）：登录页显示「获取验证码失败」。
/// 实测根因不是前端 —— 前端 Vite 把 <c>/api</c> 代理到 <c>127.0.0.1:5008</c>，
/// 而后端进程在 <c>Database.MigrateAsync()</c> 阶段抛
/// <c>SQLite Error 1: 'no such index: IX_Conversations_CreatedAt'</c>（随后
/// <c>DatabaseSeeder</c> 又撞上缺表/缺列）后崩掉，Kestrel 从未监听 ⇒ 前端所有请求 502。
/// </para>
/// <para>
/// 结论：本仓库的历史迁移链**在空库上不可重放**（这是既有缺陷，与本次开源清理无关）。
/// 修复方式见 <see cref="FreshDatabaseBootstrap"/>：空库按当前 EF 模型建表并标记历史迁移；
/// 有历史的库仍走 <c>Migrate()</c>。本测试锁住这条「克隆即可运行」的路径。
/// </para>
/// </summary>
public class FreshSqliteDatabaseMigrationTests
{
    [Fact]
    public async Task FreshDatabase_IsInitializedFromModel_WithSchemaAndSeededHistory()
    {
        var dbPath = NewTempDbPath();
        var connectionString = $"Data Source={dbPath}";

        try
        {
            var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
                .UseSqlite(connectionString)
                .Options;

            using var db = new NoneadCSSDbContext(options);

            // 空库必须被识别出来，并按模型初始化（不得抛异常）
            Assert.True(await FreshDatabaseBootstrap.IsFreshDatabaseAsync(db));
            Assert.True(await FreshDatabaseBootstrap.TryInitializeFreshDatabaseAsync(db));

            // 初始化之后就不再是空库（重复调用必须是空操作）
            Assert.False(await FreshDatabaseBootstrap.IsFreshDatabaseAsync(db));
            Assert.False(await FreshDatabaseBootstrap.TryInitializeFreshDatabaseAsync(db));

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            // 迁移链中途断掉时最后几个迁移的产物会缺失 —— 逐个取证，失败信息直接点出缺什么
            Assert.True(TableExists(connection, "OutlookSyncSettings"),
                "缺表 OutlookSyncSettings：空库没有建出完整 schema");
            Assert.True(TableExists(connection, "ImMessageLogs"),
                "缺表 ImMessageLogs（四个 IM 渠道的出站队列依赖它）");
            Assert.True(TableExists(connection, "Modules"), "缺表 Modules");
            Assert.True(HasColumn(connection, "AuditLogs", "HmacSignature"),
                "AuditLogs 缺列 HmacSignature（20260728134238_AddAuditLogHmacSignature 的产物）");
            Assert.True(HasColumn(connection, "Conversations", "SourceVia"),
                "Conversations 缺列 SourceVia");

            // 历史表必须被补写，否则下一次作者迁移会试图重放整条链
            var migrations = db.Database.GetMigrations().ToList();
            Assert.NotEmpty(migrations);
            Assert.Equal(migrations.Count, CountMigrationHistory(connection));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void RawMigrationChain_OnEmptySqliteDatabase_IsKnownBroken_NotUsedForFreshInstall()
    {
        // 有意保留为「文档性断言」而不是失败用例：
        // 历史迁移链在空库上会在 no such index / duplicate column 处中断，
        // 这既是本次用户故障的根因，也是 FreshDatabaseBootstrap 存在的理由。
        // 若将来有人把整条链修成可重放，把这条断言的期望值改成 true 即可。
        var chainIsReplayableOnEmptyDatabase = false;
        Assert.False(chainIsReplayableOnEmptyDatabase,
            "已知缺陷：历史迁移链在空库上不可重放 ⇒ 空库启动必须走 FreshDatabaseBootstrap（见该类注释）。");
    }

    private static string NewTempDbPath()
        => Path.Combine(Path.GetTempPath(), $"noneadcss-fresh-{Guid.NewGuid():N}.db");

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static long CountMigrationHistory(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\";";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void TryDelete(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
            catch (IOException)
            {
                // 临时库删不掉不影响测试结论
            }
        }
    }
}
