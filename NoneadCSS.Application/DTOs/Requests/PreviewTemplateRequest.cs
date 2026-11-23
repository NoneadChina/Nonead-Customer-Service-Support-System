using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class PreviewTemplateRequest
{
    [Required(ErrorMessage = "模板内容不能为空")]
    public string Template { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "主题最多 500 个字符")]
    public string? Subject { get; set; }

    public Dictionary<string, string> Variables { get; set; } = [];
}
