using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class ReplyRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "回复内容不能为空")]
    [MaxLength(100000, ErrorMessage = "回复内容不能超过 100000 字符")]
    public string Body { get; set; } = string.Empty;

    public bool IsInternal { get; set; }
}

public class SendEmailRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "MailboxId 无效")]
    public int MailboxId { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "收件人不能为空")]
    [MaxLength(500, ErrorMessage = "收件人不能超过 500 字符")]
    [EmailAddress(ErrorMessage = "收件人邮箱格式不正确")]
    public string To { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "主题不能超过 500 字符")]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(100000, ErrorMessage = "正文不能超过 100000 字符")]
    public string Body { get; set; } = string.Empty;
}
