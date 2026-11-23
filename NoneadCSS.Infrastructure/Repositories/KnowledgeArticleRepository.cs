using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Infrastructure.Repositories;

public class KnowledgeArticleRepository : IKnowledgeArticleRepository
{
    private readonly NoneadCSSDbContext _context;

    public KnowledgeArticleRepository(NoneadCSSDbContext context)
    {
        _context = context;
    }

    private IQueryable<KnowledgeArticle> ApplyIncludes(IQueryable<KnowledgeArticle> query)
    {
        return query.Include(a => a.Category).Include(a => a.Author);
    }

    public async Task<KnowledgeArticle?> GetByIdAsync(int id)
    {
        // 详情查询加载附件列表（列表查询走 BuildQuery，不加载附件，避免 N+1）
        return await ApplyIncludes(_context.KnowledgeArticles)
            .Include(a => a.Attachments.Where(att => att.DeletedAt == null))
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<IReadOnlyList<KnowledgeArticle>> GetListAsync(
        int? categoryId = null,
        string? status = null,
        string? keyword = null,
        int? authorId = null,
        bool? isPinned = null,
        int page = 1,
        int pageSize = 20,
        int[]? accessibleMailboxIds = null)
    {
        var query = BuildQuery(categoryId, status, keyword, authorId, isPinned, accessibleMailboxIds);
        // 置顶在前，然后按排序值，再按更新时间倒序
        query = query.OrderByDescending(a => a.IsPinned).ThenBy(a => a.SortOrder).ThenByDescending(a => a.UpdatedAt);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return items;
    }

    public async Task<int> CountAsync(
        int? categoryId = null,
        string? status = null,
        string? keyword = null,
        int? authorId = null,
        bool? isPinned = null,
        int[]? accessibleMailboxIds = null)
    {
        var query = BuildQuery(categoryId, status, keyword, authorId, isPinned, accessibleMailboxIds);
        return await query.CountAsync();
    }

    public async Task<IReadOnlyList<KnowledgeArticle>> SearchAsync(string keyword, int page = 1, int pageSize = 20, int[]? accessibleMailboxIds = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<KnowledgeArticle>();
        }
        var kw = keyword.Trim().ToLower();
        var query = ApplyIncludes(_context.KnowledgeArticles.AsNoTracking())
            .Where(a => a.DeletedAt == null && a.Status == "published")
            .Where(a => a.Title.ToLower().Contains(kw) || (a.Summary != null && a.Summary.ToLower().Contains(kw)) || a.Content.ToLower().Contains(kw) || (a.Tags != null && a.Tags.ToLower().Contains(kw)));

        // IDOR 防护：当指定可访问邮箱 ID 时，仅返回全局分类（Category.MailboxId 为 null）或属于授权邮箱的文章
        // accessibleMailboxIds = null 表示不限制（超管）；空数组表示仅返回全局分类文章
        if (accessibleMailboxIds != null)
        {
            var mailboxIdSet = accessibleMailboxIds;
            query = query.Where(a =>
                a.CategoryId == null
                || a.Category == null
                || a.Category.MailboxId == null
                || mailboxIdSet.Contains(a.Category.MailboxId.Value));
        }

        query = query.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.UpdatedAt);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return items;
    }

    public async Task<int> SearchCountAsync(string keyword, int[]? accessibleMailboxIds = null)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return 0;
        var kw = keyword.Trim().ToLower();
        var query = ApplyIncludes(_context.KnowledgeArticles.AsNoTracking())
            .Where(a => a.DeletedAt == null && a.Status == "published")
            .Where(a => a.Title.ToLower().Contains(kw) || (a.Summary != null && a.Summary.ToLower().Contains(kw)) || a.Content.ToLower().Contains(kw) || (a.Tags != null && a.Tags.ToLower().Contains(kw)));

        if (accessibleMailboxIds != null)
        {
            var mailboxIdSet = accessibleMailboxIds;
            query = query.Where(a =>
                a.CategoryId == null
                || a.Category == null
                || a.Category.MailboxId == null
                || mailboxIdSet.Contains(a.Category.MailboxId.Value));
        }

        return await query.CountAsync();
    }

    // ====== FTS5 全文搜索（优先，带 snippet 片段；失败降级 LIKE）======

    /// <summary>
    /// 把原始搜索词规范化为 FTS5 可匹配的查询表达式。
    /// 思路：按非字母数字/中日韩/下划线/横杠的边界拆词，单 token 默认前缀匹配（*），
    /// 多 token 用 AND 组合；对 FTS 特殊字符做转义，避免 MATCH 语法错误。
    /// 单字符查询返回 null 让调用方自动走 LIKE 降级，避免 FTS 误报。
    /// </summary>
    private static string? BuildFtsQuery(string rawKeyword)
    {
        if (string.IsNullOrWhiteSpace(rawKeyword)) return null;
        var cleaned = rawKeyword.Trim();
        if (cleaned.Length == 0) return null;

        // 去 FTS 保留字符
        var stripped = cleaned
            .Replace("\"", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal)
            .Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("+", "", StringComparison.Ordinal)
            .Replace("*", "", StringComparison.Ordinal)
            .Replace("(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal);
        if (stripped.Length == 0) return null;

        // 按空格/常见分隔符分词
        var tokens = stripped.Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        // 单 token 过短：走 LIKE
        if (tokens.Length == 1 && tokens[0].Length < 2) return null;

        // 过短 token 直接丢弃
        var validTokens = tokens.Where(t => t.Length >= 1).ToList();
        if (validTokens.Count == 0) return null;

        // 对每个 token 追加前缀通配符 *（中文单字也允许前缀匹配）
        var exprs = validTokens.Select(t => $"\"{t}\"*").ToList();
        return string.Join(" AND ", exprs);
    }

    /// <summary>
    /// FTS5 snippet 调用包装：若 FTS MATCH 失败不抛出，返回空让上层降级。
    /// </summary>
    public async Task<IReadOnlyList<(KnowledgeArticle Article, string? Snippet, float? Rank)>> FullTextSearchAsync(
        string keyword,
        int page = 1,
        int pageSize = 20,
        int[]? accessibleMailboxIds = null,
        bool includeDraft = false)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Array.Empty<(KnowledgeArticle, string?, float?)>();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var ftsQuery = BuildFtsQuery(keyword);
        List<(int RowId, string? Snippet, float? Rank)>? ftsRows = null;
        bool usedFts = false;

        if (!string.IsNullOrEmpty(ftsQuery))
        {
            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await _context.Database.OpenConnectionAsync();

                var skip = (page - 1) * pageSize;
                // snippet(fts, col_idx, start_mark, end_mark, ellipsis, tokens)
                // 字段顺序：0=title, 1=summary, 2=content, 3=tags, 4=category_name
                var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT
    rowid,
    snippet(fts_knowledge_articles, -1, '[[H]]', '[[/H]]', ' ... ', 16) AS snip,
    bm25(fts_knowledge_articles) AS rk
FROM fts_knowledge_articles fts
WHERE fts MATCH @q
  AND (@incDraft = 1 OR status = 'published')
ORDER BY
    is_pinned DESC,
    rk ASC,
    updated_at DESC
LIMIT @skip, @take;";
                var pQ = cmd.CreateParameter(); pQ.ParameterName = "@q"; pQ.Value = ftsQuery; cmd.Parameters.Add(pQ);
                var pInc = cmd.CreateParameter(); pInc.ParameterName = "@incDraft"; pInc.Value = includeDraft ? 1 : 0; cmd.Parameters.Add(pInc);
                var pSkip = cmd.CreateParameter(); pSkip.ParameterName = "@skip"; pSkip.Value = skip; cmd.Parameters.Add(pSkip);
                var pTake = cmd.CreateParameter(); pTake.ParameterName = "@take"; pTake.Value = pageSize; cmd.Parameters.Add(pTake);

                ftsRows = new List<(int, string?, float?)>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var rid = reader.GetInt32(0);
                    var snip = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var rank = reader.IsDBNull(2) ? null : (float?)(double)reader.GetFloat(2);
                    ftsRows.Add((rid, snip, rank));
                }
                usedFts = ftsRows.Count > 0;
            }
            catch
            {
                // FTS 语法错误或虚拟表未建好：降级 LIKE
                ftsRows = null;
                usedFts = false;
            }
        }

        List<KnowledgeArticle> articles;
        if (usedFts && ftsRows != null && ftsRows.Count > 0)
        {
            var ids = ftsRows.Select(r => r.RowId).Distinct().ToList();
            var dict = await ApplyIncludes(_context.KnowledgeArticles.AsNoTracking())
                .Where(a => ids.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);

            // 邮箱访问权限再次过滤（FTS 表上有冗余 category_id，但 MailboxId 仍需从 Category 查）
            if (accessibleMailboxIds != null)
            {
                var allowedSet = accessibleMailboxIds;
                ids = ids
                    .Where(id => dict.TryGetValue(id, out var a) &&
                                 (a.Category == null ||
                                  a.Category.MailboxId == null ||
                                  allowedSet.Contains(a.Category.MailboxId.Value)))
                    .ToList();
            }
            articles = ids.Select(id => dict.GetValueOrDefault(id)).OfType<KnowledgeArticle>().ToList();

            // 拼接 (Article, Snippet, Rank)
            var snipMap = ftsRows.ToDictionary(r => r.RowId, r => (r.Snippet, r.Rank));
            var result = new List<(KnowledgeArticle, string?, float?)>(articles.Count);
            foreach (var a in articles)
            {
                if (snipMap.TryGetValue(a.Id, out var sr))
                    result.Add((a, sr.Snippet, sr.Rank));
                else
                    result.Add((a, null, null));
            }
            return result;
        }

        // ====== LIKE 降级（与历史 SearchAsync 一致，但返回 Snippet=null）======
        var kw = keyword.Trim().ToLower();
        var likeQuery = ApplyIncludes(_context.KnowledgeArticles.AsNoTracking())
            .Where(a => a.DeletedAt == null && (includeDraft || a.Status == "published"))
            .Where(a =>
                a.Title.ToLower().Contains(kw) ||
                (a.Summary != null && a.Summary.ToLower().Contains(kw)) ||
                a.Content.ToLower().Contains(kw) ||
                (a.Tags != null && a.Tags.ToLower().Contains(kw)));

        if (accessibleMailboxIds != null)
        {
            var set = accessibleMailboxIds;
            likeQuery = likeQuery.Where(a =>
                a.CategoryId == null ||
                a.Category == null ||
                a.Category.MailboxId == null ||
                set.Contains(a.Category.MailboxId.Value));
        }

        likeQuery = likeQuery.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.UpdatedAt);
        var likeItems = await likeQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return likeItems.Select(a => (a, (string?)null, (float?)null)).ToList();
    }

    public async Task<int> SumViewCountAsync(string? status = null)
    {
        var query = _context.KnowledgeArticles.Where(a => a.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }
        return await query.SumAsync(a => a.ViewCount);
    }

    public async Task<int> SumLikeCountAsync(string? status = null)
    {
        var query = _context.KnowledgeArticles.Where(a => a.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }
        return await query.SumAsync(a => a.LikeCount);
    }

    public async Task<IReadOnlyList<KnowledgeArticle>> GetTopByViewCountAsync(string status, int count, int[]? accessibleMailboxIds = null)
    {
        var query = ApplyIncludes(_context.KnowledgeArticles.AsNoTracking())
            .Where(a => a.DeletedAt == null && a.Status == status);

        // 邮箱隔离：非超管用户仅可看到全局分类或授权邮箱归属分类下的文章
        if (accessibleMailboxIds != null)
        {
            query = query.Where(a =>
                a.Category == null ||
                a.Category.MailboxId == null ||
                accessibleMailboxIds.Contains(a.Category.MailboxId.Value));
        }

        query = query.OrderByDescending(a => a.ViewCount).Take(count);
        return await query.ToListAsync();
    }

    public async Task AddAsync(KnowledgeArticle article)
    {
        await _context.KnowledgeArticles.AddAsync(article);
    }

    /// <summary>
    /// ⚠️ 反模式：推荐改用 tracked 查询（默认 tracking，不带 AsNoTracking）+ 直接修改属性后 SaveChangesAsync，
    /// 避免全字段 UPDATE 以及对象图附加导致的跟踪冲突。
    /// </summary>
    [Obsolete("推荐改用 tracked 查询 + 直接修改属性")]
    public void Update(KnowledgeArticle article)
    {
        _context.KnowledgeArticles.Update(article);
    }

    public void Remove(KnowledgeArticle article)
    {
        _context.KnowledgeArticles.Remove(article);
    }

    public async Task IncrementViewCountAsync(int id)
    {
        // 添加 DeletedAt IS NULL 防护：原生 SQL 不受 EF Core 全局查询过滤器约束，
        // 防止 TOCTOU 竞态下已软删除文章的浏览数被更新
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeArticles SET ViewCount = ViewCount + 1 WHERE Id = {id} AND DeletedAt IS NULL");
    }

    public async Task IncrementLikeCountAsync(int id)
    {
        // 同上：防止已软删除文章的点赞数被更新
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeArticles SET LikeCount = LikeCount + 1 WHERE Id = {id} AND DeletedAt IS NULL");
    }

    /// <summary>
    /// KB-002: 原子化点赞去重。依赖 KnowledgeArticleLikes 表的 PRIMARY KEY (ArticleId, UserFingerprint) 唯一约束。
    /// 使用 INSERT OR IGNORE 在数据库层实现原子去重：
    ///   - 未点赞：插入成功，返回 1
    ///   - 已点赞：唯一约束冲突被 IGNORE，返回 0
    /// 该方式天然防止并发竞态导致的重复点赞（两个并发请求中只有一个能成功插入）。
    /// </summary>
    public async Task<int> TryInsertLikeAsync(int articleId, string userFingerprint)
    {
        // 使用 ExecuteSqlInterpolatedAsync + FormattableString 插值，EF Core 自动转为参数化查询防止 SQL 注入
        // 附带文章存在性 + 未软删除 + 已发布状态校验，防止对无效文章的点赞被记录
        return await _context.Database.ExecuteSqlInterpolatedAsync($@"
INSERT OR IGNORE INTO KnowledgeArticleLikes (ArticleId, UserFingerprint, CreatedAt)
SELECT {articleId}, {userFingerprint}, CURRENT_TIMESTAMP
WHERE EXISTS (
    SELECT 1 FROM KnowledgeArticles
    WHERE Id = {articleId} AND DeletedAt IS NULL AND Status = 'published'
);");
    }

    /// <summary>
    /// T000101: 取消点赞（删除 KnowledgeArticleLikes 中该用户对该文章的记录）。
    /// 返回删除的行数：1=成功取消，0=记录不存在（幂等）。
    /// </summary>
    public async Task<int> TryRemoveLikeAsync(int articleId, string userFingerprint)
    {
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM KnowledgeArticleLikes WHERE ArticleId = {articleId} AND UserFingerprint = {userFingerprint}");
    }

    /// <summary>
    /// T000101: 点赞数递减（取消点赞用），下限保护为 0。
    /// </summary>
    public async Task DecrementLikeCountAsync(int id)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeArticles SET LikeCount = MAX(LikeCount - 1, 0) WHERE Id = {id} AND DeletedAt IS NULL");
    }

    /// <summary>
    /// T000101: 批量查询当前用户已点赞的文章 ID 集合（用于填充 DTO.IsLiked）。
    /// 分批查询避免 IN 列表超过 SQLite 参数上限。
    /// </summary>
    public async Task<HashSet<int>> GetLikedArticleIdsAsync(string userFingerprint, IEnumerable<int> articleIds)
    {
        var ids = articleIds.Where(x => x > 0).Distinct().ToList();
        if (string.IsNullOrEmpty(userFingerprint) || ids.Count == 0) return new HashSet<int>();

        var liked = new HashSet<int>();
        const int batchSize = 400;
        for (var i = 0; i < ids.Count; i += batchSize)
        {
            var batch = ids.Skip(i).Take(batchSize).ToList();
            var parameters = new List<object> { userFingerprint };
            var placeholders = new List<string>();
            for (var j = 0; j < batch.Count; j++)
            {
                placeholders.Add($"{{{parameters.Count}}}");
                parameters.Add(batch[j]);
            }
            var sql = $"SELECT ArticleId FROM KnowledgeArticleLikes WHERE UserFingerprint = {{0}} AND ArticleId IN ({string.Join(",", placeholders)})";
            var rows = await _context.Database.SqlQueryRaw<int>(sql, parameters.ToArray()).ToListAsync();
            foreach (var r in rows) liked.Add(r);
        }
        return liked;
    }

    /// <summary>
    /// KB-010: 分类重命名后同步 FTS5 索引中的 category_name 字段。
    /// FTS5 触发器只在 KnowledgeArticles 表上创建，分类名变更不会自动同步，
    /// 需手动调用此方法重新生成该分类下所有文章的 FTS 记录。
    /// 使用 INSERT OR REPLACE 全量替换该分类下文章的 FTS 记录，确保 category_name 为最新值。
    /// </summary>
    public async Task SyncFtsByCategoryIdAsync(int categoryId)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync($@"
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
WHERE a.CategoryId = {categoryId} AND a.DeletedAt IS NULL;
");
    }

    private IQueryable<KnowledgeArticle> BuildQuery(
        int? categoryId, string? status, string? keyword, int? authorId, bool? isPinned, int[]? accessibleMailboxIds = null)
    {
        var query = ApplyIncludes(_context.KnowledgeArticles.AsNoTracking()).Where(a => a.DeletedAt == null).AsQueryable();
        if (categoryId.HasValue)
        {
            query = query.Where(a => a.CategoryId == categoryId.Value);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(a => a.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(a => a.Title.ToLower().Contains(kw) || (a.Summary != null && a.Summary.ToLower().Contains(kw)) || (a.Tags != null && a.Tags.ToLower().Contains(kw)));
        }
        if (authorId.HasValue)
        {
            query = query.Where(a => a.AuthorId == authorId.Value);
        }
        if (isPinned.HasValue)
        {
            query = query.Where(a => a.IsPinned == isPinned.Value);
        }
        // IDOR 防护：accessibleMailboxIds = null 表示不限制（超管）；非 null 时仅返回全局分类（Category.MailboxId 为 null）或属于授权邮箱的文章
        if (accessibleMailboxIds != null)
        {
            var mailboxIdSet = accessibleMailboxIds;
            query = query.Where(a =>
                a.CategoryId == null
                || a.Category == null
                || a.Category.MailboxId == null
                || mailboxIdSet.Contains(a.Category.MailboxId.Value));
        }
        return query;
    }

    // ====== 垃圾箱 ======

    public async Task<KnowledgeArticle?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_context.KnowledgeArticles.IgnoreQueryFilters().AsNoTracking())
            .FirstOrDefaultAsync(a => a.Id == id && a.DeletedAt != null);
    }

    public async Task<(IReadOnlyList<KnowledgeArticle> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_context.KnowledgeArticles.IgnoreQueryFilters())
            .Where(a => a.DeletedAt != null)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(a => a.Title.ToLower().Contains(kw) || (a.Summary != null && a.Summary.ToLower().Contains(kw)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.DeletedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _context.KnowledgeArticles.IgnoreQueryFilters().CountAsync(a => a.DeletedAt != null);
    }

    public async Task RestoreAsync(int id)
    {
        await _context.KnowledgeArticles.IgnoreQueryFilters()
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.DeletedAt, (DateTime?)null)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow));
    }

    /// <summary>
    /// 恢复已删除文章，同时将其关联到指定的新分类（用于原分类已删除的场景）。
    /// 通过单条 ExecuteUpdateAsync 原子完成，避免 tracked 实体修改+ExecuteUpdate 双写不一致问题。
    /// </summary>
    public async Task RestoreWithCategoryResetAsync(int id)
    {
        await _context.KnowledgeArticles.IgnoreQueryFilters()
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.DeletedAt, (DateTime?)null)
                .SetProperty(a => a.CategoryId, (int?)null)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow));
    }

    public async Task HardDeleteAsync(int id)
    {
        await _context.KnowledgeArticles.IgnoreQueryFilters()
            .Where(a => a.Id == id)
            .ExecuteDeleteAsync();
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        return await _context.KnowledgeArticles.IgnoreQueryFilters()
            .Where(a => a.DeletedAt != null)
            .ExecuteDeleteAsync();
    }
}
