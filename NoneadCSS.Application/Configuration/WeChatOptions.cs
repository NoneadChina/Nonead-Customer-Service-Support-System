using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 微信服务号接入配置选项
/// 对应 appsettings.json 的 WeChat 节
/// </summary>
public class WeChatOptions
{
    public const string SectionName = "WeChat";

    /// <summary>服务号 AppId</summary>
    [MaxLength(64)]
    public string? AppId { get; set; }

    /// <summary>服务号 AppSecret（密文存储时为加密后的值）</summary>
    [MaxLength(256)]
    public string? AppSecret { get; set; }

    /// <summary>服务器配置的 Token（用于签名校验）</summary>
    [MaxLength(64)]
    public string? Token { get; set; }

    /// <summary>消息加解密密钥（安全模式使用）</summary>
    [MaxLength(64)]
    public string? EncodingAESKey { get; set; }

    /// <summary>
    /// 消息加解密方式：plain(明文)、compatible(兼容)、safe(安全)
    /// </summary>
    public string EncryptMode { get; set; } = "plain";

    /// <summary>关联的邮箱 ID（微信会话归属此邮箱）</summary>
    public int? MailboxId { get; set; }

    /// <summary>是否启用客户信息及联系方式收集功能（新会话时发引导消息，客户回复后自动提取姓名/手机/邮箱）</summary>
    public bool ContactCollectionEnabled { get; set; } = true;

    /// <summary>是否启用自动回复</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>自动回复内容</summary>
    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; }

    /// <summary>是否启用微信接入</summary>
    public bool Enabled { get; set; }

    /// <summary>回调路径（默认 /api/wechat）</summary>
    public string CallbackPath { get; set; } = "/api/wechat";

    /// <summary>
    /// 回调基础 URL（可选）。配置后微信回调地址使用此值拼接 CallbackPath，
    /// 不再依赖 App:BaseUrl/App:FrontendUrl。适用于微信回调走独立域名/代理的场景。
    /// 未配置时回退到 App:BaseUrl 或 App:FrontendUrl。
    /// </summary>
    public string? CallbackBaseUrl { get; set; }

    // 出站微信 API 一律直连微信公众平台（https://api.weixin.qq.com/cgi-bin/...），
    // 不提供任何中转/代理网关配置项。

    /// <summary>判断是否已配置核心凭证</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId)
        && !string.IsNullOrWhiteSpace(AppSecret)
        && !string.IsNullOrWhiteSpace(Token);
}
