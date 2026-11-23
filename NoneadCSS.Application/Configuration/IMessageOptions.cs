using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.Configuration;

/// <summary>
/// iMessage / Apple Messages 渠道配置（SectionName = "IMessage"）。
/// 一个抽象两套传输（见 docs/integration/imessage.md）：
///   bridge            — 自建常开 macOS 上的 BlueBubbles REST 服务器（默认，非官方）
///   amb_msp           — Apple Messages for Business（AMB）经已核准 MSP（配置存在才启用，默认关闭）
///   amb_apple_direct  — 直连 mspgw.push.apple.com（实验性，仅 Apple 核准的 MSP 可用，默认关闭）
/// 说明：本仓库不包含 macOS 桥接服务器，bridge 模式需要另行部署 BlueBubbles。
/// </summary>
public class IMessageOptions
{
    public const string SectionName = "IMessage";

    /// <summary>传输模式：bridge / amb_msp / amb_apple_direct（默认 bridge）。</summary>
    [MaxLength(32)]
    public string Mode { get; set; } = IMessageModes.Bridge;

    /// <summary>渠道总开关（关闭时不接受入站、不允许坐席发送）。</summary>
    public bool Enabled { get; set; }

    // ===== bridge（BlueBubbles REST）=====

    /// <summary>BlueBubbles 服务器基址，例如 https://bb.example.internal:1234（建议走隧道，勿直开 1234）。</summary>
    [MaxLength(500)]
    public string? BaseUrl { get; set; }

    /// <summary>鉴权方式：bearer（默认，推荐）| query（guid/password/token 走 query，会把密钥写进访问日志）。</summary>
    [MaxLength(16)]
    public string AuthStyle { get; set; } = IMessageAuthStyles.Bearer;

    /// <summary>BlueBubbles 服务器口令（加密存储，只写不回显、绝不写进可被记录的 URL）。</summary>
    [MaxLength(500)]
    public string? Password { get; set; }

    /// <summary>
    /// bridge webhook 的共享密钥（加密存储，只写不回显）。
    /// BlueBubbles 的 webhook 载荷不带签名，端点又是固定路径 ⇒ 单靠"路径不可猜"挡不住伪造入站。
    /// 配置后入站必须携带 X-Webhook-Secret 头（或 ?secret= 查询参数）。为空时保持旧行为（放行）并打一次性告警，
    /// 避免升级瞬间把已在运行的部署打死。
    /// </summary>
    [MaxLength(500)]
    public string? BridgeWebhookSecret { get; set; }

    /// <summary>本系统暴露的 webhook 路径（相对 App:BaseUrl）。默认 /api/imessage/bluebubbles/webhook。</summary>
    [MaxLength(200)]
    public string WebhookPath { get; set; } = "/api/imessage/bluebubbles/webhook";

    /// <summary>webhook 回调公网基址覆盖（缺省用 App:BaseUrl）。</summary>
    [MaxLength(500)]
    public string? WebhookBaseUrl { get; set; }

    /// <summary>发送方式：apple-script（默认，普通权限）| private-api（需关闭 SIP）| any。</summary>
    [MaxLength(24)]
    public string SendMethod { get; set; } = IMessageSendMethods.AppleScript;

    /// <summary>SMS/RCS 回落。默认 false：客服场景把 iMessage 静默降级成 SMS 属质量事故（且是另一条法律渠道）。</summary>
    public bool AllowSmsFallback { get; set; }

    /// <summary>入站白名单（handle / 邮箱 / chatGuid，逗号分隔；空=不限制）。</summary>
    [MaxLength(2000)]
    public string? AllowList { get; set; }

    /// <summary>
    /// 出站限流的代码默认值（每分钟条数）。也是配置里读到 0 或负数时的回落值：
    /// <c>IMessageRateLimiter.TryAcquire</c> 对 <c>limitPerMinute &lt;= 0</c> 一律拒绝，
    /// 存 0 会让全部出站静默停发，所以这个值不允许为 0。
    /// </summary>
    public const int DefaultRateLimitPerMinute = 20;

    /// <summary>出站限流（每分钟条数）。持续大出站量有 Apple ID 锁定风险。</summary>
    public int RateLimitPerMinute { get; set; } = DefaultRateLimitPerMinute;

    /// <summary>是否校验 TLS 证书（自签隧道场景可关；默认 true）。</summary>
    public bool VerifyTls { get; set; } = true;

    /// <summary>健康检查端点（BlueBubbles ≥0.2.0 提供 /api/v1/ping）。</summary>
    [MaxLength(200)]
    public string HealthEndpoint { get; set; } = "/api/v1/ping";

    /// <summary>要求最低服务器版本（webhook 需 ≥1.0.0）。</summary>
    [MaxLength(32)]
    public string MinServerVersion { get; set; } = "1.0.0";

    /// <summary>启动时是否回读历史（只读预热 chatIdentifier→chatGuid 缓存，不建会话）。</summary>
    public bool ReadHistoryOnStart { get; set; }

    // ===== 会话匹配（AMB 是会话制，不是时间窗口制）=====

    /// <summary>
    /// 复用既有会话的作用窗口（小时）。这不是 Apple 的时间窗（AMB 无 48h/7d 窗口概念，
    /// 桥接也无），仅用于把同一客户归到既有会话；空闲结案由本系统/供应商排队规则决定。
    /// </summary>
    public int MatchWindowHours { get; set; } = 168;

    /// <summary>关联邮箱（0 = 自动取第一个启用邮箱）。</summary>
    public int MailboxId { get; set; }

    /// <summary>自动回复开关。</summary>
    public bool AutoReplyEnabled { get; set; }

    /// <summary>自动回复内容。</summary>
    [MaxLength(2000)]
    public string? AutoReplyMessage { get; set; }

    /// <summary>状态探测超时（秒）。</summary>
    public int StatusCheckTimeoutSeconds { get; set; } = 8;

    // ===== amb_msp（经已核准 MSP 接入 AMB）=====

    /// <summary>MSP 连接器标识（每家 MSP 一套接口，无统一 API）。</summary>
    [MaxLength(64)]
    public string? MspConnector { get; set; }

    /// <summary>MSP API 基址。</summary>
    [MaxLength(500)]
    public string? ApiBaseUrl { get; set; }

    /// <summary>MSP API Key / Bearer（加密存储）。</summary>
    [MaxLength(500)]
    public string? ApiKey { get; set; }

    /// <summary>校验 MSP 入站 HMAC 的密钥（加密存储）。</summary>
    [MaxLength(500)]
    public string? WebhookSecret { get; set; }

    /// <summary>本企业在 MSP 侧的业务标识。</summary>
    [MaxLength(200)]
    public string? BusinessIdentifier { get; set; }

    /// <summary>邀请消息的回落渠道：sms | rcs | none（Apple 对 Business Invitations 强制要求回落）。</summary>
    [MaxLength(16)]
    public string FallbackChannel { get; set; } = "none";

    // ===== amb_apple_direct（实验性：需 Apple 核准 MSP 资格）=====

    /// <summary>Apple 核准的 MSP ID（JWT 的 iss/aud）。</summary>
    [MaxLength(200)]
    public string? MspId { get; set; }

    /// <summary>HS256 共享密钥（加密存储；不存在 Apple 下发的 EC 密钥对，也不是 ES256）。</summary>
    [MaxLength(500)]
    public string? MspSharedSecret { get; set; }

    /// <summary>JWT 有效期（秒）。iat 超过 1 小时 Apple 直接拒 ⇒ 默认 3600、硬上限 3600，且不跨小时缓存。</summary>
    public int TokenTtlSeconds { get; set; } = 3600;

    /// <summary>Apple 网关基址（真实基址，不存在 api.apple-chat.com）。</summary>
    [MaxLength(500)]
    public string GatewayBaseUrl { get; set; } = "https://mspgw.push.apple.com";

    /// <summary>企业身份 urn:biz:&lt;uuid&gt;（Apple Business Register 下发）。</summary>
    [MaxLength(200)]
    public string? BusinessId { get; set; }

    /// <summary>入站路径（必须接受 gzip 请求体）。默认 /message。</summary>
    [MaxLength(200)]
    public string InboundPath { get; set; } = "/message";

    /// <summary>附件密钥位数（Apple 要求单次 256-bit key）。</summary>
    public int AttachmentKeySize { get; set; } = 256;

    /// <summary>入站请求体上限（字节）。</summary>
    public int MaxBodyBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>实验性 Apple 直连开关：必须显式置 true 才允许 amb_apple_direct（默认关闭）。</summary>
    public bool ExperimentalAppleDirect { get; set; }

    /// <summary>当前是否为 AMB 系列模式。</summary>
    public bool IsAmbMode =>
        string.Equals(Mode, IMessageModes.AmbMsp, StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(Mode, IMessageModes.AmbAppleDirect, StringComparison.OrdinalIgnoreCase)
         && ExperimentalAppleDirect);

    /// <summary>模式是否可用（amb_apple_direct 未开实验开关时视为不可用）。</summary>
    public bool IsModeActive()
    {
        if (string.Equals(Mode, IMessageModes.Bridge, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(Mode, IMessageModes.AmbMsp, StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(Mode, IMessageModes.AmbAppleDirect, StringComparison.OrdinalIgnoreCase)
               && ExperimentalAppleDirect;
    }

    /// <summary>当前模式的必填凭据是否齐备（不含 Enabled）。</summary>
    public bool IsConfigured
    {
        get
        {
            if (!IsModeActive()) return false;

            if (string.Equals(Mode, IMessageModes.Bridge, StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Password);

            if (string.Equals(Mode, IMessageModes.AmbMsp, StringComparison.OrdinalIgnoreCase))
                // WebhookSecret 是 AMB MSP 入站 HMAC 的唯一依据：缺它时入站无法验签，
                // 若仍报"已配置"，设置页会显示正常、发送路径也会放行，等于给伪造入站留半开门。
                // 故此处要求 ApiBaseUrl + BusinessIdentifier + WebhookSecret 齐全。
                // 注：发送侧凭据（ApiKey/MspSharedSecret）沿用原判定，未在此收紧，避免改动既有部署的配置口径。
                return !string.IsNullOrWhiteSpace(ApiBaseUrl)
                       && !string.IsNullOrWhiteSpace(BusinessIdentifier)
                       && !string.IsNullOrWhiteSpace(WebhookSecret);

            // amb_apple_direct
            return !string.IsNullOrWhiteSpace(GatewayBaseUrl)
                   && !string.IsNullOrWhiteSpace(MspId)
                   && !string.IsNullOrWhiteSpace(MspSharedSecret)
                   && !string.IsNullOrWhiteSpace(BusinessId);
        }
    }
}

/// <summary>传输模式常量（字符串键与前端 mode 选择器一致）。</summary>
public static class IMessageModes
{
    public const string Bridge = "bridge";
    public const string AmbMsp = "amb_msp";
    public const string AmbAppleDirect = "amb_apple_direct";
}

/// <summary>BlueBubbles 鉴权方式。</summary>
public static class IMessageAuthStyles
{
    public const string Bearer = "bearer";
    public const string Query = "query";
}

/// <summary>BlueBubbles 发送方式（apple-script 普通权限可用；private-api 需关闭 SIP）。</summary>
public static class IMessageSendMethods
{
    public const string AppleScript = "apple-script";
    public const string PrivateApi = "private-api";
    public const string Any = "any";
}
