using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixSendLogStatusCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 数据清理必须在表重建之前执行。
            //
            // 背景（2026-09-21 生产首次尝试失败实录）：
            // SQLite 无法原地修改 CHECK 约束，EF 会重建 SendLogs：
            //   CREATE TABLE ef_temp_SendLogs(... FKs ...) → INSERT ... SELECT ... FROM SendLogs
            //   → PRAGMA foreign_keys=0 → DROP/换名 → 重建索引
            // 注意 EF 把 `PRAGMA foreign_keys = 0` 放在**数据拷贝之后**，所以拷贝阶段 FK 校验是开启的
            // （连接默认 PRAGMA foreign_keys = 1）。
            // 生产库 SendLogs 中存在指向已删除实体的孤立引用（实测 ThreadId 6 行），
            // 导致 `INSERT INTO ef_temp_SendLogs ...` 报 SQLite Error 19: FOREIGN KEY constraint failed，
            // 迁移在 MigrateAsync 中抛出、进程退出，应用无法启动。
            //
            // 处理方式与代码库既有策略一致：实体被硬删除时把 SendLogs 的关联列置空、
            // 保留历史发送记录（参见 CustomerRepository 中「CustomerId 设为 null」的处理）。
            migrationBuilder.Sql(
                """
                UPDATE "SendLogs" SET "ThreadId" = NULL
                WHERE "ThreadId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "Threads" t WHERE t."Id" = "SendLogs"."ThreadId");
                """);
            migrationBuilder.Sql(
                """
                UPDATE "SendLogs" SET "CustomerId" = NULL
                WHERE "CustomerId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "Customers" c WHERE c."Id" = "SendLogs"."CustomerId");
                """);
            migrationBuilder.Sql(
                """
                UPDATE "SendLogs" SET "UserId" = NULL
                WHERE "UserId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."Id" = "SendLogs"."UserId");
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SendLog_Status",
                table: "SendLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SendLog_Status",
                table: "SendLogs",
                sql: "[Status] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SendLog_Status",
                table: "SendLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SendLog_Status",
                table: "SendLogs",
                sql: "[Status] IN (1, 2, 3, 4, 5)");
        }
    }
}
