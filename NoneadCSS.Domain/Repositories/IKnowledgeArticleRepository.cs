using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IKnowledgeArticleRepository
{
    Task<KnowledgeArticle?> GetByIdAsync(int id);
    Task<IReadOnlyList<KnowledgeArticle>> GetListAsync(
        int? categoryId = null,
        string? status = null,
        string? keyword = null,
        int? authorId = null,
        bool? isPinned = null,
        int page = 1,
        int pageSize = 20,
        int[]? accessibleMailboxIds = null);
    Task<int> CountAsync(
        int? categoryId = null,
        string? status = null,
        string? keyword = null,
        int? authorId = null,
        bool? isPinned = null,
        int[]? accessibleMailboxIds = null);
    Task<IReadOnlyList<KnowledgeArticle>> SearchAsync(string keyword, int page = 1, int pageSize = 20, int[]? accessibleMailboxIds = null);
    Task<int> SearchCountAsync(string keyword, int[]? accessibleMailboxIds = null);

    /// <summary>
    /// FTS5 全文搜索（优先使用 fts_knowledge_articles 虚拟表 + bm25 排序 + snippet 片段）；
    /// 若 FTS 未启用或查询语法异常，自动降级为 LIKE 搜索（兼容历史实现）。
    /// 返回文章 + 高亮片段 Snippet；Snippet 为 null 表示该条未走 FTS。
    /// </summary>
    Task<IReadOnlyList<(KnowledgeArticle Article, string? Snippet, float? Rank)>> FullTextSearchAsync(
        string keyword,
        int page = 1,
        int pageSize = 20,
        int[]? accessibleMailboxIds = null,
        bool includeDraft = false);
    Task<int> SumViewCountAsync(string? status = null);
    Task<int> SumLikeCountAsync(string? status = null);
    Task<IReadOnlyList<KnowledgeArticle>> GetTopByViewCountAsync(string status, int count, int[]? accessibleMailboxIds = null);
    Task AddAsync(KnowledgeArticle article);
    void Update(KnowledgeArticle article);
    void Remove(KnowledgeArticle article);
    Task IncrementViewCountAsync(int id);
    Task IncrementLikeCountAsync(int id);

    /// <summary>
    /// KB-002: 原子化点赞去重。依赖 KnowledgeArticleLikes 表 PRIMARY KEY (ArticleId, UserFingerprint) 唯一约束。
    /// 已点赞时 INSERT OR IGNORE 返回 0，未点赞时插入成功返回 1。
    /// </summary>
    Task<int> TryInsertLikeAsync(int articleId, string userFingerprint);

    /// <summary>
    /// T000101: 取消点赞（删除 KnowledgeArticleLikes 中该用户对该文章的记录）。
    /// 返回删除的行数：1=成功取消，0=记录不存在（幂等）。
    /// </summary>
    Task<int> TryRemoveLikeAsync(int articleId, string userFingerprint);

    /// <summary>
    /// T000101: 点赞数递减（取消点赞用），下限保护为 0。
    /// </summary>
    Task DecrementLikeCountAsync(int id);

    /// <summary>
    /// T000101: 批量查询当前用户已点赞的文章 ID 集合（用于填充 DTO.IsLiked）。
    /// </summary>
    Task<HashSet<int>> GetLikedArticleIdsAsync(string userFingerprint, IEnumerable<int> articleIds);

    /// <summary>
    /// KB-010: 分类重命名后同步 FTS5 索引中的 category_name 字段。
    /// FTS5 触发器只在 KnowledgeArticles 表上创建，分类名变更不会自动同步，
    /// 需手动调用此方法重新生成该分类下所有文章的 FTS 记录。
    /// </summary>
    Task SyncFtsByCategoryIdAsync(int categoryId);

    // 垃圾箱
    Task<KnowledgeArticle?> GetDeletedByIdAsync(int id);
    Task<(IReadOnlyList<KnowledgeArticle> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task RestoreAsync(int id);
    Task RestoreWithCategoryResetAsync(int id);
    Task HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}
