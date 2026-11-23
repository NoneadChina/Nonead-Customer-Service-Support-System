using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateThreadRequest
{
    [Required(ErrorMessage = "会话ID不能为空")]
    public int ConversationId { get; set; }

    [Required(ErrorMessage = "消息内容不能为空")]
    public string Body { get; set; } = string.Empty;

    [Required(ErrorMessage = "发送者邮箱不能为空")]
    [EmailAddress(ErrorMessage = "无效的邮箱格式")]
    public string From { get; set; } = string.Empty;

    [MaxLength(255, ErrorMessage = "收件人最多 255 个字符")]
    public string? To { get; set; }

    [MaxLength(255, ErrorMessage = "抄送最多 255 个字符")]
    public string? Cc { get; set; }

    [MaxLength(255, ErrorMessage = "密送最多 255 个字符")]
    public string? Bcc { get; set; }

    [MaxLength(500, ErrorMessage = "主题最多 500 个字符")]
    public string? Subject { get; set; }

    public bool IsNote { get; set; }

    public List<int>? AttachmentIds { get; set; }
}
