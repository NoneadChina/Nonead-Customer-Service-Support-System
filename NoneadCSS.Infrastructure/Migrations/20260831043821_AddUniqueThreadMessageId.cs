using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueThreadMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Threads_MessageId",
                table: "Threads");

            // TD-06: 清理重复 MessageId（保留每个重复组中最早的一条，其余置空），
            // 确保唯一过滤索引可创建。空 MessageId（如手动创建的 Note）不受影响。
            migrationBuilder.Sql(@"
UPDATE ""Threads""
SET ""MessageId"" = ''
WHERE ""MessageId"" != ''
AND ""Id"" NOT IN (
    SELECT MIN(""Id"")
    FROM ""Threads""
    WHERE ""MessageId"" != ''
    GROUP BY ""MessageId""
)");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_MessageId",
                table: "Threads",
                column: "MessageId",
                unique: true,
                filter: "\"MessageId\" != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Threads_MessageId",
                table: "Threads");

            migrationBuilder.CreateIndex(
                name: "IX_Threads_MessageId",
                table: "Threads",
                column: "MessageId");
        }
    }
}
