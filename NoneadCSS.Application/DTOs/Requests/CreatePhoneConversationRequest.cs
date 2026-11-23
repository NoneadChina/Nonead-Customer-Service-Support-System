using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreatePhoneConversationRequest
{
    [Required(ErrorMessage = "邮箱ID不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "邮箱ID必须大于0")]
    public int MailboxId { get; set; }

    [Required(ErrorMessage = "电话号码不能为空")]
    [MaxLength(50, ErrorMessage = "电话号码最多 50 个字符")]
    public string PhoneNumber { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "客户姓名最多 100 个字符")]
    public string? CustomerName { get; set; }

    [EmailAddress(ErrorMessage = "无效的客户邮箱格式")]
    [MaxLength(255, ErrorMessage = "客户邮箱最多 255 个字符")]
    public string? CustomerEmail { get; set; }

    [MaxLength(500, ErrorMessage = "主题最多 500 个字符")]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(50000, ErrorMessage = "正文最多 50000 个字符")]
    public string Body { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "用户ID必须大于0")]
    public int? UserId { get; set; }

    [Range(0, 86400, ErrorMessage = "通话时长必须在 0-86400 秒之间")]
    public int? CallDuration { get; set; }

    [MaxLength(2000, ErrorMessage = "通话备注最多 2000 个字符")]
    public string? CallNotes { get; set; }
}