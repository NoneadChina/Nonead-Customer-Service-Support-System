using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRuleRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "NotificationRules",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "NotificationRules");
        }
    }
}
