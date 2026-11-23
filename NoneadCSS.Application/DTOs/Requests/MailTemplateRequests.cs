using System.ComponentModel.DataAnnotations;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateMailTemplateRequest
{
    [Required(ErrorMessage = "模板名称不能为空")]
    [MaxLength(200, ErrorMessage = "模板名称最多 200 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200, ErrorMessage = "模板标识最多 200 个字符")]
    public string Slug { get; set; } = string.Empty;

    public MailTemplateType Type { get; set; }

    [MaxLength(500, ErrorMessage = "主题最多 500 个字符")]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(100000, ErrorMessage = "HTML正文最多 100000 个字符")]
    public string BodyHtml { get; set; } = string.Empty;

    [MaxLength(100000, ErrorMessage = "纯文本正文最多 100000 个字符")]
    public string BodyText { get; set; } = string.Empty;

    public string? VariablesJson { get; set; }
    public bool IsActive { get; set; } = true;
    public int? MailboxId { get; set; }

    [MaxLength(500, ErrorMessage = "描述最多 500 个字符")]
    public string? Description { get; set; }

    [Range(0, 9999, ErrorMessage = "排序值无效")]
    public int SortOrder { get; set; }
}

public class UpdateMailTemplateRequest : CreateMailTemplateRequest { }

public class PreviewMailTemplateRequest
{
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public Dictionary<string, string> Variables { get; set; } = [];
}

public class MailTemplateQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public MailTemplateType? Type { get; set; }
    public int? MailboxId { get; set; }
    public bool? IsActive { get; set; }
    public string? Keyword { get; set; }
}

public class MailTemplatePreviewRequest
{
    public Dictionary<string, string>? CustomVariables { get; set; }
}
