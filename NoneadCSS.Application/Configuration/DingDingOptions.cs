using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 钉钉开放平台接入配置选项
/// 对应 appsettings.json 的 DingDing 节
/// </summary>
public class DingDingOptions
{
    public const string SectionName = "DingDing";

    /// <summary>企业内部应用 AppKey（即 ClientId）</summary>
    [MaxLength(64)]
    public string? AppKey { get; set; }

    /// <summary>企业内部应用 AppSecret（即 ClientSecret，密文存储时为加密后的值）</summary>
    [MaxLength(256)]
    public string? AppSecret { get; set; }

    /// <summary>应用 AgentId（工作通知模式使用，机器人模式可空）</summary>
    public long? AgentId { get; set; }

    /// <summary>机器人编码（机器人模式使用）</summary>
    [MaxLength(64)]
    public string? RobotCode { get; set; }

    /// <summary>事件订阅签名 Token（用于回调签名校验）</summary>
    [MaxLength(64)]
    public string? CallbackToken { get; set; }

    /// <summary>事件订阅 AES 加密密钥（43位字符串，HTTP模式必填）</summary>
    [MaxLength(64)]
    public string? EncodingAesKey { get; set; }

    /// <summary>接收消息模式：Http 或 Stream（默认 Http）</summary>
    public string ReceiveMode { get; set; } = "Http";

    /// <summary>关联的邮箱 ID（钉钉会话归属此邮箱）</summary>
    public int? MailboxId { get; set; }

    /// <summary>是否启用客户信息及联系方式收集功能（新会话时发引导消息，客户回复后自动提取姓名/手机/邮箱）</summary>
    public bool ContactCollectionEnabled { get; set; } = true;

    /// <summary>是否启用自动回复</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>自动回复内容</summary>
    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    /// <summary>是否启用钉钉接入</summary>
    public bool Enabled { get; set; }

    /// <summary>回调路径（默认 /api/dingding）</summary>
    public string CallbackPath { get; set; } = "/api/dingding";

    /// <summary>HTTP 模式下 API 可达性检测超时秒数（默认 8 秒）</summary>
    public int StatusCheckTimeoutSeconds { get; set; } = 8;

    /// <summary>判断是否已配置核心凭证</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppKey)
        && !string.IsNullOrWhiteSpace(AppSecret);
}
