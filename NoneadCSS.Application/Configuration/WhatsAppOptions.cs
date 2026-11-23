using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// WhatsApp Business Platform (Cloud API) 接入配置选项。
/// 对应 appsettings.json 的 WhatsApp 节，运行时由 SystemOptions 数据库覆盖。
/// </summary>
public class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>Phone Number ID（Meta 平台获取）</summary>
    [MaxLength(64)]
    public string? PhoneNumberId { get; set; }

    /// <summary>Bearer Access Token（敏感字段，加密存储）</summary>
    [MaxLength(512)]
    public string? AccessToken { get; set; }

    /// <summary>App Secret（用于 HMAC-SHA256 签名验证，敏感字段，加密存储）</summary>
    [MaxLength(128)]
    public string? AppSecret { get; set; }

    /// <summary>Webhook 验证 Token（Meta 配置 Webhook 时填写的 Verify Token）</summary>
    [MaxLength(128)]
    public string? VerifyToken { get; set; }

    /// <summary>商家显示电话号码（如 +8613800138000）</summary>
    [MaxLength(32)]
    public string? BusinessPhoneNumber { get; set; }

    /// <summary>Graph API 版本（默认 v24.0）</summary>
    public string ApiVersion { get; set; } = "v24.0";

    /// <summary>关联的邮箱 ID（WhatsApp 会话归属此邮箱）</summary>
    public int? MailboxId { get; set; }

    /// <summary>是否启用客户信息及联系方式收集功能（新会话时发引导消息，客户回复后自动提取姓名/手机/邮箱）</summary>
    public bool ContactCollectionEnabled { get; set; } = true;

    /// <summary>是否启用自动回复（新会话欢迎语）</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>自动回复内容</summary>
    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    /// <summary>是否启用 WhatsApp 接入</summary>
    public bool Enabled { get; set; }

    /// <summary>回调路径（默认 /api/whatsapp）</summary>
    public string CallbackPath { get; set; } = "/api/whatsapp";

    /// <summary>API 可达性检测超时（秒）。用于 GetStatusAsync 探测 Graph API，避免网络波动误判、阻塞页面加载。</summary>
    public int StatusCheckTimeoutSeconds { get; set; } = 8;

    /// <summary>判断是否已配置核心凭证</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PhoneNumberId)
        && !string.IsNullOrWhiteSpace(AccessToken)
        && !string.IsNullOrWhiteSpace(AppSecret)
        && !string.IsNullOrWhiteSpace(VerifyToken);
}
