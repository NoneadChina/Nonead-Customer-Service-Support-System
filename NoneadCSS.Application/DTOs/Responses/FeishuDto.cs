namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>飞书配置响应（脱敏）</summary>
public class FeishuSettingsDto
{
    public string? AppId { get; set; }

    /// <summary>是否已配置 App Secret（不返回原值）</summary>
    public bool HasAppSecret { get; set; }

    /// <summary>是否已配置 Verification Token（不返回原值，防止伪造回调）</summary>
    public bool HasVerificationToken { get; set; }

    /// <summary>是否已配置 Encrypt Key（不返回原值）</summary>
    public bool HasEncryptKey { get; set; }

    public bool EncryptEnabled { get; set; }
    public int? MailboxId { get; set; }
    public bool ContactCollectionEnabled { get; set; }
    public bool AutoReplyEnabled { get; set; }
    public string? AutoReplyMessage { get; set; }
    public bool Enabled { get; set; }
    public string? CallbackUrl { get; set; }

    /// <summary>事件接收模式："Http"（Webhook）或 "Stream"（WebSocket 长连接）</summary>
    public string? ReceiveMode { get; set; }
}

/// <summary>飞书事件消息解析结果</summary>
public class FeishuEventDto
{
    /// <summary>事件 ID（用于去重）</summary>
    public string? EventId { get; set; }

    /// <summary>事件类型（如 im.message.receive_v1）</summary>
    public string? EventType { get; set; }

    /// <summary>事件创建时间（Unix 毫秒）</summary>
    public string? CreateTime { get; set; }

    /// <summary>事件 token（用于校验）</summary>
    public string? Token { get; set; }

    /// <summary>应用 App ID</summary>
    public string? AppId { get; set; }

    /// <summary>租户 key</summary>
    public string? TenantKey { get; set; }

    // 发送者信息
    public string? SenderOpenId { get; set; }
    public string? SenderUserId { get; set; }
    public string? SenderUnionId { get; set; }
    public string? SenderType { get; set; }

    // 消息信息
    public string? MessageId { get; set; }
    public string? ChatType { get; set; }    // p2p / group
    public string? MessageType { get; set; }  // text / image / file / audio / media / post / interactive
    public string? Content { get; set; }      // JSON 序列化后的字符串
    public string? MessageCreateTime { get; set; }

    /// <summary>是否为挑战验证请求（URL 首次验证）</summary>
    public bool IsChallenge { get; set; }

    /// <summary>挑战验证字符串（仅 IsChallenge=true 时有值）</summary>
    public string? Challenge { get; set; }

    /// <summary>是否为事件回调（非消息事件）</summary>
    public bool IsEvent => !string.IsNullOrEmpty(EventType)
        && EventType != "im.message.receive_v1";

    /// <summary>消息正文文本（解析后）</summary>
    public string? BodyText { get; set; }
}

/// <summary>飞书接入处理结果</summary>
public class FeishuIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsEvent { get; set; }
    public int? ConversationId { get; set; }

    /// <summary>自动回复内容（新会话欢迎语，通过 API 异步发送）</summary>
    public string? AutoReplyContent { get; set; }

    public string? ErrorMessage { get; set; }
}
