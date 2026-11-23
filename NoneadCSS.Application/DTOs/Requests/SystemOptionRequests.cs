using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class UpdateSystemOptionRequest
{
    [Required(ErrorMessage = "选项名称不能为空")]
    [MaxLength(100, ErrorMessage = "选项名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "选项值不能为空")]
    [MaxLength(2000, ErrorMessage = "选项值最多 2000 个字符")]
    public string Value { get; set; } = string.Empty;

    [MaxLength(50, ErrorMessage = "分类最多 50 个字符")]
    public string? Category { get; set; }
}

public class CreateSystemOptionRequest : UpdateSystemOptionRequest { }

public class BatchUpdateSystemOptionRequest
{
    [Required(ErrorMessage = "选项列表不能为空")]
    [MinLength(1, ErrorMessage = "至少更新一个选项")]
    public Dictionary<string, string> Options { get; set; } = new();
}
