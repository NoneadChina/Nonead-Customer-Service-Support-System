using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaPolicyBusinessHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 说明：本迁移最初自动生成时假设提醒尝试表名为单数 CalendarReminderAttempt，
            // 但 20260814171217 创建的、生产库与模型快照实际使用的都是复数 CalendarReminderAttempts。
            // 因此原有 DropForeignKey/DropPrimaryKey/DropIndex/RenameTable 操作在任何数据库上都会失败，
            // 现已移除。表、主键、外键及 IX_CalRem_EventId_Channel 索引早由 20260814171217 创建，无需重复处理。
            //
            // SlaPolicies 业务小时列（WorkDays/WorkEndHour/WorkStartHour）在全新库与生产库均缺失，直接补列。
            migrationBuilder.AddColumn<string>(
                name: "WorkDays",
                table: "SlaPolicies",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WorkEndHour",
                table: "SlaPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkStartHour",
                table: "SlaPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // OutlookSyncSettings 的 ClientId/ClientSecret/RedirectUri/Scopes/TenantId 列
            // 由运行时补丁 T000099 在启动时幂等补齐（旧库可能已存在，不可在此重复 ADD COLUMN）。

            // 以下索引使用 IF NOT EXISTS 幂等创建，避免因历史库已存在同名索引而中断迁移。
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_TimeEntries_UserId_EndTime""
ON ""TimeEntries"" (""UserId"", ""EndTime"")
WHERE ""EndTime"" IS NULL AND ""DeletedAt"" IS NULL;");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_CalendarReminderAttempts_CreatedAt""
ON ""CalendarReminderAttempts"" (""CreatedAt"");");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ""IX_CalendarReminderAttempts_Status_CreatedAt""
ON ""CalendarReminderAttempts"" (""Status"", ""CreatedAt"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_UserId_EndTime",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_CalendarReminderAttempts_CreatedAt",
                table: "CalendarReminderAttempts");

            migrationBuilder.DropIndex(
                name: "IX_CalendarReminderAttempts_Status_CreatedAt",
                table: "CalendarReminderAttempts");

            migrationBuilder.DropColumn(
                name: "WorkDays",
                table: "SlaPolicies");

            migrationBuilder.DropColumn(
                name: "WorkEndHour",
                table: "SlaPolicies");

            migrationBuilder.DropColumn(
                name: "WorkStartHour",
                table: "SlaPolicies");
        }
    }
}
