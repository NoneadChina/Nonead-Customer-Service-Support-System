using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 知识库服务实现
/// </summary>
public class KnowledgeService : IKnowledgeService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<KnowledgeService> _logger;
    private readonly AttachmentSettings _attachmentSettings;
    private readonly string _uploadBasePath;
    private readonly ILocalizationService? _localization;

    // 允许的文章状态白名单
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft", "published", "archived"
    };

    // KB-012: 移除 IDistributedCache 和 _likeDedupWindow——KB-002 改用数据库唯一约束去重后不再需要

    public KnowledgeService(IUnitOfWork unitOfWork, ICurrentUserAccessor currentUserAccessor, IOptions<AttachmentSettings> attachmentSettings, IHostEnvironment hostEnvironment, ILogger<KnowledgeService> logger, ILocalizationService? localization = null)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
        _attachmentSettings = attachmentSettings.Value;
        _uploadBasePath = Path.Combine(hostEnvironment.ContentRootPath, _attachmentSettings.UploadPath);
        _localization = localization;
        if (!Directory.Exists(_uploadBasePath))
        {
            Directory.CreateDirectory(_uploadBasePath);
        }
    }

    // ====== 分类 ======

    public async Task<List<KnowledgeCategoryDto>> GetCategoriesAsync(KnowledgeCategoryQuery query)
    {
        var accessibleMailboxIds = await _currentUserAccessor.GetAccessibleMailboxIdsAsync();

        var categories = await _unitOfWork.KnowledgeCategories.GetListAsync(
            query.ParentId, query.MailboxId, query.IsActive, query.Keyword);

        // IDOR 防护：过滤掉非授权邮箱归属的分类（超管 accessibleMailboxIds=null 不过滤）
        if (accessibleMailboxIds != null)
        {
            categories = categories
                .Where(c => c.MailboxId == null || accessibleMailboxIds.Contains(c.MailboxId.Value))
                .ToList();
        }

        // 批量预加载所有分类的文章数，避免 N+1 查询
        var articleCounts = await _unitOfWork.KnowledgeCategories.GetArticleCountsByCategoryIdsAsync(categories.Select(c => c.Id));

        var result = new List<KnowledgeCategoryDto>();
        foreach (var c in categories)
        {
            var dto = c.ToDto();
            dto.ArticleCount = articleCounts.GetValueOrDefault(c.Id, 0);
            result.Add(dto);
        }
        return result;
    }

    public async Task<KnowledgeCategoryDto?> GetCategoryByIdAsync(int id)
    {
        var c = await _unitOfWork.KnowledgeCategories.GetByIdAsync(id);
        if (c == null || c.DeletedAt != null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(c.MailboxId))
            return null;

        var dto = c.ToDto();
        dto.ArticleCount = await _unitOfWork.KnowledgeCategories.CountArticlesAsync(c.Id);
        return dto;
    }

    public async Task<KnowledgeCategoryDto> CreateCategoryAsync(CreateKnowledgeCategoryRequest request)
    {
        // 父分类存在性 + 权限校验
        if (request.ParentId.HasValue)
        {
            var parent = await _unitOfWork.KnowledgeCategories.GetByIdAsync(request.ParentId.Value);
            if (parent == null || parent.DeletedAt != null)
                throw new KeyNotFoundException("父分类不存在或已删除");
            if (!await _currentUserAccessor.CanAccessMailboxAsync(parent.MailboxId))
                throw new UnauthorizedAccessException("无权在所选父分类下创建子分类");
        }

        // 邮箱归属权限校验
        if (!await _currentUserAccessor.CanAccessMailboxAsync(request.MailboxId))
            throw new UnauthorizedAccessException("无权为所选邮箱创建分类");

        var category = new KnowledgeCategory
        {
            Name = request.Name,
            Description = request.Description,
            ParentId = request.ParentId,
            MailboxId = request.MailboxId,
            SortOrder = request.SortOrder,
            Icon = request.Icon,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _unitOfWork.KnowledgeCategories.AddAsync(category);
        await _unitOfWork.SaveChangesAsync();
        var created = await GetCategoryByIdAsync(category.Id);
        if (created == null)
            throw new InvalidOperationException("知识分类保存后无法重新加载，可能已被其他进程删除。");
        return created;
    }

    public async Task<KnowledgeCategoryDto> UpdateCategoryAsync(int id, UpdateKnowledgeCategoryRequest request, string? ifMatch = null)
    {
        var category = await _unitOfWork.Repository<KnowledgeCategory>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
        if (category == null) throw new KeyNotFoundException("分类不存在");

        // TD-07 并发冲突：分类树拖拽排序/改名/改图标 与批量迁移文章并发
        ConcurrencyCheckHelper.Check(
            category,
            category.RowVersion,
            ifMatch ?? request.IfMatch,
            entityType: "KnowledgeCategory",
            entityId: category.Id,
            latestFactory: () => category.ToDto(),
            _logger);

        if (!await _currentUserAccessor.CanAccessMailboxAsync(category.MailboxId))
            throw new UnauthorizedAccessException("无权访问该分类");

        // 循环引用校验：若设置 ParentId，沿父链向上遍历，禁止形成环
        if (request.ParentId.HasValue)
        {
            if (request.ParentId.Value == id)
                throw new InvalidOperationException("不能将分类设为自身的子分类");

            var visited = new HashSet<int> { id };
            var parentIdToCheck = request.ParentId.Value;
            while (parentIdToCheck != 0)
            {
                if (!visited.Add(parentIdToCheck))
                    throw new InvalidOperationException("检测到分类循环引用，已拒绝更新");
                var parent = await _unitOfWork.KnowledgeCategories.GetByIdAsync(parentIdToCheck);
                if (parent == null || parent.DeletedAt != null)
                    throw new KeyNotFoundException("父分类不存在或已删除");
                if (!await _currentUserAccessor.CanAccessMailboxAsync(parent.MailboxId))
                    throw new UnauthorizedAccessException("无权引用所选父分类");
                parentIdToCheck = parent.ParentId ?? 0;
            }
        }

        // 邮箱归属权限校验
        if (!await _currentUserAccessor.CanAccessMailboxAsync(request.MailboxId))
            throw new UnauthorizedAccessException("无权将分类归属到所选邮箱");

        // KB-010: 记录旧分类名，用于判断是否需要同步 FTS 索引
        var oldName = category.Name;

        category.Name = request.Name;
        category.Description = request.Description;
        category.ParentId = request.ParentId;
        category.MailboxId = request.MailboxId;
        category.SortOrder = request.SortOrder;
        category.Icon = request.Icon;
        category.IsActive = request.IsActive;
        category.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        // KB-010: 分类名变更后同步 FTS5 索引中的 category_name 字段
        // FTS5 触发器只在 KnowledgeArticles 表上创建，分类名变更不会自动同步到 FTS 索引
        if (!string.Equals(oldName, request.Name, StringComparison.Ordinal))
        {
            await _unitOfWork.KnowledgeArticles.SyncFtsByCategoryIdAsync(id);
        }
        var updated = await GetCategoryByIdAsync(id);
        if (updated == null)
            throw new InvalidOperationException("知识分类更新后无法重新加载，可能已被其他进程删除。");
        return updated;
    }

    public async Task DeleteCategoryAsync(int id)
    {
        var category = await _unitOfWork.Repository<KnowledgeCategory>()
            .GetQueryable(false)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
        if (category == null) return;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(category.MailboxId))
            return;

        // KB-001 修复：包裹事务，确保解除文章关联 + 递归软删除子分类 + 标记删除原子完成
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // 解除该分类下未删除文章的关联，防止悬空 CategoryId 导致权限绕过
            await _unitOfWork.KnowledgeCategories.UnlinkArticlesOfCategoryAsync(id);

            // 递归软删除子分类（避免遗留孤儿分类），限制最大递归深度防止栈溢出
            await SoftDeleteDescendantsAsync(id, depth: 0);

            category.DeletedAt = DateTime.UtcNow;
            category.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 分类树允许的最大递归深度。
    /// 防止数据异常（如循环引用残留）或恶意构造的超深层级导致栈溢出。
    /// </summary>
    private const int MaxCategoryDepth = 16;

    /// <summary>
    /// 递归软删除指定分类下的所有未删除子分类。
    /// 防止父分类被删除后子分类成为孤儿节点。
    /// 通过深度限制避免无限递归导致的栈溢出。
    /// </summary>
    private async Task SoftDeleteDescendantsAsync(int parentId, int depth)
    {
        // KB-006 修复：超过最大深度时抛出异常而非静默返回，防止孤儿分类残留
        if (depth >= MaxCategoryDepth)
        {
            throw new InvalidOperationException($"分类树深度超过最大限制（{MaxCategoryDepth}），无法继续递归删除，请检查是否存在分类循环引用");
        }

        var children = await _unitOfWork.KnowledgeCategories.GetListAsync(parentId: parentId);
        foreach (var child in children)
        {
            // 递归解除子分类下文章关联
            await _unitOfWork.KnowledgeCategories.UnlinkArticlesOfCategoryAsync(child.Id);
            // 递归处理孙级
            await SoftDeleteDescendantsAsync(child.Id, depth + 1);

            // GetListAsync 返回的实体已被 DbContext 追踪，直接修改属性即可被 SaveChangesAsync 持久化，
            // 无需重新查询（避免 N+1 冗余数据库往返）
            child.DeletedAt = DateTime.UtcNow;
            child.UpdatedAt = DateTime.UtcNow;
        }
    }

    // ====== 文章 ======

    public async Task<PagedResponse<KnowledgeArticleDto>> GetArticlesAsync(KnowledgeArticleQuery query)
    {
        var accessibleMailboxIds = await _currentUserAccessor.GetAccessibleMailboxIdsAsync();

        // 非超管用户强制只能查看已发布文章，防止草稿/归档内容泄露
        var effectiveStatus = _currentUserAccessor.IsSuperAdmin ? query.Status : "published";

        var items = await _unitOfWork.KnowledgeArticles.GetListAsync(
            query.CategoryId, effectiveStatus, query.Keyword,
            query.AuthorId, query.IsPinned,
            query.Page, query.PageSize,
            accessibleMailboxIds);

        var total = await _unitOfWork.KnowledgeArticles.CountAsync(
            query.CategoryId, effectiveStatus, query.Keyword,
            query.AuthorId, query.IsPinned,
            accessibleMailboxIds);

        var data = items.Select(a => a.ToDto()).ToList();
        // T000101: 填充当前用户是否已点赞，供前端显示/取消点赞状态
        await FillLikedFlagsAsync(data, userFingerprint: null);

        return new PagedResponse<KnowledgeArticleDto>
        {
            Data = data,
            CurrentPage = query.Page,
            PageSize = query.PageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        };
    }

    public async Task<KnowledgeArticleDto?> GetArticleByIdAsync(int id, bool incrementView = false)
    {
        var article = await _unitOfWork.KnowledgeArticles.GetByIdAsync(id);
        if (article == null || article.DeletedAt != null) return null;

        // 邮箱归属权限校验（IDOR 防护）
        if (!await _currentUserAccessor.CanAccessMailboxAsync(article.Category?.MailboxId))
            return null;

        // 非超管用户只能查看已发布文章，防止草稿/归档内容泄露
        if (!_currentUserAccessor.IsSuperAdmin && article.Status != "published")
            return null;

        if (incrementView)
        {
            await _unitOfWork.KnowledgeArticles.IncrementViewCountAsync(id);
            // KB-009 修复：重新查询最新 ViewCount，避免并发增量导致内存值与 DB 值不一致
            article.ViewCount = await _unitOfWork.Repository<KnowledgeArticle>()
                .GetQueryable(asNoTracking: true)
                .Where(a => a.Id == id)
                .Select(a => a.ViewCount)
                .FirstOrDefaultAsync();
        }
        var dto = article.ToDto();
        // T000101: 填充当前用户是否已点赞，供前端显示/取消点赞状态
        await FillLikedFlagsAsync(new[] { dto }, userFingerprint: null);
        return dto;
    }

    public async Task<KnowledgeArticleDto> CreateArticleAsync(CreateKnowledgeArticleRequest request)
    {
        // 状态白名单校验
        if (!AllowedStatuses.Contains(request.Status))
            throw new InvalidOperationException($"无效的文章状态：{request.Status}");

        // 分类存在性 + 权限校验
        if (request.CategoryId.HasValue)
        {
            var category = await _unitOfWork.KnowledgeCategories.GetByIdAsync(request.CategoryId.Value);
            if (category == null || category.DeletedAt != null)
                throw new KeyNotFoundException("分类不存在或已删除");
            // T000105：禁用的分类不能被分配为新文章的归属分类（已存在文章允许保持，不强制改动）
            if (!category.IsActive)
                throw new InvalidOperationException("所选分类已禁用，请先启用或更换为其他分类");
            if (!await _currentUserAccessor.CanAccessMailboxAsync(category.MailboxId))
                throw new UnauthorizedAccessException("无权在所选分类下创建文章");
        }

        // AuthorId 不可伪造：未提供或为 0 时默认当前用户
        var authorId = (request.AuthorId.HasValue && request.AuthorId.Value > 0)
            ? request.AuthorId.Value
            : (_currentUserAccessor.UserId > 0 ? _currentUserAccessor.UserId : (int?)null);

        // ContentType 白名单：默认 markdown，仅允许 markdown / html
        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "markdown" : request.ContentType.ToLowerInvariant();
        if (contentType != "markdown" && contentType != "html")
            throw new InvalidOperationException("内容格式只能为 markdown 或 html");

        // 内容按格式分流处理：
        // - html 模式：必须经过 HtmlSanitizerHelper.Sanitize 清理 XSS 危险标签/属性/script，防止存储型 XSS
        // - markdown 模式：保留原始 markdown 文本，不做 HTML 清洗（清洗会破坏 markdown 语法）
        //   前端渲染时需用安全 markdown 解析器（如 react-markdown）配合 rehype-sanitize
        var content = contentType == "html"
            ? HtmlSanitizerHelper.Sanitize(request.Content)
            : request.Content;

        var article = new KnowledgeArticle
        {
            Title = request.Title,
            Content = content,
            ContentType = contentType,
            Summary = request.Summary,
            CategoryId = request.CategoryId,
            AuthorId = authorId,
            Tags = request.Tags,
            Status = request.Status,
            SortOrder = request.SortOrder,
            IsPinned = request.IsPinned,
            PublishedAt = request.Status == "published" ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _unitOfWork.KnowledgeArticles.AddAsync(article);
        await _unitOfWork.SaveChangesAsync();
        var created = await GetArticleByIdAsync(article.Id);
        if (created == null)
            throw new InvalidOperationException("知识文章保存后无法重新加载，可能已被其他进程删除。");
        return created;
    }

    public async Task<KnowledgeArticleDto> UpdateArticleAsync(int id, UpdateKnowledgeArticleRequest request, string? ifMatch = null)
    {
        // 状态白名单校验
        if (!AllowedStatuses.Contains(request.Status))
            throw new InvalidOperationException($"无效的文章状态：{request.Status}");

        var article = await _unitOfWork.Repository<KnowledgeArticle>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(a => a.Id == id && a.DeletedAt == null);
        if (article == null) throw new KeyNotFoundException("文章不存在");

        // TD-07 并发冲突：多客服同时编辑文章正文/标题/状态切换；浏览点赞计数
        ConcurrencyCheckHelper.Check(
            article,
            article.RowVersion,
            ifMatch ?? request.IfMatch,
            entityType: "KnowledgeArticle",
            entityId: article.Id,
            latestFactory: () => article.ToDto(),
            _logger);

        // 归属过滤：检查文章所属分类的邮箱访问权限
        int? mailboxId = null;
        if (article.CategoryId.HasValue)
        {
            var category = await _unitOfWork.KnowledgeCategories.GetByIdAsync(article.CategoryId.Value);
            mailboxId = category?.MailboxId;
        }
        if (!await _currentUserAccessor.CanAccessMailboxAsync(mailboxId))
            throw new UnauthorizedAccessException("无权访问该文章");

        // 目标分类存在性 + 权限校验
        if (request.CategoryId.HasValue)
        {
            var targetCategory = await _unitOfWork.KnowledgeCategories.GetByIdAsync(request.CategoryId.Value);
            if (targetCategory == null || targetCategory.DeletedAt != null)
                throw new KeyNotFoundException("目标分类不存在或已删除");
            // T000105：禁用的分类不允许作为新的目标分类（只有当文章原本就归属该禁用分类时，未改动的情况除外）
            if (!targetCategory.IsActive && targetCategory.Id != article.CategoryId)
                throw new InvalidOperationException("所选分类已禁用，请先启用或更换为其他分类");
            if (!await _currentUserAccessor.CanAccessMailboxAsync(targetCategory.MailboxId))
                throw new UnauthorizedAccessException("无权将文章移动到所选分类");
        }

        // ContentType 白名单：默认 markdown，仅允许 markdown / html
        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "markdown" : request.ContentType.ToLowerInvariant();
        if (contentType != "markdown" && contentType != "html")
            throw new InvalidOperationException("内容格式只能为 markdown 或 html");

        // 内容按格式分流处理（同 CreateArticleAsync）：
        // - html 模式：经过 HtmlSanitizerHelper.Sanitize 防止存储型 XSS
        // - markdown 模式：保留原始 markdown 文本，前端用安全解析器渲染
        var content = contentType == "html"
            ? HtmlSanitizerHelper.Sanitize(request.Content)
            : request.Content;

        var wasPublished = article.Status == "published";
        article.Title = request.Title;
        article.Content = content;
        article.ContentType = contentType;
        article.Summary = request.Summary;
        article.CategoryId = request.CategoryId;
        // AuthorId 不可伪造：更新时禁止修改（仅允许保留原值，防止横向越权伪造作者）
        article.Tags = request.Tags;
        article.Status = request.Status;
        article.SortOrder = request.SortOrder;
        article.IsPinned = request.IsPinned;
        article.UpdatedAt = DateTime.UtcNow;
        if (!wasPublished && request.Status == "published")
        {
            article.PublishedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync();
        var updated = await GetArticleByIdAsync(id);
        if (updated == null)
            throw new InvalidOperationException("知识文章更新后无法重新加载，可能已被其他进程删除。");
        return updated;
    }

    public async Task DeleteArticleAsync(int id)
    {
        var article = await _unitOfWork.Repository<KnowledgeArticle>()
            .GetQueryable(false)
            .FirstOrDefaultAsync(a => a.Id == id && a.DeletedAt == null);
        if (article == null) return;

        // 归属过滤：检查文章所属分类的邮箱访问权限
        int? mailboxId = null;
        if (article.CategoryId.HasValue)
        {
            var category = await _unitOfWork.KnowledgeCategories.GetByIdAsync(article.CategoryId.Value);
            mailboxId = category?.MailboxId;
        }
        if (!await _currentUserAccessor.CanAccessMailboxAsync(mailboxId))
            return;

        article.DeletedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<bool> LikeArticleAsync(int id, string? userFingerprint)
    {
        // 校验文章存在性 + 未删除 + 已发布状态
        // 仅 published 文章允许点赞，draft/archived 文章不可点赞（避免泄露未公开内容）
        var article = await _unitOfWork.KnowledgeArticles.GetByIdAsync(id);
        if (article == null || article.DeletedAt != null || article.Status != "published")
        {
            throw new KeyNotFoundException("文章不存在");
        }

        // IDOR 防护：校验当前用户对文章所属分类的邮箱有访问权限
        if (!await _currentUserAccessor.CanAccessMailboxAsync(article.Category?.MailboxId))
        {
            throw new UnauthorizedAccessException("无权访问该文章");
        }

        // 点赞指纹：控制器标注 [Authorize]，用户必然已登录。
        // 使用 _currentUserAccessor.UserId 生成指纹，确保每用户唯一且不可伪造。
        var fingerprint = string.IsNullOrWhiteSpace(userFingerprint)
            ? (_currentUserAccessor.UserId > 0 ? $"u:{_currentUserAccessor.UserId}" : null)
            : userFingerprint;

        // 防御性校验：[Authorize] 保证用户已登录，UserId 不应为 0
        if (string.IsNullOrEmpty(fingerprint))
        {
            throw new InvalidOperationException("无法识别用户身份");
        }

        // KB-002 修复：使用数据库唯一约束原子化点赞去重，替代非原子的 cache check-then-set
        // INSERT OR IGNORE 依赖 KnowledgeArticleLikes 表的 PRIMARY KEY (ArticleId, UserFingerprint) 唯一约束
        // 并发请求中仅一个能成功插入，其余被忽略，确保每用户每篇文章仅一次点赞
        var affected = await _unitOfWork.KnowledgeArticles.TryInsertLikeAsync(id, fingerprint);
        if (affected > 0)
        {
            // 插入成功 → 点赞
            await _unitOfWork.KnowledgeArticles.IncrementLikeCountAsync(id);
            return true;
        }

        // T000101：已点赞 → 再次点击取消点赞（删除记录 + 递减计数）
        await _unitOfWork.KnowledgeArticles.TryRemoveLikeAsync(id, fingerprint);
        await _unitOfWork.KnowledgeArticles.DecrementLikeCountAsync(id);
        return false;
    }

    /// <summary>
    /// T000101: 解析当前用户点赞指纹（与 LikeArticleAsync 一致）。
    /// </summary>
    private string? ResolveFingerprint(string? userFingerprint)
    {
        return string.IsNullOrWhiteSpace(userFingerprint)
            ? (_currentUserAccessor.UserId > 0 ? $"u:{_currentUserAccessor.UserId}" : null)
            : userFingerprint;
    }

    /// <summary>
    /// T000101: 批量填充文章 DTO 的 IsLiked（当前用户是否已点赞）。
    /// 仅对可见文章批量查询一次 KnowledgeArticleLikes，避免逐条 N+1。
    /// </summary>
    private async Task FillLikedFlagsAsync(IEnumerable<KnowledgeArticleDto> dtos, string? userFingerprint)
    {
        var list = dtos.Where(d => d.Id > 0).ToList();
        if (list.Count == 0) return;
        var fp = ResolveFingerprint(userFingerprint);
        if (string.IsNullOrEmpty(fp)) return;
        var likedIds = await _unitOfWork.KnowledgeArticles.GetLikedArticleIdsAsync(fp, list.Select(d => d.Id));
        foreach (var d in list) d.IsLiked = likedIds.Contains(d.Id);
    }

    // ====== 文章附件 ======

    /// <summary>
    /// 获取指定文章的全部附件（软删除的附件自动过滤）
    /// </summary>
    public async Task<List<KnowledgeArticleAttachmentDto>> GetAttachmentsAsync(int articleId)
    {
        // 校验文章存在性 + 权限（与文章查看权限一致）
        var article = await _unitOfWork.KnowledgeArticles.GetByIdAsync(articleId);
        if (article == null || article.DeletedAt != null)
            throw new KeyNotFoundException("文章不存在");

        int? mailboxId = null;
        if (article.CategoryId.HasValue)
        {
            var category = await _unitOfWork.KnowledgeCategories.GetByIdAsync(article.CategoryId.Value);
            mailboxId = category?.MailboxId;
        }
        if (!await _currentUserAccessor.CanAccessMailboxAsync(mailboxId))
            throw new UnauthorizedAccessException("无权访问该文章");

        var attachments = await _unitOfWork.Repository<KnowledgeArticleAttachment>()
            .FindAsync(a => a.ArticleId == articleId && a.DeletedAt == null);
        return attachments
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => a.ToDto())
            .ToList();
    }

    /// <summary>
    /// 上传文章附件（PDF/图片/文档）。
    /// 安全措施：扩展名白名单 + MIME 白名单 + 大小限制 + 文件名安全清理 + 路径穿越防护。
    /// </summary>
    public async Task<KnowledgeArticleAttachmentDto> UploadAttachmentAsync(int articleId, KnowledgeAttachmentUploadRequest request, int? uploaderId = null)
    {
        // 校验文章存在性 + 权限
        var article = await _unitOfWork.Repository<KnowledgeArticle>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(a => a.Id == articleId && a.DeletedAt == null);
        if (article == null)
            throw new KeyNotFoundException("文章不存在");

        int? mailboxId = null;
        if (article.CategoryId.HasValue)
        {
            var category = await _unitOfWork.KnowledgeCategories.GetByIdAsync(article.CategoryId.Value);
            mailboxId = category?.MailboxId;
        }
        if (!await _currentUserAccessor.CanAccessMailboxAsync(mailboxId))
            throw new UnauthorizedAccessException("无权访问该文章");

        // 文件基本校验
        if (request.Stream == null || request.FileSize <= 0)
            throw new InvalidOperationException("文件内容为空");

        // 大小限制
        if (request.FileSize > _attachmentSettings.MaxSize)
            throw new InvalidOperationException($"文件大小超过限制（最大 {FormatFileSize(_attachmentSettings.MaxSize)}）");

        // 扩展名白名单校验
        var extension = Path.GetExtension(request.FileName)?.TrimStart('.').ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(extension) || !_attachmentSettings.AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("文件类型不允许");

        // MIME 白名单校验
        var mimeTypeAllowed = _attachmentSettings.AllowedMimeTypes
            .Any(m => request.MimeType.StartsWith(m, StringComparison.OrdinalIgnoreCase));
        if (!mimeTypeAllowed)
            throw new InvalidOperationException("文件 MIME 类型不允许");

        // 文件名安全清理：去除路径分隔符与控制字符，防止路径穿越和文件名注入
        var safeFileName = SanitizeFileName(request.FileName);

        // 存储路径：{basePath}/knowledge/{articleId}/{yyyy/MM/dd}/{guid}.{ext}
        // 按文章 ID 分目录，便于管理；按日期再分层防止单目录文件过多
        var fileDir = Path.Combine(_uploadBasePath, "knowledge", articleId.ToString(), DateTime.UtcNow.ToString("yyyy/MM/dd"));
        Directory.CreateDirectory(fileDir);

        var storedFileName = Guid.NewGuid() + "." + extension;
        var filePath = Path.Combine(fileDir, storedFileName);

        // 写入文件
        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Create);
            await request.Stream.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "知识库附件写入磁盘失败: ArticleId={ArticleId}, FileName={FileName}", articleId, safeFileName);
            throw new InvalidOperationException("文件保存失败，请重试");
        }

        // 相对路径（相对于 UploadPath 根目录），用于物理定位
        var relativeFilePath = Path.Combine("knowledge", articleId.ToString(), DateTime.UtcNow.ToString("yyyy/MM/dd"), storedFileName)
            .Replace('\\', '/');

        var attachment = new KnowledgeArticleAttachment
        {
            ArticleId = articleId,
            FileName = safeFileName,
            FilePath = relativeFilePath,
            Url = string.Empty, // 先占位，SaveChanges 后用生成的 Id 回填
            MimeType = request.MimeType,
            FileSize = request.FileSize,
            Extension = extension,
            UploaderId = uploaderId ?? (_currentUserAccessor.UserId > 0 ? _currentUserAccessor.UserId : (int?)null),
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Repository<KnowledgeArticleAttachment>().AddAsync(attachment);
        await _unitOfWork.SaveChangesAsync();

        // 回填对外访问 URL（需 Id 生成后才能构造 RESTful 下载路径）
        attachment.Url = $"/api/knowledge/articles/{articleId}/attachments/{attachment.Id}/download";
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("知识库附件上传成功: ArticleId={ArticleId}, AttachmentId={AttachmentId}, FileName={FileName}, Size={Size}",
            articleId, attachment.Id, safeFileName, request.FileSize);

        return attachment.ToDto();
    }

    /// <summary>
    /// 删除文章附件（软删除）。同时删除物理文件。
    /// </summary>
    public async Task<bool> DeleteAttachmentAsync(int articleId, int attachmentId)
    {
        // 校验文章存在性 + 权限
        var article = await _unitOfWork.Repository<KnowledgeArticle>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(a => a.Id == articleId && a.DeletedAt == null);
        if (article == null)
            throw new KeyNotFoundException("文章不存在");

        int? mailboxId = null;
        if (article.CategoryId.HasValue)
        {
            var category = await _unitOfWork.KnowledgeCategories.GetByIdAsync(article.CategoryId.Value);
            mailboxId = category?.MailboxId;
        }
        if (!await _currentUserAccessor.CanAccessMailboxAsync(mailboxId))
            throw new UnauthorizedAccessException("无权访问该文章");

        var attachment = await _unitOfWork.Repository<KnowledgeArticleAttachment>()
            .GetQueryable(asNoTracking: false)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ArticleId == articleId && a.DeletedAt == null);
        if (attachment == null)
            return false;

        // 删除物理文件（失败不影响数据库软删除，仅记录日志）
        try
        {
            var fullPath = GetSafeFilePath(attachment.FilePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "知识库附件物理文件删除失败: AttachmentId={AttachmentId}, FilePath={FilePath}", attachmentId, attachment.FilePath);
        }

        // 软删除
        attachment.DeletedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("知识库附件删除成功: ArticleId={ArticleId}, AttachmentId={AttachmentId}", articleId, attachmentId);
        return true;
    }

    /// <summary>
    /// 下载附件文件流。校验权限后返回 (Stream, MimeType, FileName)。
    /// 调用方负责释放 Stream。
    /// </summary>
    public async Task<(Stream Stream, string MimeType, string FileName)?> GetAttachmentFileAsync(int articleId, int attachmentId)
    {
        // 校验文章存在性 + 权限
        var article = await _unitOfWork.Repository<KnowledgeArticle>()
            .GetQueryable(asNoTracking: true)
            .FirstOrDefaultAsync(a => a.Id == articleId && a.DeletedAt == null);
        if (article == null) return null;

        int? mailboxId = null;
        if (article.CategoryId.HasValue)
        {
            var category = await _unitOfWork.KnowledgeCategories.GetByIdAsync(article.CategoryId.Value);
            mailboxId = category?.MailboxId;
        }
        if (!await _currentUserAccessor.CanAccessMailboxAsync(mailboxId))
            return null;

        var attachment = await _unitOfWork.Repository<KnowledgeArticleAttachment>()
            .GetQueryable(asNoTracking: true)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ArticleId == articleId && a.DeletedAt == null);
        if (attachment == null) return null;

        var fullPath = GetSafeFilePath(attachment.FilePath);
        if (!File.Exists(fullPath)) return null;

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream, attachment.MimeType, attachment.FileName);
    }

    /// <summary>
    /// 根据相对路径获取安全的绝对路径（防止路径穿越攻击）。
    /// </summary>
    private string GetSafeFilePath(string relativePath)
    {
        var cleanRelative = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_uploadBasePath, cleanRelative));
        var normalizedBase = Path.GetFullPath(_uploadBasePath);
        if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("无效的文件路径");
        return fullPath;
    }

    /// <summary>
    /// 文件名安全清理：只取文件名部分，替换非法字符，限制长度。
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed";

        var name = Path.GetFileName(fileName) ?? fileName;
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim('.', ' ');
        if (string.IsNullOrEmpty(name))
            name = "unnamed";
        if (name.Length > 255)
        {
            var ext = Path.GetExtension(name);
            var baseName = Path.GetFileNameWithoutExtension(name);
            name = (baseName.Length > 200 ? baseName[..200] : baseName) + ext;
        }
        return name;
    }

    /// <summary>
    /// 格式化文件大小为人类可读字符串
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    // ====== 搜索和概览 ======

    public async Task<KnowledgeSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 20)
    {
        var accessibleMailboxIds = await _currentUserAccessor.GetAccessibleMailboxIdsAsync();

        // 优先使用 FTS5 全文搜索（带 snippet/bm25；失败自动降级 LIKE）
        var matched = await _unitOfWork.KnowledgeArticles.FullTextSearchAsync(
            keyword: keyword,
            page: page,
            pageSize: pageSize,
            accessibleMailboxIds: accessibleMailboxIds,
            includeDraft: false);

        // Total 仍复用 SearchCountAsync（LIKE 口径，结果更完整，避免 FTS 未命中时 0 总数）
        var total = await _unitOfWork.KnowledgeArticles.SearchCountAsync(keyword, accessibleMailboxIds);
        var data = matched.Select(t => t.Article.ToDto(t.Snippet, t.Rank)).ToList();
        // T000101: 填充当前用户是否已点赞
        await FillLikedFlagsAsync(data, userFingerprint: null);
        return new KnowledgeSearchResult
        {
            Data = data,
            Total = total,
            Page = page,
            PageSize = pageSize,
            // snippet 标记：只要有一个结果带了片段就视为走了 FTS
            IsFts = matched.Any(t => t.Snippet != null),
        };
    }

    public async Task<KnowledgeOverviewResponse> GetOverviewAsync()
    {
        var accessibleMailboxIds = await _currentUserAccessor.GetAccessibleMailboxIdsAsync();
        var isFullAccess = accessibleMailboxIds == null;

        // IDOR 防护：非超管用户仅返回 published 相关数据，不泄露 draft/archived 统计
        // 顺序加载所有数据（DbContext 非线程安全，禁止并行访问同一 UnitOfWork）
        var publishedCount = await _unitOfWork.KnowledgeArticles.CountAsync(
            status: "published",
            accessibleMailboxIds: accessibleMailboxIds);

        var recentArticles = await _unitOfWork.KnowledgeArticles.GetListAsync(
            status: "published",
            page: 1,
            pageSize: 5,
            accessibleMailboxIds: accessibleMailboxIds);

        var popularArticles = await _unitOfWork.KnowledgeArticles.GetTopByViewCountAsync("published", 5, accessibleMailboxIds);

        // 概览统计 - 非超管仅返回 published 计数和公开统计
        var stats = new Dictionary<string, int>();

        if (isFullAccess)
        {
            // 超管可见全部统计
            var total = await _unitOfWork.KnowledgeArticles.CountAsync();
            var draftCount = await _unitOfWork.KnowledgeArticles.CountAsync(status: "draft");
            var archivedCount = await _unitOfWork.KnowledgeArticles.CountAsync(status: "archived");
            var pinnedCount = await _unitOfWork.KnowledgeArticles.CountAsync(isPinned: true);
            var totalViews = await _unitOfWork.KnowledgeArticles.SumViewCountAsync();
            var totalLikes = await _unitOfWork.KnowledgeArticles.SumLikeCountAsync();
            var categories = await _unitOfWork.KnowledgeCategories.GetAllAsync();

            stats["total"] = total;
            stats["draft"] = draftCount;
            stats["published"] = publishedCount;
            stats["archived"] = archivedCount;
            stats["pinned"] = pinnedCount;
            stats["totalViews"] = totalViews;
            stats["totalLikes"] = totalLikes;
            stats["categories"] = categories.Count;
        }
        else
        {
            // 普通用户仅返回 published 计数和公开文章浏览/点赞总数
            var categories = await _unitOfWork.KnowledgeCategories.GetAllAsync();
            var accessibleCategoryCount = categories
                .Count(c => c.MailboxId == null || accessibleMailboxIds!.Contains(c.MailboxId.Value));

            stats["published"] = publishedCount;
            stats["categories"] = accessibleCategoryCount;
            // 其余字段（draft/archived/total）不返回，前端按 0 处理
        }

        var recentArticleDtos = recentArticles.Select(a => a.ToDto()).ToList();
        var popularArticleDtos = popularArticles.Select(a => a.ToDto()).ToList();
        // T000101: 填充当前用户是否已点赞（recent + popular 合并批量查询）
        await FillLikedFlagsAsync(recentArticleDtos.Concat(popularArticleDtos), userFingerprint: null);

        List<KnowledgeCategoryDto> topCategories;
        if (isFullAccess)
        {
            var allCategories = await _unitOfWork.KnowledgeCategories.GetAllAsync();
            topCategories = allCategories.Take(10).Select(c => c.ToDto()).ToList();
        }
        else
        {
            var allCategories = await _unitOfWork.KnowledgeCategories.GetAllAsync();
            topCategories = allCategories
                .Where(c => c.MailboxId == null || accessibleMailboxIds!.Contains(c.MailboxId.Value))
                .Take(10)
                .Select(c => c.ToDto())
                .ToList();
        }

        return new KnowledgeOverviewResponse
        {
            Stats = stats,
            RecentArticles = recentArticleDtos,
            PopularArticles = popularArticleDtos,
            TopCategories = topCategories,
            StatusOptions = BuildLocalizedStatusOptions()
        };
    }

    /// <summary>
    /// 文章状态目录：走 Messages 资源（en / zh-Hans），词条缺失或未注入本地化服务时回退 zh-Hans 原文。
    /// </summary>
    private Dictionary<string, string> BuildLocalizedStatusOptions() => new(StringComparer.Ordinal)
    {
        ["draft"] = Translate("KnowledgeStatus_draft", "草稿"),
        ["published"] = Translate("KnowledgeStatus_published", "已发布"),
        ["archived"] = Translate("KnowledgeStatus_archived", "已归档")
    };

    /// <summary>
    /// 取词条文本：本地化服务在词条缺失时会返回键名本身，此时回退给定中文原文，避免把键名吐给前端。
    /// </summary>
    private string Translate(string key, string zhFallback, params object[] args)
    {
        if (_localization is null) return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        var value = _localization[key];
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        }
        return args.Length > 0 ? string.Format(value, args) : value;
    }

    // ====== 文章垃圾箱 ======

    public async Task<PagedResponse<KnowledgeArticleDto>> GetArticleTrashAsync(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.KnowledgeArticles.GetDeletedPagedAsync(page, pageSize, keyword);
        return new PagedResponse<KnowledgeArticleDto>
        {
            Data = items.Select(a => a.ToDto()).ToList(),
            CurrentPage = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<KnowledgeArticleDto?> RestoreArticleAsync(int id)
    {
        var article = await _unitOfWork.KnowledgeArticles.GetDeletedByIdAsync(id);
        if (article == null) return null;

        // 事务保护：检查分类状态 + 恢复操作必须原子完成
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            // 恢复前校验：若原分类已删除/不存在，使用 RestoreWithCategoryResetAsync 原子清空 CategoryId
            // 避免之前 tracked 实体修改 + ExecuteUpdateAsync 双写不一致导致修改丢失
            bool needCategoryReset = false;
            if (article.CategoryId.HasValue)
            {
                var category = await _unitOfWork.KnowledgeCategories.GetByIdAsync(article.CategoryId.Value);
                if (category == null || category.DeletedAt != null)
                {
                    needCategoryReset = true;
                }
            }

            if (needCategoryReset)
            {
                await _unitOfWork.KnowledgeArticles.RestoreWithCategoryResetAsync(id);
            }
            else
            {
                await _unitOfWork.KnowledgeArticles.RestoreAsync(id);
            }

            await transaction.CommitAsync();
            return await GetArticleByIdAsync(id);
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            throw;
        }
    }

    public async Task HardDeleteArticleAsync(int id)
    {
        // KB-007 修复：非超管调用时抛出异常而非静默返回，defense-in-depth
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException("仅超管可硬删除文章");
        }
        await _unitOfWork.KnowledgeArticles.HardDeleteAsync(id);
    }

    public async Task<int> ClearArticleTrashAsync()
    {
        return await _unitOfWork.KnowledgeArticles.HardDeleteAllDeletedAsync();
    }

    // ====== 分类垃圾箱 ======

    public async Task<PagedResponse<KnowledgeCategoryDto>> GetCategoryTrashAsync(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.KnowledgeCategories.GetDeletedPagedAsync(page, pageSize, keyword);
        return new PagedResponse<KnowledgeCategoryDto>
        {
            Data = items.Select(c => c.ToDto()).ToList(),
            CurrentPage = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<KnowledgeCategoryDto?> RestoreCategoryAsync(int id)
    {
        var category = await _unitOfWork.KnowledgeCategories.GetDeletedByIdAsync(id);
        if (category == null) return null;

        // 事务保护：检查父分类状态 + 恢复操作必须原子完成
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            // 恢复前校验：若父分类已删除/不存在，使用 RestoreWithParentResetAsync 原子清空 ParentId
            // 避免之前 tracked 实体修改 + ExecuteUpdateAsync 双写不一致导致修改丢失
            bool needParentReset = false;
            if (category.ParentId.HasValue)
            {
                var parent = await _unitOfWork.KnowledgeCategories.GetByIdAsync(category.ParentId.Value);
                if (parent == null || parent.DeletedAt != null)
                {
                    needParentReset = true;
                }
            }

            if (needParentReset)
            {
                await _unitOfWork.KnowledgeCategories.RestoreWithParentResetAsync(id);
            }
            else
            {
                await _unitOfWork.KnowledgeCategories.RestoreAsync(id);
            }

            await transaction.CommitAsync();
            return await GetCategoryByIdAsync(id);
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            throw;
        }
    }

    public async Task HardDeleteCategoryAsync(int id)
    {
        // KB-007 修复：非超管调用时抛出异常而非静默返回
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            throw new UnauthorizedAccessException("仅超管可硬删除分类");
        }
        // 单分类硬删除：包裹事务，保证解除文章关联 + 删除分类原子性
        await using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.KnowledgeCategories.HardDeleteAsync(id);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<int> ClearCategoryTrashAsync()
    {
        // 清空分类垃圾箱：包裹事务，保证解除文章关联 + 删除分类原子性，避免中途失败导致数据不一致
        await using var tx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var count = await _unitOfWork.KnowledgeCategories.HardDeleteAllDeletedAsync();
            await tx.CommitAsync();
            return count;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
