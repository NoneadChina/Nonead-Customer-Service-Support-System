using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Entities;

public class Attachment
{
    public int Id { get; set; }
    public int? ThreadId { get; set; }
    public int? UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public AttachmentType Type { get; set; } = AttachmentType.Other;
    public string FileDir { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool Embedded { get; set; }
    public string? ContentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ConversationThread Thread { get; set; } = null!;
    public User? User { get; set; }

    /// <summary>
    /// 是否为图片附件
    /// </summary>
    public bool IsImage() => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否为文档附件
    /// </summary>
    public bool IsDocument() =>
        MimeType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase) ||
        MimeType.StartsWith("application/msword", StringComparison.OrdinalIgnoreCase) ||
        MimeType.StartsWith("application/vnd.openxmlformats-officedocument", StringComparison.OrdinalIgnoreCase) ||
        MimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 获取文件扩展名
    /// </summary>
    public string GetFileExtension()
    {
        var dotIndex = FileName.LastIndexOf('.');
        return dotIndex >= 0 ? FileName[dotIndex..] : string.Empty;
    }

    /// <summary>
    /// 获取格式化的文件大小
    /// </summary>
    public string GetFileSizeFormatted()
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)Size;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return $"{size:0.#} {units[unitIndex]}";
    }
}
