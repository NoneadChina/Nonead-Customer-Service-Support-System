using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImMessageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImMessageLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConversationId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MessageText = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 4),
                    NextRetryAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ExternalMessageId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImMessageLogs", x => x.Id);
                    table.CheckConstraint("CK_ImMessageLog_Status", "[Status] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_ImMessageLogs_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImMessageLogs_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImMessageLogs_ConversationId",
                table: "ImMessageLogs",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ImMessageLogs_CreatedAt",
                table: "ImMessageLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImMessageLogs_CustomerId",
                table: "ImMessageLogs",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ImMessageLogs_NextRetryAt",
                table: "ImMessageLogs",
                column: "NextRetryAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImMessageLogs_SourceType",
                table: "ImMessageLogs",
                column: "SourceType");

            migrationBuilder.CreateIndex(
                name: "IX_ImMessageLogs_Status",
                table: "ImMessageLogs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImMessageLogs_Status_NextRetryAt",
                table: "ImMessageLogs",
                columns: new[] { "Status", "NextRetryAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImMessageLogs");
        }
    }
}
