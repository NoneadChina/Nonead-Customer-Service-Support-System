using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 知识库文章
/// </summary>
public class KnowledgeArticle : ISoftDelete
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 文章正文（Markdown 或 HTML，由 ContentType 决定渲染方式）
    /// </summary>
    [StringLength(int.MaxValue)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 内容格式：markdown / html（默认 markdown）
    /// </summary>
    [MaxLength(20)]
    public string ContentType { get; set; } = "markdown";

    /// <summary>
    /// 摘要
    /// </summary>
    [MaxLength(500)]
    public string? Summary { get; set; }

    /// <summary>
    /// 分类 ID
    /// </summary>
    public int? CategoryId { get; set; }

    /// <summary>
    /// 作者用户 ID
    /// </summary>
    public int? AuthorId { get; set; }

    /// <summary>
    /// 标签（逗号分隔）
    /// </summary>
    [MaxLength(500)]
    public string? Tags { get; set; }

    /// <summary>
    /// 状态：draft / published / archived
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "draft";

    /// <summary>
    /// 浏览数
    /// </summary>
    public int ViewCount { get; set; }

    /// <summary>
    /// 点赞数
    /// </summary>
    public int LikeCount { get; set; }

    /// <summary>
    /// 排序值
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否置顶
    /// </summary>
    public bool IsPinned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁 RowVersion；常见冲突场景：
    /// 1) 多客服同时编辑同一篇文章（标题/正文/分类/状态切换 published 与 archived）
    /// 2) 浏览点赞高并发：ViewCount/LikeCount 在不加分布式锁时容易 lost update
    /// 3) 批量改分类 / 批量发布时与用户界面编辑冲突
    /// SQLite 下 EF Core 自动在每次 SaveChanges 时自增 8 字节。
    /// </summary>
    public byte[]? RowVersion { get; set; }

    // 导航属性
    public KnowledgeCategory? Category { get; set; }
    // 作者导航属性：与 AuthorId 配合，EF Core 按约定建立与 User 的外键关系
    public User? Author { get; set; }
    // 文章级附件（PDF/图片/文档，独立于会话附件）
    public List<KnowledgeArticleAttachment> Attachments { get; set; } = new();
}
