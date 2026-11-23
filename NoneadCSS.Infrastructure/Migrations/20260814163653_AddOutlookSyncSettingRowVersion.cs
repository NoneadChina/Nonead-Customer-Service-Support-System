using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutlookSyncSettingRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutlookSyncLog_Status_CreatedAt",
                table: "OutlookSyncLogs");

            migrationBuilder.DropIndex(
                name: "IX_OutlookSyncLog_UserId_CreatedAt",
                table: "OutlookSyncLogs");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "OutlookSyncSettings",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_OutlookSyncLog_Status_CreatedAt",
                table: "OutlookSyncLogs",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_OutlookSyncLog_UserId_CreatedAt",
                table: "OutlookSyncLogs",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutlookSyncLog_Status_CreatedAt",
                table: "OutlookSyncLogs");

            migrationBuilder.DropIndex(
                name: "IX_OutlookSyncLog_UserId_CreatedAt",
                table: "OutlookSyncLogs");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "OutlookSyncSettings");

            migrationBuilder.CreateIndex(
                name: "IX_OutlookSyncLog_Status_CreatedAt",
                table: "OutlookSyncLogs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutlookSyncLog_UserId_CreatedAt",
                table: "OutlookSyncLogs",
                columns: new[] { "UserId", "CreatedAt" });
        }
    }
}
