using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateEmailSignatureRequest
{
    [Required(ErrorMessage = "签名名称不能为空")]
    [MaxLength(100, ErrorMessage = "签名名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "签名内容不能为空")]
    [MaxLength(10000, ErrorMessage = "签名内容最多 10000 个字符")]
    public string Body { get; set; } = string.Empty;

    public int? MailboxId { get; set; }

    public bool IsDefault { get; set; }

    [Range(0, 9999, ErrorMessage = "排序值无效")]
    public int SortOrder { get; set; }
}

public class UpdateEmailSignatureRequest
{
    [Required(ErrorMessage = "签名名称不能为空")]
    [MaxLength(100, ErrorMessage = "签名名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "签名内容不能为空")]
    [MaxLength(10000, ErrorMessage = "签名内容最多 10000 个字符")]
    public string Body { get; set; } = string.Empty;

    public int? MailboxId { get; set; }

    public bool IsDefault { get; set; }

    [Range(0, 9999, ErrorMessage = "排序值无效")]
    public int SortOrder { get; set; }
}

public class EmailSignatureQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? UserId { get; set; }
    public int? MailboxId { get; set; }
    public bool? IsDefault { get; set; }
    public string? Keyword { get; set; }
}
