using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Responses;

// ====== 分类 DTO ======

public class KnowledgeCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? ParentId { get; set; }
    public string? ParentName { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public int SortOrder { get; set; }
    public string? Icon { get; set; }
    public bool IsActive { get; set; }
    public int ArticleCount { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public List<KnowledgeCategoryDto> Children { get; set; } = new();

    /// <summary>乐观锁版本标记（RowVersion base64 字符串），分类修改/拖拽时回传 ifMatch。</summary>
    [JsonPropertyName("rowVersion")]
    public string? RowVersion { get; set; }
}

public class CreateKnowledgeCategoryRequest
{
    [Required(ErrorMessage = "分类名称不能为空")]
    [MaxLength(100, ErrorMessage = "分类名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "分类描述最多 500 个字符")]
    public string? Description { get; set; }

    public int? ParentId { get; set; }
    public int? MailboxId { get; set; }
    public int SortOrder { get; set; }

    [MaxLength(50, ErrorMessage = "图标最多 50 个字符")]
    public string? Icon { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UpdateKnowledgeCategoryRequest : CreateKnowledgeCategoryRequest
{
    public int Id { get; set; }

    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}

public class KnowledgeCategoryQuery
{
    public int? ParentId { get; set; }
    public int? MailboxId { get; set; }
    public bool? IsActive { get; set; }

    [MaxLength(100, ErrorMessage = "关键字最多 100 个字符")]
    public string? Keyword { get; set; }
}

// ====== 文章 DTO ======

public class KnowledgeArticleDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    /// <summary>内容格式：markdown / html</summary>
    public string ContentType { get; set; } = "markdown";
    public string? Summary { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public string? Tags { get; set; }
    public List<string> TagList { get; set; } = new();
    /// <summary>文章级附件列表（PDF/图片/文档）</summary>
    public List<KnowledgeArticleAttachmentDto> Attachments { get; set; } = new();
    public string Status { get; set; } = "draft";
    public string StatusLabel { get; set; } = string.Empty;
    public int ViewCount { get; set; }
    public int LikeCount { get; set; }
    /// <summary>T000101: 当前用户是否已点赞（用于前端展示点赞/取消点赞状态）</summary>
    public bool IsLiked { get; set; }
    public int SortOrder { get; set; }
    public bool IsPinned { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? PublishedAt { get; set; }

    /// <summary>乐观锁版本标记（RowVersion base64 字符串），编辑/状态切换/点赞浏览回传 ifMatch。</summary>
    [JsonPropertyName("rowVersion")]
    public string? RowVersion { get; set; }

    /// <summary>搜索结果中的高亮片段（来自 FTS5 snippet；若未命中 FTS 则为 null）</summary>
    public string? Snippet { get; set; }

    /// <summary>FTS5 bm25 排序值（越小越相关；LIKE 回退时为 null）</summary>
    public double? Rank { get; set; }
}

/// <summary>
/// 知识库文章附件 DTO
/// </summary>
public class KnowledgeArticleAttachmentDto
{
    public int Id { get; set; }
    public int ArticleId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Extension { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    /// <summary>是否为 PDF（便于前端决定渲染方式：iframe 嵌入 vs 下载链接）</summary>
    public bool IsPdf => MimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    /// <summary>是否为图片</summary>
    public bool IsImage => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    /// <summary>人类可读文件大小（如 1.2 MB）</summary>
    public string FileSizeDisplay
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            return $"{FileSize / (1024.0 * 1024):F1} MB";
        }
    }
}

public class CreateKnowledgeArticleRequest
{
    [Required(ErrorMessage = "标题不能为空")]
    [MaxLength(200, ErrorMessage = "标题最多 200 个字符")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "内容不能为空")]
    [MaxLength(200000, ErrorMessage = "内容最多 200000 个字符")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 内容格式：markdown / html（默认 markdown）
    /// </summary>
    [RegularExpression("^(markdown|html)$", ErrorMessage = "内容格式只能为 markdown 或 html")]
    public string ContentType { get; set; } = "markdown";

    [MaxLength(500, ErrorMessage = "摘要最多 500 个字符")]
    public string? Summary { get; set; }

    public int? CategoryId { get; set; }
    public int? AuthorId { get; set; }

    [MaxLength(200, ErrorMessage = "标签最多 200 个字符")]
    public string? Tags { get; set; }

    [RegularExpression("^(draft|published|archived)$", ErrorMessage = "状态只能为 draft/published/archived")]
    public string Status { get; set; } = "draft";

    public int SortOrder { get; set; }
    public bool IsPinned { get; set; }
}

public class UpdateKnowledgeArticleRequest : CreateKnowledgeArticleRequest
{
    public int Id { get; set; }

    [JsonPropertyName("ifMatch")]
    public string? IfMatch { get; set; }
}

/// <summary>
/// 知识库文章附件上传请求（Application 层 DTO，避免在 Application 层引用 AspNetCore 的 IFormFile）。
/// 控制器层负责将 IFormFile 转换为该 DTO。
/// </summary>
public class KnowledgeAttachmentUploadRequest
{
    /// <summary>文件内容流（调用方负责释放）</summary>
    public required Stream Stream { get; set; }
    /// <summary>原始文件名（未清洗）</summary>
    public required string FileName { get; set; }
    /// <summary>MIME 类型</summary>
    public required string MimeType { get; set; }
    /// <summary>文件大小（字节）</summary>
    public required long FileSize { get; set; }
}

public class KnowledgeArticleQuery
{
    public int? CategoryId { get; set; }

    [RegularExpression("^(draft|published|archived)$", ErrorMessage = "状态只能为 draft/published/archived")]
    public string? Status { get; set; }

    [MaxLength(100, ErrorMessage = "关键字最多 100 个字符")]
    public string? Keyword { get; set; }

    public int? AuthorId { get; set; }
    public bool? IsPinned { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class KnowledgeOverviewResponse
{
    public Dictionary<string, int> Stats { get; set; } = new();
    public List<KnowledgeArticleDto> RecentArticles { get; set; } = new();
    public List<KnowledgeArticleDto> PopularArticles { get; set; } = new();
    public List<KnowledgeCategoryDto> TopCategories { get; set; } = new();
    public Dictionary<string, string> StatusOptions { get; set; } = new();
}

public class KnowledgeSearchResult
{
    public List<KnowledgeArticleDto> Data { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    /// <summary>
    /// 本次搜索是否命中 FTS5（true=使用虚拟表 snippet 片段，false=使用 LIKE 回退或无结果）。
    /// </summary>
    public bool IsFts { get; set; }
}
