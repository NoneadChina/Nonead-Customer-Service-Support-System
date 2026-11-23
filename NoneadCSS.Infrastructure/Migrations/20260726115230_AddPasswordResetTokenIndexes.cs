using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetTokenIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_MacroExecutionLogs_Conversations_ConversationId",
                table: "MacroExecutionLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_SlaViolations_Conversations_ConversationId",
                table: "SlaViolations");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowExecutionLogs_Conversations_ConversationId",
                table: "WorkflowExecutionLogs");

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
                name: "IX_Conversations_Priority",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations");

            // NOTE: Workflows.IsSystem 由更早的迁移 20260725020724_AddIsSystemToWorkflow 负责添加；
            // 此处为历史重复 DDL，删除以避免 SQLite 增量迁移时报 duplicate column name: IsSystem。

            migrationBuilder.AddColumn<string>(
                name: "Avatar",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Threads",
                type: "INTEGER",
                rowVersion: true,
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SatisfactionRatings",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RecipientMailboxId",
                table: "NotificationRules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoleId",
                table: "NotificationRules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "NotificationRules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Mailboxes",
                type: "INTEGER",
                rowVersion: true,
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SearchVector",
                table: "Conversations",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Conversations",
                type: "INTEGER",
                rowVersion: true,
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRecurring",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReminderSent",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceCount",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceEndDate",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceInterval",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceOriginalStart",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceParentId",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceType",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderMinutes",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Link = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_User_Role",
                table: "Users",
                sql: "[Role] IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConversationThread_State",
                table: "Threads",
                sql: "[State] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SendLog_Status",
                table: "SendLogs",
                sql: "[Status] IN (1, 2, 3, 4, 5)");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_RecipientMailboxId",
                table: "NotificationRules",
                column: "RecipientMailboxId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_RoleId",
                table: "NotificationRules",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRules_TeamId",
                table: "NotificationRules",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_CustomerId",
                table: "Conversations",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_FolderId_Status",
                table: "Conversations",
                columns: new[] { "FolderId", "Status" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_Number",
                table: "Conversations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_Status_Priority_LastReplyAt",
                table: "Conversations",
                columns: new[] { "Status", "Priority", "LastReplyAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations",
                column: "UserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Conversation_Priority",
                table: "Conversations",
                sql: "[Priority] IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Conversation_State",
                table: "Conversations",
                sql: "[State] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Conversation_Status",
                table: "Conversations",
                sql: "[Status] IN (1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId",
                table: "UserNotifications",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MacroExecutionLogs_Conversations_ConversationId",
                table: "MacroExecutionLogs",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationRules_Mailboxes_RecipientMailboxId",
                table: "NotificationRules",
                column: "RecipientMailboxId",
                principalTable: "Mailboxes",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationRules_Roles_RoleId",
                table: "NotificationRules",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationRules_Teams_TeamId",
                table: "NotificationRules",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SlaViolations_Conversations_ConversationId",
                table: "SlaViolations",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowExecutionLogs_Conversations_ConversationId",
                table: "WorkflowExecutionLogs",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_MacroExecutionLogs_Conversations_ConversationId",
                table: "MacroExecutionLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationRules_Mailboxes_RecipientMailboxId",
                table: "NotificationRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationRules_Roles_RoleId",
                table: "NotificationRules");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationRules_Teams_TeamId",
                table: "NotificationRules");

            migrationBuilder.DropForeignKey(
                name: "FK_SlaViolations_Conversations_ConversationId",
                table: "SlaViolations");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowExecutionLogs_Conversations_ConversationId",
                table: "WorkflowExecutionLogs");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_User_Role",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ConversationThread_State",
                table: "Threads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SendLog_Status",
                table: "SendLogs");

            migrationBuilder.DropIndex(
                name: "IX_NotificationRules_RecipientMailboxId",
                table: "NotificationRules");

            migrationBuilder.DropIndex(
                name: "IX_NotificationRules_RoleId",
                table: "NotificationRules");

            migrationBuilder.DropIndex(
                name: "IX_NotificationRules_TeamId",
                table: "NotificationRules");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_CustomerId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_FolderId_Status",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_Number",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_Status_Priority_LastReplyAt",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Conversation_Priority",
                table: "Conversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Conversation_State",
                table: "Conversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Conversation_Status",
                table: "Conversations");

            // NOTE: Workflows.IsSystem 由迁移 20260725020724_AddIsSystemToWorkflow 负责；本迁移历史重复 DDL 已移除，不再由 Down 负责。

            migrationBuilder.DropColumn(
                name: "Avatar",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SatisfactionRatings");

            migrationBuilder.DropColumn(
                name: "RecipientMailboxId",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Mailboxes");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "IsRecurring",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "IsReminderSent",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RecurrenceCount",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RecurrenceEndDate",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RecurrenceInterval",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RecurrenceOriginalStart",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RecurrenceParentId",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RecurrenceType",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "ReminderMinutes",
                table: "CalendarEvents");

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
                name: "IX_Conversations_Priority",
                table: "Conversations",
                column: "Priority",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations",
                column: "UserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MacroExecutionLogs_Conversations_ConversationId",
                table: "MacroExecutionLogs",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SlaViolations_Conversations_ConversationId",
                table: "SlaViolations",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowExecutionLogs_Conversations_ConversationId",
                table: "WorkflowExecutionLogs",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
