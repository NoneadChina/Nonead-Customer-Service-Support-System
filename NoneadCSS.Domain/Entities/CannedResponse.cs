namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 快捷回复模板
/// </summary>
public class CannedResponse : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>关联邮箱 ID（null 表示全局可用）</summary>
    public int? MailboxId { get; set; }

    /// <summary>模板名称（用于搜索和识别）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模板正文（支持 HTML 和变量占位符）</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>模板分组/分类（如"欢迎"、"退款"、"技术支持"）</summary>
    public string? Category { get; set; }

    /// <summary>排序顺序（数值越小越靠前）</summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>是否为共享模板（所有客服可见）</summary>
    public bool IsShared { get; set; } = true;

    /// <summary>创建者用户 ID（个人模板的创建者）</summary>
    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>软删除标记（null 表示未删除，非空表示已删除并记录删除时间）</summary>
    public DateTime? DeletedAt { get; set; }

    public Mailbox? Mailbox { get; set; }
    public User? CreatedByUser { get; set; }
}
