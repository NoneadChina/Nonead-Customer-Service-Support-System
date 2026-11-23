using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>更新飞书配置请求（敏感字段为 null 表示不修改）</summary>
public class UpdateFeishuSettingsRequest
{
    [MaxLength(64)]
    public string? AppId { get; set; }

    [MaxLength(256)]
    public string? AppSecret { get; set; }

    [MaxLength(128)]
    public string? VerificationToken { get; set; }

    [MaxLength(128)]
    public string? EncryptKey { get; set; }

    public bool? EncryptEnabled { get; set; }

    public int? MailboxId { get; set; }

    public bool? ContactCollectionEnabled { get; set; }

    public bool? AutoReplyEnabled { get; set; }

    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    public bool? Enabled { get; set; }

    /// <summary>事件接收模式："Http"（Webhook）或 "Stream"（WebSocket 长连接）。null 表示不修改</summary>
    [MaxLength(16)]
    public string? ReceiveMode { get; set; }
}

/// <summary>飞书回复请求</summary>
public class FeishuReplyRequest
{
    [Required]
    public int ConversationId { get; set; }

    /// <summary>回复文本内容（与 AttachmentIds 至少一个非空）</summary>
    public string? Content { get; set; }

    /// <summary>附件 ID 列表（已在系统中上传的附件）</summary>
    public List<int>? AttachmentIds { get; set; }
}
