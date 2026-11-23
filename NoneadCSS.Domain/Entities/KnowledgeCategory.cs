using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 知识库分类（支持树形结构）
/// </summary>
public class KnowledgeCategory : ISoftDelete
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 父分类 ID（null 表示顶级分类）
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// 关联邮箱 ID（null 表示全局分类）
    /// </summary>
    public int? MailboxId { get; set; }

    /// <summary>
    /// 排序值（升序）
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 图标（颜色或 emoji）
    /// </summary>
    [MaxLength(50)]
    public string? Icon { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁 RowVersion；冲突来源：
    /// 1) 分类树拖拽排序/改名/改图标与另一客服并发操作
    /// 2) 批量迁移文章到新分类时树形结构被用户界面修改
    /// </summary>
    public byte[]? RowVersion { get; set; }

    // 导航属性
    public KnowledgeCategory? Parent { get; set; }
    public ICollection<KnowledgeCategory> Children { get; set; } = new List<KnowledgeCategory>();
    public ICollection<KnowledgeArticle>? Articles { get; set; }
}
