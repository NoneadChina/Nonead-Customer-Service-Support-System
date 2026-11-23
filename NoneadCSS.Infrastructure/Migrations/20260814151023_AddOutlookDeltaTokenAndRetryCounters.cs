using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutlookDeltaTokenAndRetryCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "OutlookSyncSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DeltaToken",
                table: "OutlookSyncSettings",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFullSyncAt",
                table: "OutlookSyncSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextRetryAt",
                table: "OutlookSyncSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutlookSyncSettings_NextRetryAt",
                table: "OutlookSyncSettings",
                column: "NextRetryAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutlookSyncSettings_NextRetryAt",
                table: "OutlookSyncSettings");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "OutlookSyncSettings");

            migrationBuilder.DropColumn(
                name: "DeltaToken",
                table: "OutlookSyncSettings");

            migrationBuilder.DropColumn(
                name: "LastFullSyncAt",
                table: "OutlookSyncSettings");

            migrationBuilder.DropColumn(
                name: "NextRetryAt",
                table: "OutlookSyncSettings");
        }
    }
}
