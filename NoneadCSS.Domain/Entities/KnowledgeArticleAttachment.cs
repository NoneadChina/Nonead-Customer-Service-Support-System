using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 知识库文章附件（PDF/图片/文档，关联文章，独立于会话附件）
/// </summary>
public class KnowledgeArticleAttachment : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>
    /// 所属文章 ID
    /// </summary>
    public int ArticleId { get; set; }

    /// <summary>
    /// 原始文件名（已做安全清理，去除路径分隔符与控制字符）
    /// </summary>
    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 服务端存储相对路径（相对于 UploadPath 根目录），用于物理定位
    /// </summary>
    [Required, MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 对外访问的相对 URL（前端拼接 baseUrl 后可直接访问/下载）
    /// </summary>
    [Required, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// MIME 类型（如 application/pdf、image/png）
    /// </summary>
    [Required, MaxLength(100)]
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 文件扩展名（小写，不含点，如 pdf、png）
    /// </summary>
    [MaxLength(20)]
    public string Extension { get; set; } = string.Empty;

    public int? UploaderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // 导航属性
    public KnowledgeArticle? Article { get; set; }
    public User? Uploader { get; set; }
}
