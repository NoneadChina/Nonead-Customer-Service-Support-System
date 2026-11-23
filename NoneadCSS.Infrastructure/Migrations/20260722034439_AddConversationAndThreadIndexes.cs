using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationAndThreadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Followers_ConversationId",
                table: "Followers");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_CustomerId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_FolderId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_MailboxId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Webhooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "KnowledgeCategories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "KnowledgeArticles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Folders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Folders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Folders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CustomFields",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "ApiKeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Threads_CreatedAt",
                table: "Threads",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_Type",
                table: "Threads",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Followers_ConversationId_UserId",
                table: "Followers",
                columns: new[] { "ConversationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CreatedAt",
                table: "Conversations",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CustomerId",
                table: "Conversations",
                column: "CustomerId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_FolderId",
                table: "Conversations",
                column: "FolderId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_LastReplyAt",
                table: "Conversations",
                column: "LastReplyAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_MailboxId_Status",
                table: "Conversations",
                columns: new[] { "MailboxId", "Status" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_MailboxId_Status_LastReplyAt",
                table: "Conversations",
                columns: new[] { "MailboxId", "Status", "LastReplyAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_MailboxId_UserId",
                table: "Conversations",
                columns: new[] { "MailboxId", "UserId" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_Priority",
                table: "Conversations",
                column: "Priority",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations",
                column: "UserId",
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Threads_CreatedAt",
                table: "Threads");

            migrationBuilder.DropIndex(
                name: "IX_Threads_Type",
                table: "Threads");

            migrationBuilder.DropIndex(
                name: "IX_Followers_ConversationId_UserId",
                table: "Followers");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_CreatedAt",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_CustomerId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_FolderId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_LastReplyAt",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_MailboxId_Status",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_MailboxId_Status_LastReplyAt",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_MailboxId_UserId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_Priority",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "KnowledgeCategories");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "KnowledgeArticles");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ApiKeys");

            migrationBuilder.CreateIndex(
                name: "IX_Followers_ConversationId",
                table: "Followers",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CustomerId",
                table: "Conversations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_FolderId",
                table: "Conversations",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_MailboxId",
                table: "Conversations",
                column: "MailboxId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations",
                column: "UserId");
        }
    }
}
