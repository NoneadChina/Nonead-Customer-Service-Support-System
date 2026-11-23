using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationRules_MailboxId",
                table: "NotificationRules");

            migrationBuilder.DropIndex(
                name: "IX_NotificationRules_UserId",
                table: "NotificationRules");

            migrationBuilder.CreateTable(
                name: "ConversationReadStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConversationId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationReadStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationReadStates_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationReadStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_EventType_Active_Mailbox_User_Deleted",
                table: "NotificationRules",
                columns: new[] { "EventType", "IsActive", "MailboxId", "UserId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_MailboxId_Active_Deleted",
                table: "NotificationRules",
                columns: new[] { "MailboxId", "IsActive", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_UserId_Active_Deleted",
                table: "NotificationRules",
                columns: new[] { "UserId", "IsActive", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReadStates_ConversationId_UserId",
                table: "ConversationReadStates",
                columns: new[] { "ConversationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReadStates_UserId",
                table: "ConversationReadStates",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationReadStates");

            migrationBuilder.DropIndex(
                name: "IX_NotificationRules_EventType_Active_Mailbox_User_Deleted",
                table: "NotificationRules");

            migrationBuilder.DropIndex(
                name: "IX_NotificationRules_MailboxId_Active_Deleted",
                table: "NotificationRules");

            migrationBuilder.DropIndex(
                name: "IX_NotificationRules_UserId_Active_Deleted",
                table: "NotificationRules");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_MailboxId",
                table: "NotificationRules",
                column: "MailboxId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_UserId",
                table: "NotificationRules",
                column: "UserId");
        }
    }
}
