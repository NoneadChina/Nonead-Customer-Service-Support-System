using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class ValidateTemplateVariablesRequest
{
    [MaxLength(500, ErrorMessage = "主题最多 500 个字符")]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(100000, ErrorMessage = "正文最多 100000 个字符")]
    public string Body { get; set; } = string.Empty;
}
