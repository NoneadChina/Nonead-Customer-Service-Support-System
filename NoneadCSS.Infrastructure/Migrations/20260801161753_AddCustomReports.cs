using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReportConfig = table.Column<string>(type: "TEXT", nullable: false),
                    IsDashboardPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    DashboardSortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomReports_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomReports_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomReportSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    Recurrence = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    WeekDay = table.Column<int>(type: "INTEGER", nullable: true),
                    SendTime = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Recipients = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AttachmentFormat = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomReportSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomReportSchedules_CustomReports_CustomReportId",
                        column: x => x.CustomReportId,
                        principalTable: "CustomReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomReportSchedules_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomReports_IsDashboardPinned_DashboardSortOrder",
                table: "CustomReports",
                columns: new[] { "IsDashboardPinned", "DashboardSortOrder" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomReports_Name_OwnerId",
                table: "CustomReports",
                columns: new[] { "Name", "OwnerId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomReports_OwnerId",
                table: "CustomReports",
                column: "OwnerId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomReports_TeamId",
                table: "CustomReports",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomReportSchedules_CustomReportId",
                table: "CustomReportSchedules",
                column: "CustomReportId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomReportSchedules_IsActive_NextRunAt",
                table: "CustomReportSchedules",
                columns: new[] { "IsActive", "NextRunAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomReportSchedules_OwnerId",
                table: "CustomReportSchedules",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomReportSchedules");

            migrationBuilder.DropTable(
                name: "CustomReports");
        }
    }
}
