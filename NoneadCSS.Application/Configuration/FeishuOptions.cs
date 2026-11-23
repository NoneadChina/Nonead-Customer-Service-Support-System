using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 飞书机器人应用接入配置选项。
/// 对应 appsettings.json 的 Feishu 节，运行时由 SystemOptions 数据库覆盖。
/// </summary>
public class FeishuOptions
{
    public const string SectionName = "Feishu";

    /// <summary>飞书应用 App ID（cli_ 开头）</summary>
    [MaxLength(64)]
    public string? AppId { get; set; }

    /// <summary>飞书应用 App Secret（敏感字段，加密存储）</summary>
    [MaxLength(256)]
    public string? AppSecret { get; set; }

    /// <summary>事件订阅 Verification Token（用于校验事件来源）</summary>
    [MaxLength(128)]
    public string? VerificationToken { get; set; }

    /// <summary>事件加密 Encrypt Key（AES-256-CBC 解密密钥，敏感字段，加密存储）</summary>
    [MaxLength(128)]
    public string? EncryptKey { get; set; }

    /// <summary>是否启用事件加密</summary>
    public bool EncryptEnabled { get; set; }

    /// <summary>关联的邮箱 ID（飞书会话归属此邮箱）</summary>
    public int? MailboxId { get; set; }

    /// <summary>是否启用客户信息及联系方式收集功能（新会话时发引导消息，客户回复后自动提取姓名/手机/邮箱）</summary>
    public bool ContactCollectionEnabled { get; set; } = true;

    /// <summary>是否启用自动回复（新会话欢迎语）</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>自动回复内容</summary>
    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    /// <summary>是否启用飞书接入</summary>
    public bool Enabled { get; set; }

    /// <summary>回调路径（默认 /api/feishu）</summary>
    public string CallbackPath { get; set; } = "/api/feishu";

    /// <summary>HTTP 模式下 AccessToken 检测超时秒数（默认 3 秒）</summary>
    public int StatusCheckTimeoutSeconds { get; set; } = 3;

    /// <summary>
    /// 事件接收模式："Http"（Webhook 推送，默认）或 "Stream"（WebSocket 长连接）。
    /// Stream 模式无需公网 IP/域名，本地环境即可接收事件。
    /// </summary>
    public string ReceiveMode { get; set; } = "Http";

    /// <summary>
    /// 判断是否已配置核心凭证。
    /// - Stream 模式：仅需 AppId + AppSecret（无需公网回调 / Verification Token）
    /// - HTTP 模式：需 AppId + AppSecret + VerificationToken
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId)
        && !string.IsNullOrWhiteSpace(AppSecret)
        && (string.Equals(ReceiveMode, "Stream", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(VerificationToken));
}
