using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CalendarEventExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttendeesJson",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedBy",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeetingUrl",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Progress",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceExclusionsJson",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelatedEventId",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubTasksJson",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttendeesJson",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "MeetingUrl",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "Progress",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RecurrenceExclusionsJson",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RelatedEventId",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "SubTasksJson",
                table: "CalendarEvents");
        }
    }
}
