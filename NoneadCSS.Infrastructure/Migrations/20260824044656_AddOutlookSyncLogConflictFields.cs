using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutlookSyncLogConflictFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConflictCount",
                table: "OutlookSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConflictDetailsJson",
                table: "OutlookSyncLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarEvents_Users_CreatedBy",
                table: "CalendarEvents",
                column: "CreatedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarEvents_Users_CreatedBy",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "ConflictCount",
                table: "OutlookSyncLogs");

            migrationBuilder.DropColumn(
                name: "ConflictDetailsJson",
                table: "OutlookSyncLogs");
        }
    }
}
