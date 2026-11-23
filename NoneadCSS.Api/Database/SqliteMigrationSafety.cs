using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Api.Database;

/// <summary>
/// SQLite 代码迁移管道的安全加固辅助：
/// 1) MigrateAsync 前做「冷备 + 轮转」（仅当实际连接到 SQLite 文件型库时执行）；
/// 2) MigrateAsync 后做「自校验」，用 PRAGMA table_info / INFORMATION_SCHEMA / sys.columns
///    确认 WorkflowExecutionLogs.ConversationId 确实可空（即本次补丁的核心目标）；
/// 3) 提供 ApplyWorkflowExecutionLogConversationIdNullableDirect 作为「EF 迁移链因脏模型
///    漂移而无法整体跑通」时的 SQL 级补丁兜底。
/// </summary>
public static class SqliteMigrationSafety
{
    /// <summary>备份保留数量上限（保留最近 N 份，超过后删除最旧的）。</summary>
    private const int MaxBackupKeep = 10;

    /// <summary>
    /// 若当前数据库为 SQLite 文件型库且文件已存在：先冷备到 dbDir/backups 目录，再执行保留策略。
    /// 冷备使用 SQLite 官方的 Backup API（经由 Microsoft.Data.Sqlite SqliteConnection.BackupDatabase），
    /// 能对打开中的连接安全拷贝，避免文件级拷贝出现 "database is locked" 或损坏的半页。
    /// 返回实际写入的备份文件路径；若未执行备份则返回 null。
    /// </summary>
    public static async Task<string?> BackupBeforeMigrateAsync(
        NoneadCSSDbContext context,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        // Docker bind mount（WSL2 + Windows NTFS）下 SQLite 官方 BackupDatabase API 可能触发
        // SQLITE_IOERR_BACKUP (4618)；这种部署形态下可通过环境变量 DB__SkipBackupBeforeMigrate=true
        // 跳过此处冷备，依靠外部运维备份策略（host 侧文件级拷贝 / volume 快照）兜底。
        var skip = Environment.GetEnvironmentVariable("DB__SkipBackupBeforeMigrate");
        if (string.Equals(skip, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(skip, "1", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("DB__SkipBackupBeforeMigrate=true：跳过 SQLite 迁移前冷备（Docker bind mount 部署形态，依赖外部运维备份）");
            return null;
        }

        // 只处理 SQLite；其他 Provider 自有运维级备份机制（SQL Server Backups / pg_basebackup 等），不再此处插手
        if (!IsSqliteProvider(context.Database))
        {
            return null;
        }

        // SQLite 使用文件路径时可从连接字符串中提取 Data Source；若为空说明是 :memory: 或共享内存之类，跳过冷备
        var dataSource = TryExtractSqliteDataSource(context.Database);
        if (string.IsNullOrEmpty(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogDebug("SQLite 数据源为内存库或空值，跳过冷备");
            return null;
        }

        // 将相对路径解析为绝对路径，保证备份目录放在 db 文件旁边
        var dbFile = new FileInfo(Path.IsPathRooted(dataSource)
            ? dataSource
            : Path.Combine(AppContext.BaseDirectory, dataSource));

        if (!dbFile.Exists)
        {
            // 首次启动（空库）时不存在文件，不需要备份；MigrateAsync 会新建，后续启动才会有备份
            logger?.LogInformation("SQLite 数据库文件不存在（首次建库），跳过冷备: {Path}", dbFile.FullName);
            return null;
        }

        var backupsDir = EnsureBackupDirectory(dbFile.Directory!);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var backupFile = Path.Combine(backupsDir, $"{dbFile.Name}.{stamp}.bak");

        try
        {
            // 1) 打开一个临时备份连接（同一 Provider）
            await using var sourceConn = (SqliteConnection)context.Database.GetDbConnection();
            var wasClosed = sourceConn.State == System.Data.ConnectionState.Closed;
            if (wasClosed)
            {
                await sourceConn.OpenAsync(cancellationToken);
            }

            // 通过 SqliteConnectionStringBuilder 禁用 Pooling，防止备份连接占住文件句柄
            var backupCs = new SqliteConnectionStringBuilder
            {
                DataSource = backupFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
            };
            await using var targetConn = new SqliteConnection(backupCs.ToString());
            await targetConn.OpenAsync(cancellationToken);

            // 2) 官方备份 API：按页拷贝，内部处理写锁等待
            // 重载签名：BackupDatabase(SqliteConnection destination) 或
            //          BackupDatabase(SqliteConnection destination, string destinationName, string sourceName)
            sourceConn.BackupDatabase(targetConn, destinationName: "main", sourceName: "main");
            await targetConn.CloseAsync();

            if (wasClosed)
            {
                await sourceConn.CloseAsync();
            }

            var fi = new FileInfo(backupFile);
            logger?.LogInformation(
                "SQLite 迁移前冷备完成: {BackupPath}，大小 {SizeKB:N0} KB (源库: {SourcePath})",
                backupFile, fi.Length / 1024, dbFile.FullName);
        }
        catch (Exception ex)
        {
            // 生产环境：冷备失败直接中止迁移；宁可停机也不能在没备份的情况下跑重建表迁移
            logger?.LogCritical(ex, "SQLite 迁移前冷备失败，终止迁移执行: {BackupPath}", backupFile);
            throw new InvalidOperationException("SQLite 迁移前冷备失败；为避免不可逆的 4 步重建表迁移损坏数据，已中止。请检查 backups 目录磁盘空间/权限后重试。", ex);
        }

        // 3) 轮转保留：删除超过 MaxBackupKeep 的旧备份
        TrimBackupRetain(backupsDir, dbFile.Name, MaxBackupKeep, logger);

        // 4) 额外生成一份 SHA256 旁录，便于事后校验
        EmitBackupChecksum(backupFile, logger);

        return backupFile;
    }

    /// <summary>
    /// MigrateAsync 后的结构自校验：确认 WorkflowExecutionLogs.ConversationId 已可空。
    /// 覆盖 SQLite (PRAGMA table_info) / SQL Server (sys.columns) / PostgreSQL (information_schema) 三种最常见 Provider。
    /// 若为其他 Provider 则返回 NotChecked。
    /// </summary>
    public static async Task<MigrationCheckResult> CheckWorkflowExecutionLogConversationIdNullableAsync(
        NoneadCSSDbContext context,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var database = context.Database;
            var conn = database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            bool? nullable = null;
            string probe;
            await using (var cmd = conn.CreateCommand())
            {
                if (IsSqliteProvider(database))
                {
                    cmd.CommandText = "SELECT \"notnull\" FROM pragma_table_info('WorkflowExecutionLogs') WHERE name = 'ConversationId' LIMIT 1";
                    var raw = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (raw == null || raw == DBNull.Value)
                    {
                        return new MigrationCheckResult(false, false, "SQLite: 未找到 WorkflowExecutionLogs.ConversationId 列");
                    }
                    // PRAGMA: notnull = 1 表示 NOT NULL，= 0 表示可空
                    nullable = Convert.ToInt64(raw) == 0;
                    probe = "pragma_table_info";
                }
                else if (IsSqlServerProvider(database))
                {
                    cmd.CommandText = @"
SELECT CASE WHEN c.is_nullable = 1 THEN 1 ELSE 0 END
FROM sys.columns c
JOIN sys.tables  t ON t.object_id = c.object_id
WHERE t.name = 'WorkflowExecutionLogs' AND c.name = 'ConversationId'";
                    var raw = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (raw == null || raw == DBNull.Value)
                    {
                        return new MigrationCheckResult(false, false, "SQL Server: 未找到 WorkflowExecutionLogs.ConversationId 列");
                    }
                    nullable = Convert.ToInt32(raw) == 1;
                    probe = "sys.columns";
                }
                else if (IsPostgresProvider(database))
                {
                    cmd.CommandText = @"
SELECT CASE WHEN is_nullable = 'YES' THEN 1 ELSE 0 END
FROM information_schema.columns
WHERE table_name = 'workflowexecutionlogs' AND column_name = 'conversationid'";
                    var raw = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (raw == null || raw == DBNull.Value)
                    {
                        return new MigrationCheckResult(false, false, "PostgreSQL: 未找到 WorkflowExecutionLogs.ConversationId 列");
                    }
                    nullable = Convert.ToInt32(raw) == 1;
                    probe = "information_schema";
                }
                else
                {
                    return new MigrationCheckResult(
                        Nullable: null,
                        Checked: false,
                        Reason: $"当前 Provider {database.ProviderName} 未实现 ConversationId NULL 性校验，跳过自校验");
                }
            }

            logger?.Log(
                nullable.GetValueOrDefault() ? LogLevel.Information : LogLevel.Error,
                "迁移后自校验 ({Probe})：WorkflowExecutionLogs.ConversationId NULLABLE = {Nullable}",
                probe, nullable);

            return new MigrationCheckResult(nullable, Checked: true, Reason: probe);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "迁移后自校验 WorkflowExecutionLogs.ConversationId NULL 性失败");
            return new MigrationCheckResult(false, false, "自校验异常: " + ex.Message);
        }
    }

    private static bool IsSqliteProvider(DatabaseFacade db)
        => db.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsSqlServerProvider(DatabaseFacade db)
        => db.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true
        || db.ProviderName?.Contains("EntityFrameworkCore.SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsPostgresProvider(DatabaseFacade db)
        => db.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
        || db.ProviderName?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) == true
        || db.ProviderName?.Contains("Postgres", StringComparison.OrdinalIgnoreCase) == true;

    private static string? TryExtractSqliteDataSource(DatabaseFacade db)
    {
        try
        {
            var cs = db.GetDbConnection().ConnectionString;
            if (string.IsNullOrEmpty(cs)) return null;
            var sb = new SqliteConnectionStringBuilder(cs);
            return sb.DataSource;
        }
        catch
        {
            return null;
        }
    }

    private static string EnsureBackupDirectory(DirectoryInfo dbDir)
    {
        var backups = Path.Combine(dbDir.FullName, "backups");
        Directory.CreateDirectory(backups);
        return backups;
    }

    private static void TrimBackupRetain(string backupsDir, string dbFileName, int maxKeep, ILogger? logger)
    {
        try
        {
            var files = Directory.EnumerateFiles(backupsDir, $"{dbFileName}.*.bak", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            if (files.Count <= maxKeep) return;

            foreach (var obsolete in files.Skip(maxKeep))
            {
                try
                {
                    obsolete.Delete();
                    logger?.LogDebug("SQLite 轮转：删除旧备份 {File}", obsolete.FullName);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "SQLite 轮转：删除旧备份失败（忽略）: {File}", obsolete.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SQLite 轮转：扫描/清理旧备份失败（忽略）");
        }
    }

    private static void EmitBackupChecksum(string backupFile, ILogger? logger)
    {
        try
        {
            using var fs = File.OpenRead(backupFile);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(fs);
            var hex = string.Concat(hash.Select(b => b.ToString("x2")));
            var sidecar = backupFile + ".sha256";
            File.WriteAllText(sidecar,
                $"{hex}  {Path.GetFileName(backupFile)}{Environment.NewLine}" +
                $"SourceTimeUtc: {DateTime.UtcNow:o}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SQLite 冷备：写入 .sha256 旁录失败（忽略）: {Backup}", backupFile);
        }
    }

    /// <summary>
    /// 当 EF 整体迁移链因「历史迁移模型漂移」而无法在空库/旧库上一次跑通时，
    /// 提供 SQL 级的兜底补丁：仅处理本次补丁目标
    /// —— 把 WorkflowExecutionLogs.ConversationId 从 NOT NULL 改为 NULLABLE。
    ///
    /// 说明：
    /// - SQLite 不支持 ALTER COLUMN，因此采用与 EF 相同的 4 步重建表策略（CREATE temp →
    ///   INSERT → DROP → RENAME），并在事务 + PRAGMA foreign_keys=0 中执行。
    /// - 对 SQL Server：先动态删 default constraint，再 ALTER TABLE ... ALTER COLUMN ... NULL。
    /// - 本方法是「幂等」的：执行之前先检查当前 notnull 状态，已为 NULL 则直接返回跳过状态。
    ///
    /// 返回：Applied=真 代表实际改了表结构；Skipped=真 表示列已经 NULLABLE 无需动作。
    /// </summary>
    public static async Task<DirectPatchResult> ApplyWorkflowExecutionLogConversationIdNullableDirectAsync(
        NoneadCSSDbContext context,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var preCheck = await CheckWorkflowExecutionLogConversationIdNullableAsync(context, logger, cancellationToken);
        if (preCheck.Checked && preCheck.Nullable == true)
        {
            return new DirectPatchResult(Applied: false, Skipped: true, Reason: "ConversationId 已为 NULLABLE，无需补丁");
        }

        var database = context.Database;
        var strategy = database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var conn = database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }

            if (IsSqliteProvider(database))
            {
                return await ApplySqliteDirectPatchAsync(context, logger, cancellationToken);
            }

            if (IsSqlServerProvider(database))
            {
                return await ApplySqlServerDirectPatchAsync(context, logger, cancellationToken);
            }

            return new DirectPatchResult(false, false, $"当前 Provider {database.ProviderName} 未提供直接补丁实现，建议走 EF MigrateAsync。");
        });
    }

    private static async Task<DirectPatchResult> ApplySqliteDirectPatchAsync(
        NoneadCSSDbContext context,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        logger?.LogInformation("SQLite 直接补丁：执行 WorkflowExecutionLogs 4 步重建，将 ConversationId 改为 NULL");

        var conn = context.Database.GetDbConnection();
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;

            // 1) 临时表结构（完全按空库生成的脚本对齐）
            cmd.CommandText = @"
CREATE TABLE ""ef_temp_WorkflowExecutionLogs"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_WorkflowExecutionLogs"" PRIMARY KEY AUTOINCREMENT,
    ""ActionsExecuted"" INTEGER NOT NULL,
    ""ConversationId"" INTEGER NULL,
    ""ErrorMessage"" TEXT NULL,
    ""ExecutedAt"" TEXT NOT NULL,
    ""Status"" TEXT NOT NULL,
    ""WorkflowId"" INTEGER NOT NULL,
    CONSTRAINT ""FK_WorkflowExecutionLogs_Conversations_ConversationId"" FOREIGN KEY (""ConversationId"") REFERENCES ""Conversations"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_WorkflowExecutionLogs_Workflows_WorkflowId"" FOREIGN KEY (""WorkflowId"") REFERENCES ""Workflows"" (""Id"") ON DELETE CASCADE
);";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = @"
INSERT INTO ""ef_temp_WorkflowExecutionLogs"" (""Id"", ""ActionsExecuted"", ""ConversationId"", ""ErrorMessage"", ""ExecutedAt"", ""Status"", ""WorkflowId"")
SELECT ""Id"", ""ActionsExecuted"", ""ConversationId"", ""ErrorMessage"", ""ExecutedAt"", ""Status"", ""WorkflowId""
FROM ""WorkflowExecutionLogs"";";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // SQLite 的 DROP TABLE 不能在事务 + FK 开启时安全生效；先关闭 FK
            cmd.CommandText = "PRAGMA foreign_keys = 0;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "DROP TABLE \"WorkflowExecutionLogs\";";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "ALTER TABLE \"ef_temp_WorkflowExecutionLogs\" RENAME TO \"WorkflowExecutionLogs\";";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "PRAGMA foreign_keys = 1;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "CREATE INDEX \"IX_WorkflowExecutionLogs_ConversationId\" ON \"WorkflowExecutionLogs\" (\"ConversationId\");";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            cmd.CommandText = "CREATE INDEX \"IX_WorkflowExecutionLogs_ExecutedAt\" ON \"WorkflowExecutionLogs\" (\"ExecutedAt\");";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            cmd.CommandText = "CREATE INDEX \"IX_WorkflowExecutionLogs_WorkflowId\" ON \"WorkflowExecutionLogs\" (\"WorkflowId\");";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        logger?.LogInformation("SQLite 直接补丁：重建表完成，ConversationId 已为 NULL");

        return new DirectPatchResult(Applied: true, Skipped: false, Reason: "Sqlite 4-step rebuild");
    }

    private static async Task<DirectPatchResult> ApplySqlServerDirectPatchAsync(
        NoneadCSSDbContext context,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        logger?.LogInformation("SQL Server 直接补丁：ALTER WorkflowExecutionLogs.ConversationId → NULL");

        var conn = context.Database.GetDbConnection();
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;

            // 先动态删 default constraint
            cmd.CommandText = @"
DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WorkflowExecutionLogs]') AND [c].[name] = N'ConversationId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [WorkflowExecutionLogs] DROP CONSTRAINT ' + @var + ';');";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "ALTER TABLE [WorkflowExecutionLogs] ALTER COLUMN [ConversationId] INTEGER NULL;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return new DirectPatchResult(Applied: true, Skipped: false, Reason: "SqlServer ALTER COLUMN NULL");
    }
}

/// <summary>
/// 迁移自校验结果。
/// Nullable = true  → ConversationId 列已可空（补丁目标达成）
/// Nullable = false → 列仍是 NOT NULL 或不存在（若 Checked=true 则表示补丁未生效）
/// Nullable = null  → 当前 Provider 未实现校验，结果不具判别意义
/// </summary>
public sealed record MigrationCheckResult(
    bool? Nullable,
    bool Checked,
    string Reason);

/// <summary>直接补丁结果。</summary>
public sealed record DirectPatchResult(
    bool Applied,
    bool Skipped,
    string Reason);
