using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NoneadCSS.Infrastructure.Migrations;

/// <summary>
/// 迁移里「删除索引 / 外键 / 表」的幂等封装。
/// 背景（2026-11-19）：EF 生成的 DropIndex 会无条件执行 <c>DROP INDEX</c>，
/// 而本仓库的迁移链在演进中出现过「同一个索引被先后两个迁移删除」的情况
/// （<c>20260726115230_AddPasswordResetTokenIndexes</c> 删掉 <c>IX_Conversations_CreatedAt</c>
/// 后没有再建，<c>20260728110945_AddUserNotificationSettings</c> 又按旧快照再删一次）。
/// 在既有生产库上看不出问题（那条迁移早已记录为已应用、不会重跑），
/// 但**空库从头迁移**时第二次的删除会抛 <c>SQLite Error 1: 'no such index'</c>，
/// 整条链中断，导致「克隆即运行」失败（现象：API 起不来 → 登录页「获取验证码失败」）。
/// 这里把破坏性 DDL 改成存在才删，SQLite 与 SQL Server 分别用各自合法的语法。
/// 注意：本封装只用于「本就该幂等」的删除，不改变任何迁移的语义结果。
/// </summary>
public static class MigrationIdempotency
{
    private static bool IsSqlite(MigrationBuilder migrationBuilder)
        => migrationBuilder.ActiveProvider?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>仅当索引存在时删除（SQLite: DROP INDEX IF EXISTS；SQL Server: 查 sys.indexes）。</summary>
    public static void DropIndexIfExists(MigrationBuilder migrationBuilder, string table, string index)
    {
        if (IsSqlite(migrationBuilder))
        {
            migrationBuilder.Sql($@"DROP INDEX IF EXISTS ""{index}"";");
            return;
        }

        migrationBuilder.Sql(
            $@"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{index}' AND object_id = OBJECT_ID(N'[{table}]'))
    BEGIN
        DROP INDEX [{index}] ON [{table}];
    END");
    }

    /// <summary>仅当外键存在时删除。</summary>
    public static void DropForeignKeyIfExists(MigrationBuilder migrationBuilder, string table, string foreignKey)
    {
        if (IsSqlite(migrationBuilder))
        {
            // SQLite 无法直接 DROP CONSTRAINT；EF 在 SQLite 上的 AlterTable 重建路径已处理外键，
            // 这里等价于「不存在就什么都不做」。
            migrationBuilder.Sql(
                $@"SELECT CASE WHEN EXISTS (
                       SELECT 1 FROM pragma_foreign_key_list('{table}') WHERE id IN (
                           SELECT id FROM pragma_foreign_key_list('{table}') WHERE constraint_name = '{foreignKey}'
                       )
                   ) THEN 1 ELSE 1 END;");
            return;
        }

        migrationBuilder.Sql(
            $@"IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = '{foreignKey}')
    BEGIN
        ALTER TABLE [{table}] DROP CONSTRAINT [{foreignKey}];
    END");
    }

    /// <summary>仅当表存在时删除。</summary>
    public static void DropTableIfExists(MigrationBuilder migrationBuilder, string name)
    {
        if (IsSqlite(migrationBuilder))
        {
            migrationBuilder.Sql($@"DROP TABLE IF EXISTS ""{name}"";");
            return;
        }

        migrationBuilder.Sql($@"IF OBJECT_ID(N'[{name}]', N'U') IS NOT NULL DROP TABLE [{name}];");
    }
}
