using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixUniqueIndexSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 使用幂等 SQL 防止索引不存在时迁移失败（SQLite 支持 DROP INDEX IF EXISTS）
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Stars_ConversationId\"");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Conversations_Number\"");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ApiKeys_Key\"");

            // 使用幂等 SQL 创建索引（SQLite 支持 CREATE INDEX IF NOT EXISTS）
            // 避免因索引已存在或数据重复导致迁移失败
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Threads_MessageId\" ON \"Threads\" (\"MessageId\")");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Stars_ConversationId_UserId\" ON \"Stars\" (\"ConversationId\", \"UserId\")");

            // 清理重复的 Conversations.Number 值（仅未删除记录），确保唯一索引可创建
            // 为重复记录分配新的唯一 Number 值（基于当前最大值 + 行号递增）
            migrationBuilder.Sql(@"
WITH duplicates AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) as rn,
           (SELECT COALESCE(MAX(Number), 0) FROM Conversations WHERE DeletedAt IS NULL) as max_num
    FROM Conversations
    WHERE DeletedAt IS NULL
    AND Id NOT IN (SELECT MIN(Id) FROM Conversations WHERE DeletedAt IS NULL GROUP BY Number HAVING COUNT(*) > 1)
    AND Number IN (SELECT Number FROM Conversations WHERE DeletedAt IS NULL GROUP BY Number HAVING COUNT(*) > 1)
)
UPDATE Conversations
SET Number = (SELECT max_num + rn FROM duplicates WHERE duplicates.Id = Conversations.Id)
WHERE Id IN (SELECT Id FROM duplicates)");

            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Conversations_Number\" ON \"Conversations\" (\"Number\") WHERE \"DeletedAt\" IS NULL");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_ApiKeys_Key\" ON \"ApiKeys\" (\"Key\") WHERE \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Threads_MessageId",
                table: "Threads");

            migrationBuilder.DropIndex(
                name: "IX_Stars_ConversationId_UserId",
                table: "Stars");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_Number",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_Key",
                table: "ApiKeys");

            migrationBuilder.CreateIndex(
                name: "IX_Stars_ConversationId",
                table: "Stars",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_Number",
                table: "Conversations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_Key",
                table: "ApiKeys",
                column: "Key",
                unique: true);
        }
    }
}
