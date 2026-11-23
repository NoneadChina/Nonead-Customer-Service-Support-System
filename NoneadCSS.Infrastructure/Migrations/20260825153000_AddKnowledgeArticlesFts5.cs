using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeArticlesFts5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) 创建 FTS5 虚拟表：标题、摘要、内容、标签、分类名统一索引
            //    使用 unicode61 分词 + 去变音符号，冗余 content_rowid/category_id/status 便于过滤
            var tokenizeClause = "tokenize = 'unicode61 remove_diacritics 2 tokenchars \"\"_-\"\"'";
            migrationBuilder.Sql($@"
CREATE VIRTUAL TABLE IF NOT EXISTS fts_knowledge_articles USING fts5(
    title,
    summary,
    content,
    tags,
    category_name,
    content_rowid UNINDEXED,
    category_id UNINDEXED,
    status UNINDEXED,
    updated_at UNINDEXED,
    is_pinned UNINDEXED,
    {tokenizeClause}
);
");
            // 2) 回填已存在的文章数据（含分类名称关联）
            migrationBuilder.Sql(@"
INSERT OR REPLACE INTO fts_knowledge_articles (
    rowid, title, summary, content, tags, category_name,
    content_rowid, category_id, status, updated_at, is_pinned
)
SELECT
    a.Id,
    COALESCE(a.Title, ''),
    COALESCE(a.Summary, ''),
    COALESCE(a.Content, ''),
    COALESCE(a.Tags, ''),
    COALESCE(c.Name, ''),
    a.Id,
    a.CategoryId,
    a.Status,
    a.UpdatedAt,
    CASE WHEN a.IsPinned = 1 THEN 1 ELSE 0 END
FROM KnowledgeArticles a
LEFT JOIN KnowledgeCategories c ON a.CategoryId = c.Id AND c.DeletedAt IS NULL
WHERE a.DeletedAt IS NULL;
");
            // 3) 创建触发器：文章插入后同步至 FTS
            migrationBuilder.Sql(@"
CREATE TRIGGER IF NOT EXISTS trg_knowledge_articles_after_insert
AFTER INSERT ON KnowledgeArticles
FOR EACH ROW WHEN NEW.DeletedAt IS NULL
BEGIN
    INSERT OR REPLACE INTO fts_knowledge_articles (
        rowid, title, summary, content, tags, category_name,
        content_rowid, category_id, status, updated_at, is_pinned
    )
    SELECT
        NEW.Id,
        COALESCE(NEW.Title, ''),
        COALESCE(NEW.Summary, ''),
        COALESCE(NEW.Content, ''),
        COALESCE(NEW.Tags, ''),
        COALESCE(c.Name, ''),
        NEW.Id,
        NEW.CategoryId,
        NEW.Status,
        NEW.UpdatedAt,
        CASE WHEN NEW.IsPinned = 1 THEN 1 ELSE 0 END
    FROM (SELECT 1) LEFT JOIN KnowledgeCategories c ON NEW.CategoryId = c.Id AND c.DeletedAt IS NULL;
END;
");
            // 4) 更新触发器
            migrationBuilder.Sql(@"
CREATE TRIGGER IF NOT EXISTS trg_knowledge_articles_after_update
AFTER UPDATE ON KnowledgeArticles
FOR EACH ROW
BEGIN
    -- 软删除：从 FTS 删除
    IF NEW.DeletedAt IS NOT NULL THEN
        DELETE FROM fts_knowledge_articles WHERE rowid = OLD.Id;
    ELSE
        INSERT OR REPLACE INTO fts_knowledge_articles (
            rowid, title, summary, content, tags, category_name,
            content_rowid, category_id, status, updated_at, is_pinned
        )
        SELECT
            NEW.Id,
            COALESCE(NEW.Title, ''),
            COALESCE(NEW.Summary, ''),
            COALESCE(NEW.Content, ''),
            COALESCE(NEW.Tags, ''),
            COALESCE(c.Name, ''),
            NEW.Id,
            NEW.CategoryId,
            NEW.Status,
            NEW.UpdatedAt,
            CASE WHEN NEW.IsPinned = 1 THEN 1 ELSE 0 END
        FROM (SELECT 1) LEFT JOIN KnowledgeCategories c ON NEW.CategoryId = c.Id AND c.DeletedAt IS NULL;
    END IF;
END;
");
            // 5) 删除触发器
            migrationBuilder.Sql(@"
CREATE TRIGGER IF NOT EXISTS trg_knowledge_articles_after_delete
AFTER DELETE ON KnowledgeArticles
FOR EACH ROW
BEGIN
    DELETE FROM fts_knowledge_articles WHERE rowid = OLD.Id;
END;
");
            // 6) 分类名变更时同步更新 FTS
            migrationBuilder.Sql(@"
CREATE TRIGGER IF NOT EXISTS trg_knowledge_categories_after_update_sync_fts
AFTER UPDATE ON KnowledgeCategories
FOR EACH ROW WHEN OLD.Name <> NEW.Name AND NEW.DeletedAt IS NULL
BEGIN
    UPDATE fts_knowledge_articles
    SET category_name = COALESCE(NEW.Name, '')
    WHERE category_id = NEW.Id;
END;
");
            migrationBuilder.Sql(@"
CREATE TRIGGER IF NOT EXISTS trg_knowledge_categories_after_delete_sync_fts
AFTER DELETE ON KnowledgeCategories
FOR EACH ROW
BEGIN
    UPDATE fts_knowledge_articles
    SET category_name = '', category_id = NULL
    WHERE category_id = OLD.Id;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_knowledge_categories_after_delete_sync_fts;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_knowledge_categories_after_update_sync_fts;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_knowledge_articles_after_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_knowledge_articles_after_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_knowledge_articles_after_insert;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS fts_knowledge_articles;");
        }
    }
}
