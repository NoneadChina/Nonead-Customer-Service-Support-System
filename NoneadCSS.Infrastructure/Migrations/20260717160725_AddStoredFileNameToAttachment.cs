using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredFileNameToAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoredFileName",
                table: "Attachments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoredFileName",
                table: "Attachments");
        }
    }
}
