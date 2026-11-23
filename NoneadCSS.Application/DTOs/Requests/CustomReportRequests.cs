using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateCustomReportRequest
{
    [Required(ErrorMessage = "报表名称不能为空")]
    [MaxLength(200, ErrorMessage = "报表名称最多 200 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000, ErrorMessage = "报表描述最多 2000 个字符")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "报表分类不能为空")]
    [MaxLength(50, ErrorMessage = "报表分类最多 50 个字符")]
    public string Category { get; set; } = "performance";

    public string Visibility { get; set; } = "private";

    public int? TeamId { get; set; }

    [Required(ErrorMessage = "报表配置不能为空")]
    public string ReportConfig { get; set; } = "{}";
}

public class UpdateCustomReportRequest
{
    [Required(ErrorMessage = "报表名称不能为空")]
    [MaxLength(200, ErrorMessage = "报表名称最多 200 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000, ErrorMessage = "报表描述最多 2000 个字符")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "报表分类不能为空")]
    [MaxLength(50, ErrorMessage = "报表分类最多 50 个字符")]
    public string Category { get; set; } = string.Empty;

    public string Visibility { get; set; } = "private";

    public int? TeamId { get; set; }

    [Required(ErrorMessage = "报表配置不能为空")]
    public string ReportConfig { get; set; } = "{}";
}

public class CreateScheduleRequest
{
    [Required(ErrorMessage = "重复周期不能为空")]
    public string Recurrence { get; set; } = "daily";

    public int? WeekDay { get; set; }

    [Required(ErrorMessage = "发送时间不能为空")]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "发送时间格式必须为 HH:mm")]
    public string SendTime { get; set; } = "09:00";

    [Required(ErrorMessage = "收件人不能为空")]
    public string Recipients { get; set; } = "[]";

    [Required(ErrorMessage = "邮件主题不能为空")]
    [MaxLength(200, ErrorMessage = "邮件主题最多 200 个字符")]
    public string Subject { get; set; } = string.Empty;

    public string AttachmentFormat { get; set; } = "pdf";

    public bool IsActive { get; set; } = true;
}

public class UpdateScheduleRequest : CreateScheduleRequest { }
