using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarReminderFieldsAndAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReminderConfirmation",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReminderConfirmedAt",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReminderSentAt",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CalendarReminderAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptChannel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AttemptNo = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorDetail = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarReminderAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarReminderAttempts_CalendarEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "CalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalRem_EventId_Channel",
                table: "CalendarReminderAttempts",
                columns: new[] { "EventId", "AttemptChannel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarReminderAttempts");

            migrationBuilder.DropColumn(
                name: "ReminderConfirmation",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "ReminderConfirmedAt",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "CalendarEvents");
        }
    }
}
