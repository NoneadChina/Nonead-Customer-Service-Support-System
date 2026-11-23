using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新邮箱自动回复请求
/// </summary>
public class MailboxAutoReplyRequest
{
    public bool Enabled { get; set; }
    [MaxLength(500)]
    public string? Subject { get; set; }
    [MaxLength(10000)]
    public string? Body { get; set; }
    public bool SendOncePerCustomer { get; set; } = true;
    public List<string> TriggerOn { get; set; } = new();
    [MaxLength(100, ErrorMessage = "开始时间格式最多 100 个字符")]
    public string? AfterHoursStart { get; set; }
    [MaxLength(100, ErrorMessage = "结束时间格式最多 100 个字符")]
    public string? AfterHoursEnd { get; set; }
    public List<int> ExcludeUserIds { get; set; } = new();
}
