using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class Base64UploadRequest
{
    [Required]
    [MaxLength(10485760)]
    public string Base64Content { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string MimeType { get; set; } = string.Empty;
}

public class AttachmentValidateRequest
{
    [Required(ErrorMessage = "fileName 不能为空")]
    [StringLength(255, ErrorMessage = "fileName 长度不能超过 255 个字符")]
    public string FileName { get; set; } = string.Empty;

    [Range(0, long.MaxValue, ErrorMessage = "fileSize 必须为非负数")]
    public long FileSize { get; set; }

    [Required(ErrorMessage = "mimeType 不能为空")]
    [StringLength(100, ErrorMessage = "mimeType 长度不能超过 100 个字符")]
    public string MimeType { get; set; } = string.Empty;
}
