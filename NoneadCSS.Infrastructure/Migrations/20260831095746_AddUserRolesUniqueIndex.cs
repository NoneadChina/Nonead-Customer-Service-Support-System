using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRolesUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S2: (UserId, RoleId, MailboxId) 唯一索引，防止并发重复分配角色。
            // 手工改写为 COALESCE 表达式索引：普通唯一索引对 NULL 值不去重，
            // 使用 COALESCE(MailboxId, -1) 后，MailboxId 为 NULL 的全局角色同样受唯一约束。
            if (migrationBuilder.ActiveProvider?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
            {
                // 先清理历史重复数据（保留每个 (UserId, RoleId, MailboxKey) 最旧的一条），避免建索引失败
                migrationBuilder.Sql("""
                    DELETE FROM "UserRoles"
                    WHERE "Id" NOT IN (
                        SELECT MIN("Id")
                        FROM "UserRoles"
                        GROUP BY "UserId", "RoleId", COALESCE("MailboxId", -1)
                    );
                    """);
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX "IX_UserRoles_UserId_RoleId_MailboxId"
                    ON "UserRoles" ("UserId", "RoleId", COALESCE("MailboxId", -1));
                    """);
            }
            else
            {
                migrationBuilder.Sql("""
                    DELETE FROM [UserRoles]
                    WHERE [Id] NOT IN (
                        SELECT MIN([Id])
                        FROM [UserRoles]
                        GROUP BY [UserId], [RoleId], COALESCE([MailboxId], -1)
                    );
                    """);
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX [IX_UserRoles_UserId_RoleId_MailboxId]
                    ON [UserRoles] ([UserId], [RoleId], COALESCE([MailboxId], -1));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRoles_UserId_RoleId_MailboxId",
                table: "UserRoles");
        }
    }
}
