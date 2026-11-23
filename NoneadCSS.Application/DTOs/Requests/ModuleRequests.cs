using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateModuleRequest
{
    [Required(ErrorMessage = "模块标识不能为空")]
    [MaxLength(50, ErrorMessage = "模块标识最多 50 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "模块名称不能为空")]
    [MaxLength(100, ErrorMessage = "模块名称最多 100 个字符")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "模块描述最多 500 个字符")]
    public string? Description { get; set; }

    [MaxLength(20, ErrorMessage = "版本号最多 20 个字符")]
    public string Version { get; set; } = "1.0.0";

    [MaxLength(100, ErrorMessage = "作者最多 100 个字符")]
    public string? Author { get; set; }

    [MaxLength(50, ErrorMessage = "图标最多 50 个字符")]
    public string? Icon { get; set; }

    [MaxLength(50, ErrorMessage = "分类最多 50 个字符")]
    public string Category { get; set; } = "general";

    [MaxLength(5000, ErrorMessage = "设置最多 5000 个字符")]
    public string? Settings { get; set; }

    [Range(0, 9999, ErrorMessage = "排序值无效")]
    public int SortOrder { get; set; }
}

public class UpdateModuleRequest
{
    [Required(ErrorMessage = "模块名称不能为空")]
    [MaxLength(100, ErrorMessage = "模块名称最多 100 个字符")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "模块描述最多 500 个字符")]
    public string? Description { get; set; }

    [MaxLength(20, ErrorMessage = "版本号最多 20 个字符")]
    public string Version { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "作者最多 100 个字符")]
    public string? Author { get; set; }

    [MaxLength(50, ErrorMessage = "图标最多 50 个字符")]
    public string? Icon { get; set; }

    [MaxLength(50, ErrorMessage = "分类最多 50 个字符")]
    public string Category { get; set; } = string.Empty;

    [MaxLength(5000, ErrorMessage = "设置最多 5000 个字符")]
    public string? Settings { get; set; }

    [Range(0, 9999, ErrorMessage = "排序值无效")]
    public int SortOrder { get; set; }
}

public class ModuleQuery
{
    [MaxLength(50, ErrorMessage = "分类最多 50 个字符")]
    public string? Category { get; set; }

    public bool? IsActive { get; set; }

    [MaxLength(100, ErrorMessage = "搜索关键词最多 100 个字符")]
    public string? Search { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "页码必须大于0")]
    public int Page { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "每页条数 1-100")]
    public int PageSize { get; set; } = 20;
}
