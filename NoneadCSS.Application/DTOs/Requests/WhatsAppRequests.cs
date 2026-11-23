using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>更新 WhatsApp 配置请求（敏感字段为 null 表示不修改）</summary>
public class UpdateWhatsAppSettingsRequest
{
    [MaxLength(64)]
    public string? PhoneNumberId { get; set; }

    [MaxLength(512)]
    public string? AccessToken { get; set; }

    [MaxLength(128)]
    public string? AppSecret { get; set; }

    [MaxLength(128)]
    public string? VerifyToken { get; set; }

    [MaxLength(32)]
    public string? BusinessPhoneNumber { get; set; }

    public string? ApiVersion { get; set; }

    public int? MailboxId { get; set; }

    public bool? ContactCollectionEnabled { get; set; }

    public bool? AutoReplyEnabled { get; set; }

    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    public bool? Enabled { get; set; }
}

/// <summary>WhatsApp 回复请求</summary>
public class WhatsAppReplyRequest
{
    [Required]
    public int ConversationId { get; set; }

    /// <summary>回复文本内容（与 AttachmentIds 至少一个非空）</summary>
    public string? Content { get; set; }

    /// <summary>附件 ID 列表（已在系统中上传的附件）</summary>
    public List<int>? AttachmentIds { get; set; }
}

/// <summary>WhatsApp 模板消息发送请求</summary>
public class WhatsAppTemplateRequest
{
    [Required]
    public int ConversationId { get; set; }

    [Required]
    [MaxLength(256)]
    public string TemplateName { get; set; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string Language { get; set; } = "zh_CN";

    /// <summary>模板组件参数（JSON 字符串，由前端构建）</summary>
    public string? ComponentsJson { get; set; }
}
