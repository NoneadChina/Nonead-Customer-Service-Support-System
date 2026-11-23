using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// KB-002: 创建 KnowledgeArticleLikes 表，用于原子化点赞去重。
    /// 依赖 PRIMARY KEY (ArticleId, UserFingerprint) 唯一约束实现并发安全的点赞计数：
    ///   - INSERT OR IGNORE 在数据库层保证每用户每文章仅一次点赞
    ///   - 与应用层 cache check-then-set 相比，从根本上消除竞态条件
    /// 注意：KnowledgeArticles.LikeCount 列继续作为点赞数的权威来源（递增更新），
    ///       本表仅用于"是否已点赞"的去重判定，无需回填历史点赞记录。
    ///       历史点赞数已在 LikeCount 列中累积，新点赞会同时插入本表并递增 LikeCount。
    /// </summary>
    public partial class AddKnowledgeArticleLikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 创建点赞去重表：复合主键 (ArticleId, UserFingerprint) 即保证唯一性
            // 外键 ON DELETE CASCADE：文章物理删除时自动清理点赞记录，避免孤儿数据
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS KnowledgeArticleLikes (
    ArticleId INTEGER NOT NULL,
    UserFingerprint TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (ArticleId, UserFingerprint),
    FOREIGN KEY (ArticleId) REFERENCES KnowledgeArticles(Id) ON DELETE CASCADE
);
");

            // 反向索引：支持按 UserFingerprint 查询用户点赞过的文章列表（如个人中心-我的点赞）
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS IX_KnowledgeArticleLikes_UserFingerprint
ON KnowledgeArticleLikes(UserFingerprint);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS KnowledgeArticleLikes;");
        }
    }
}
