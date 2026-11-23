using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Persistence;

/// <summary>
/// 全新数据库（空库）初始化。
/// <para>
/// 背景（2026-11-19）：本仓库的历史迁移链在**空库**上无法重放 —— 逐个踩到
/// <c>SQLite Error 1: no such index: IX_Conversations_CreatedAt</c>、
/// <c>duplicate column name: Avatar</c>、<c>table AuditLogs has no column named HmacSignature</c> 等，
/// 于是 <c>Database.MigrateAsync()</c> 中断、<c>DatabaseSeeder</c> 继而在缺表/缺列上崩溃，
/// API 进程在 Kestrel 监听之前就退出 —— 现象就是前端登录页「获取验证码失败」（Vite 代理到
/// <c>127.0.0.1:5008</c> 得到 502）。这些缺陷只影响「从零建库」，既有生产库（迁移历史齐全）
/// 不受影响，因此这里不改写几十个历史迁移，而是给空库一条等价且可重复的建库路径。
/// </para>
/// <para>
/// 做法：空库 ⇒ 按**当前 EF 模型**建表（<see cref="DatabaseFacade.EnsureCreatedAsync(CancellationToken)"/>），
/// 然后把全部迁移 id 写入 <c>__EFMigrationsHistory</c>，使后续新增的作者迁移可以正常叠加。
/// 有历史的库 ⇒ 仍然走 <c>MigrateAsync()</c>，与以往行为完全一致。
/// </para>
/// </summary>
public static class FreshDatabaseBootstrap
{
    /// <summary>判断目标库是否为「空库」：无任何用户表（忽略 sqlite 内部表与迁移历史表）。</summary>
    public static async Task<bool> IsFreshDatabaseAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = context.Database.IsSqlite()
                ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory';"
                : "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory';";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var count = Convert.ToInt64(result ?? 0L);
            return count == 0;
        }
        catch (Exception)
        {
            // 探测失败时按「非空库」处理：宁可走原有迁移路径，也不要误判成空库去重建 schema
            return false;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// 按当前模型建库，并把全部迁移标记为已应用。返回是否真的执行了初始化（false 表示库非空、未做任何改动）。
    /// </summary>
    public static async Task<bool> TryInitializeFreshDatabaseAsync(
        DbContext context,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (!await IsFreshDatabaseAsync(context, cancellationToken))
        {
            return false;
        }

        var provider = context.Database.ProviderName ?? "(unknown)";
        logger?.LogInformation("检测到空数据库（provider={Provider}），按当前 EF 模型建库（跳过历史迁移重放）", provider);

        await context.Database.EnsureCreatedAsync(cancellationToken);
        await SeedMigrationHistoryAsync(context, logger, cancellationToken);

        logger?.LogInformation("空库初始化完成：已按模型建表，并把 {Count} 条迁移标记为已应用",
            context.Database.GetMigrations().Count());
        return true;
    }

    private static async Task SeedMigrationHistoryAsync(
        DbContext context,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            // EF 迁移历史表里记录的 ProductVersion 只用于诊断，取 EF 程序集版本即可
            var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "10.0.0";

            await using (var create = connection.CreateCommand())
            {
                create.CommandText = context.Database.IsSqlite()
                    ? "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);"
                    : "IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL CREATE TABLE [__EFMigrationsHistory] ([MigrationId] nvarchar(150) NOT NULL CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY, [ProductVersion] nvarchar(32) NOT NULL);";
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            var migrations = context.Database.GetMigrations().ToList();
            var applied = new HashSet<string>(context.Database.GetAppliedMigrations(), StringComparer.Ordinal);

            foreach (var migration in migrations)
            {
                if (applied.Contains(migration))
                {
                    continue;
                }

                await using var insert = connection.CreateCommand();
                insert.CommandText = context.Database.IsSqlite()
                    ? "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($id, $ver);"
                    : "IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = @id) INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (@id, @ver);";

                var idParam = insert.CreateParameter();
                idParam.ParameterName = context.Database.IsSqlite() ? "$id" : "@id";
                idParam.Value = migration;
                insert.Parameters.Add(idParam);

                var verParam = insert.CreateParameter();
                verParam.ParameterName = context.Database.IsSqlite() ? "$ver" : "@ver";
                verParam.Value = productVersion;
                insert.Parameters.Add(verParam);

                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // 历史表补写失败不应阻断启动：库结构与模型已一致，最坏情况是下次迁移试图重放
            logger?.LogWarning(ex, "写入 __EFMigrationsHistory 失败（库结构已按模型建好，可继续启动）");
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
