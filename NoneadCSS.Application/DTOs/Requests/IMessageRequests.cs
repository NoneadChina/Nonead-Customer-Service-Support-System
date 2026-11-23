using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新 iMessage 配置。所有字段可空：null = 不改动；空串 = 清除。
/// 密钥字段（Password / ApiKey / WebhookSecret / MspSharedSecret）只在非空时写入，并以 ENC: 前缀加密存储，绝不回显。
/// </summary>
public class UpdateIMessageSettingsRequest
{
    /// <summary>bridge / amb_msp / amb_apple_direct</summary>
    [MaxLength(32)]
    public string? Mode { get; set; }

    public bool? Enabled { get; set; }

    // ===== bridge =====
    [MaxLength(500)]
    public string? BaseUrl { get; set; }

    [MaxLength(16)]
    public string? AuthStyle { get; set; }

    /// <summary>BlueBubbles 口令（只写不回显）</summary>
    [MaxLength(500)]
    public string? Password { get; set; }

    /// <summary>bridge webhook 共享密钥（只写不回显；为空 = 不校验，仅保留旧行为并告警一次）</summary>
    [MaxLength(500)]
    public string? BridgeWebhookSecret { get; set; }

    [MaxLength(200)]
    public string? WebhookPath { get; set; }

    [MaxLength(500)]
    public string? WebhookBaseUrl { get; set; }

    [MaxLength(24)]
    public string? SendMethod { get; set; }

    public bool? AllowSmsFallback { get; set; }

    [MaxLength(2000)]
    public string? AllowList { get; set; }

    /// <summary>
    /// 出站限流（每分钟条数）。下限必须是 1：限流器对 &lt;= 0 一律拒绝发送，
    /// 填 0 会让 iMessage 全部出站静默停发（上限 600 与前端输入框一致，也远高于 Apple ID 安全容忍量）。
    /// </summary>
    [Range(1, 600)]
    public int? RateLimitPerMinute { get; set; }
    public bool? VerifyTls { get; set; }

    [MaxLength(200)]
    public string? HealthEndpoint { get; set; }

    [MaxLength(32)]
    public string? MinServerVersion { get; set; }

    public bool? ReadHistoryOnStart { get; set; }

    // ===== 会话 / 邮箱 / 自动回复 =====
    public int? MatchWindowHours { get; set; }
    public int? MailboxId { get; set; }
    public bool? AutoReplyEnabled { get; set; }

    [MaxLength(2000)]
    public string? AutoReplyMessage { get; set; }

    // ===== amb_msp =====
    [Range(1, 300)]
    public int? StatusCheckTimeoutSeconds { get; set; }

    public string? MspConnector { get; set; }

    [MaxLength(500)]
    public string? ApiBaseUrl { get; set; }

    /// <summary>MSP API Key / Bearer（只写不回显）</summary>
    [MaxLength(500)]
    public string? ApiKey { get; set; }

    /// <summary>校验 MSP 入站 HMAC（只写不回显）</summary>
    [MaxLength(500)]
    public string? WebhookSecret { get; set; }

    [MaxLength(200)]
    public string? BusinessIdentifier { get; set; }

    /// <summary>sms | rcs | none（Business Invitations 必须有回落渠道）</summary>
    [MaxLength(16)]
    public string? FallbackChannel { get; set; }

    // ===== amb_apple_direct（实验性）=====
    [MaxLength(200)]
    public string? MspId { get; set; }

    /// <summary>HS256 共享密钥（只写不回显）</summary>
    [MaxLength(500)]
    public string? MspSharedSecret { get; set; }

    public int? TokenTtlSeconds { get; set; }

    [MaxLength(500)]
    public string? GatewayBaseUrl { get; set; }

    [MaxLength(200)]
    public string? BusinessId { get; set; }

    [MaxLength(200)]
    public string? InboundPath { get; set; }

    public int? AttachmentKeySize { get; set; }
    public int? MaxBodyBytes { get; set; }

    /// <summary>实验性 Apple 直连开关（必须显式开启）</summary>
    public bool? ExperimentalAppleDirect { get; set; }
}

/// <summary>坐席回复请求。</summary>
public class IMessageReplyRequest
{
    [Required]
    public int ConversationId { get; set; }

    /// <summary>文本内容（与 AttachmentIds 至少一项）</summary>
    [MaxLength(4000)]
    public string? Content { get; set; }

    /// <summary>已上传附件 ID</summary>
    public List<int>? AttachmentIds { get; set; }
}
