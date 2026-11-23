using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 补充添加 Mailboxes.Version 列（int, 并发令牌, 自动生成值）。
    ///
    /// 历史根因：
    ///   NoneadCSSDbContext 中通过 Fluent API 配置了 Mailbox.Version 为 IsRowVersion()，
    ///   ModelSnapshot 已包含该列，但从未生成对应的迁移 SQL，
    ///   导致从空库迁移时 schema 缺失该列。
    ///
    /// 部署须知（生产环境）：
    ///   生产环境已通过手动 ALTER TABLE 添加了该列。
    ///   部署前必须在 __EFMigrationsHistory 中手动插入本迁移记录，
    ///   避免 EF Core 重复执行 AddColumn 导致 "duplicate column name: Version" 错误。
    ///
    ///   SQL:
    ///     INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    ///     VALUES ('20260814090000_AddMailboxVersion', '10.0.9');
    ///
    /// 新空库：
    ///   通过 dotnet ef database update 正常应用本迁移，无需手动干预。
    /// </summary>
    public partial class AddMailboxVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Mailboxes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite 旧版本不支持 DROP COLUMN，需通过表重建。
            // 如需彻底回滚，请手动执行 SQLite 表重建。
            // 这里不做破坏性操作以保护数据安全。
        }
    }
}
