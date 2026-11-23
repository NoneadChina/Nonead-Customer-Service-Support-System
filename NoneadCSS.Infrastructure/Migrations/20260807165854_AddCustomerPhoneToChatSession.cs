using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPhoneToChatSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HideWhenChannelDisabled",
                table: "CustomFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SourceTypesJson",
                table: "CustomFields",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhone",
                table: "ChatSessions",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HideWhenChannelDisabled",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "SourceTypesJson",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "CustomerPhone",
                table: "ChatSessions");
        }
    }
}
