using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>更新 Messenger 配置请求（敏感字段为 null/空表示不修改；密钥只写不回显）</summary>
public class UpdateMessengerSettingsRequest
{
    [MaxLength(64)]
    public string? AppId { get; set; }

    /// <summary>App Secret（敏感；写入时加密，null/空 表示不修改）</summary>
    [MaxLength(128)]
    public string? AppSecret { get; set; }

    [MaxLength(64)]
    public string? PageId { get; set; }

    /// <summary>Page Access Token（敏感；写入时加密，null/空 表示不修改）</summary>
    [MaxLength(512)]
    public string? PageAccessToken { get; set; }

    /// <summary>Webhook Verify Token（null/空 表示不修改）</summary>
    [MaxLength(128)]
    public string? VerifyToken { get; set; }

    [MaxLength(64)]
    public string? PageUsername { get; set; }

    /// <summary>Graph API 版本（形如 v25.0；null 表示不修改）</summary>
    public string? ApiVersion { get; set; }

    public int? MailboxId { get; set; }

    // 上界钳到 Graph 的 2000 字符上限：配得更大时分片仍超平台上限，长回复会整体失败。
    [Range(200, 2000)]
    public int? TextChunkChars { get; set; }

    public int? WindowHours { get; set; }

    /// <summary>auto / none / always；null 表示不修改</summary>
    public string? MessageTagPolicy { get; set; }

    public bool? TypingIndicator { get; set; }

    public bool? MarkSeen { get; set; }

    public bool? ContactCollectionEnabled { get; set; }

    public bool? AutoReplyEnabled { get; set; }

    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    public bool? Enabled { get; set; }

    [MaxLength(512)]
    public string? SubscribedFields { get; set; }
}

/// <summary>Messenger 回复请求（坐席主动回复，窗口外按 MessageTagPolicy 带 message_tag）</summary>
public class MessengerReplyRequest
{
    [Required]
    public int ConversationId { get; set; }

    /// <summary>回复文本内容（与 AttachmentIds 至少一个非空）；超过 TextChunkChars 自动分片</summary>
    public string? Content { get; set; }

    /// <summary>附件 ID 列表（已在系统中上传的附件）</summary>
    public List<int>? AttachmentIds { get; set; }

    /// <summary>回复引用的入站消息 mid（可选，映射到 message.reply_to.id）</summary>
    public string? ReplyToMessageId { get; set; }
}

/// <summary>发送 sender_action 请求（typing_on / typing_off / mark_seen）</summary>
public class MessengerSenderActionRequest
{
    [Required]
    public int ConversationId { get; set; }

    [Required]
    [RegularExpression("^(typing_on|typing_off|mark_seen)$", ErrorMessage = "Action 取值 typing_on/typing_off/mark_seen")]
    public string Action { get; set; } = "typing_on";
}

/// <summary>订阅 Webhook 字段请求（需 pages_manage_metadata 权限）</summary>
public class MessengerSubscribeRequest
{
    /// <summary>字段清单，逗号分隔；null 时用配置的 MessengerOptions.SubscribedFields</summary>
    [MaxLength(512)]
    public string? Fields { get; set; }
}
