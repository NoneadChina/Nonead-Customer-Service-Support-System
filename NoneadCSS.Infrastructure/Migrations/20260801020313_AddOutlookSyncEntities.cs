using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutlookSyncEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutlookEventMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CalendarEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    OutlookEventId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "local"),
                    OutlookLastModified = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LocalLastModified = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutlookEventMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutlookEventMappings_CalendarEvents_CalendarEventId",
                        column: x => x.CalendarEventId,
                        principalTable: "CalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutlookEventMappings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutlookSyncSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    OutlookEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OutlookUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AccessToken = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RefreshToken = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncDirection = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "both"),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutlookSyncSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutlookSyncSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutlookEventMappings_CalendarEventId_UserId",
                table: "OutlookEventMappings",
                columns: new[] { "CalendarEventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutlookEventMappings_OutlookEventId",
                table: "OutlookEventMappings",
                column: "OutlookEventId");

            migrationBuilder.CreateIndex(
                name: "IX_OutlookEventMappings_Source",
                table: "OutlookEventMappings",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_OutlookEventMappings_UserId",
                table: "OutlookEventMappings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutlookSyncSettings_LastSyncAt",
                table: "OutlookSyncSettings",
                column: "LastSyncAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutlookSyncSettings_SyncEnabled",
                table: "OutlookSyncSettings",
                column: "SyncEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_OutlookSyncSettings_UserId",
                table: "OutlookSyncSettings",
                column: "UserId",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutlookEventMappings");

            migrationBuilder.DropTable(
                name: "OutlookSyncSettings");
        }
    }
}
