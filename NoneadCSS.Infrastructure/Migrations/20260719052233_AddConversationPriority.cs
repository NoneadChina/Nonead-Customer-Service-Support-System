using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Conversations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // 将现有会话的优先级更新为 Normal(1)
            // （列已带 defaultValue:1，新建行会自动取 Normal；此处显式更新确保旧数据一致）
            migrationBuilder.Sql("UPDATE Conversations SET Priority = 1 WHERE Priority = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Conversations");
        }
    }
}
