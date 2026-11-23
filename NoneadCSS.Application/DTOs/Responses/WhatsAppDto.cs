using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>WhatsApp 配置响应（脱敏）</summary>
public class WhatsAppSettingsDto
{
    public string? PhoneNumberId { get; set; }
    public string? BusinessPhoneNumber { get; set; }
    public string? ApiVersion { get; set; }
    public int? MailboxId { get; set; }
    public bool ContactCollectionEnabled { get; set; }

    public bool AutoReplyEnabled { get; set; }
    public string? AutoReplyMessage { get; set; }
    public bool Enabled { get; set; }
    public string? CallbackUrl { get; set; }

    /// <summary>是否已配置 AccessToken（不返回原值）</summary>
    public bool HasAccessToken { get; set; }

    /// <summary>是否已配置 AppSecret（不返回原值）</summary>
    public bool HasAppSecret { get; set; }

    /// <summary>是否已配置 VerifyToken（不返回原值）</summary>
    public bool HasVerifyToken { get; set; }
}

/// <summary>WhatsApp Webhook 入站消息解析结果</summary>
public class WhatsAppWebhookMessage
{
    /// <summary>消息 ID（wamid.xxx 格式，用于去重）</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>发送者 wa_id（WhatsApp 号码，含国家区号）</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>发送者名称（从 contacts.profile.name 提取）</summary>
    public string? ProfileName { get; set; }

    /// <summary>消息类型：text/image/video/audio/document/sticker/interactive/button/location/contacts/system/unknown</summary>
    public string MessageType { get; set; } = "unknown";

    /// <summary>文本内容（text 类型）</summary>
    public string? TextBody { get; set; }

    /// <summary>媒体 ID（image/video/audio/document/sticker 类型）</summary>
    public string? MediaId { get; set; }

    /// <summary>媒体 MIME 类型</summary>
    public string? MimeType { get; set; }

    /// <summary>媒体标题（image/video/document 的 caption）</summary>
    public string? Caption { get; set; }

    /// <summary>文件名（document 类型）</summary>
    public string? Filename { get; set; }

    /// <summary>媒体下载 URL（Webhook 返回，5 分钟有效）</summary>
    public string? MediaUrl { get; set; }

    /// <summary>交互式消息标题（button_reply.title / list_reply.title）</summary>
    public string? InteractiveTitle { get; set; }

    /// <summary>按钮消息文本（button.text）</summary>
    public string? ButtonText { get; set; }

    /// <summary>位置 - 纬度</summary>
    public double? Latitude { get; set; }

    /// <summary>位置 - 经度</summary>
    public double? Longitude { get; set; }

    /// <summary>位置 - 名称</summary>
    public string? LocationName { get; set; }

    /// <summary>位置 - 地址</summary>
    public string? LocationAddress { get; set; }

    /// <summary>消息时间戳（Unix 秒）</summary>
    public long Timestamp { get; set; }

    /// <summary>接收方商家电话号码（display_phone_number）</summary>
    public string? DisplayPhoneNumber { get; set; }
}

/// <summary>WhatsApp 状态回执</summary>
public class WhatsAppStatusUpdate
{
    public string MessageId { get; set; } = string.Empty;
    public string RecipientWaId { get; set; } = string.Empty;
    /// <summary>状态：sent / delivered / read / failed</summary>
    public string Status { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ConversationId { get; set; }
}

/// <summary>WhatsApp 接入处理结果</summary>
public class WhatsAppIngestionResult
{
    public int NewConversationCount { get; set; }
    public int ReplyCount { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsStatus { get; set; }
    public int? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>自动回复已发送的内容（新会话且启用自动回复时非空）</summary>
    public string? AutoReplyContent { get; set; }
}
