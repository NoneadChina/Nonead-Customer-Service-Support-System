using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 创建快捷回复模板请求
/// </summary>
public class CreateCannedResponseRequest
{
    /// <summary>关联邮箱 ID（null 表示全局）</summary>
    public int? MailboxId { get; set; }

    /// <summary>模板名称</summary>
    [Required(ErrorMessage = "模板名称不能为空")]
    [MaxLength(200, ErrorMessage = "模板名称最多 200 个字符")]
    public string Name { get; set; } = string.Empty;

    /// <summary>模板正文（支持 HTML）</summary>
    [Required(ErrorMessage = "模板正文不能为空")]
    public string Body { get; set; } = string.Empty;

    /// <summary>模板分组</summary>
    [MaxLength(100, ErrorMessage = "分组名称最多 100 个字符")]
    public string? Category { get; set; }

    /// <summary>排序顺序</summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>是否共享</summary>
    public bool IsShared { get; set; } = true;
}

/// <summary>
/// 更新快捷回复模板请求
/// </summary>
public class UpdateCannedResponseRequest
{
    [Required(ErrorMessage = "模板名称不能为空")]
    [MaxLength(200, ErrorMessage = "模板名称最多 200 个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "模板正文不能为空")]
    public string Body { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "分组名称最多 100 个字符")]
    public string? Category { get; set; }

    public int? MailboxId { get; set; }
    public int SortOrder { get; set; }
    public bool IsShared { get; set; } = true;
}
