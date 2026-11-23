using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// iMessage / Apple Messages 管理服务 — 配置读写、连接状态查询、坐席回复、连通性测试。
/// 无「模板消息」概念：bridge 模式没有模板；AMB 的 Business Invitations 只能用 Apple 编写的模板，
/// 且需企业申请 + Apple 批准，故不在本渠道的常规发送路径内（见 docs/integration/imessage.md §3）。
/// </summary>
public interface IIMessageService
{
    /// <summary>获取当前 iMessage 配置（密钥字段脱敏，只回报是否已配置）。</summary>
    Task<IMessageSettingsDto> GetSettingsAsync();

    /// <summary>更新 iMessage 配置，持久化到 SystemOptions 表并刷新内存缓存。</summary>
    Task<IMessageSettingsDto> UpdateSettingsAsync(UpdateIMessageSettingsRequest request);

    /// <summary>获取连接状态（是否已配置、是否启用、桥接服务器可达性、会话窗口状态）。</summary>
    Task<IMessageStatusDto> GetStatusAsync();

    /// <summary>回复客户消息（bridge：走 BlueBubbles；AMB：会话未关闭期间可回，绝不冷启动私聊）。</summary>
    Task<IMessageReplyResult> ReplyAsync(IMessageReplyRequest request, int userId);

    /// <summary>测试连通性（bridge：/api/v1/ping + 向指定 handle 发一条测试文本；amb：签发 JWT 并探测网关）。</summary>
    Task<IMessageTestResult> TestConnectionAsync(string? testHandle = null);

    /// <summary>
    /// 向 BlueBubbles 服务器登记本系统的 webhook 接收地址（POST /api/v1/server/webhook）。
    /// 仅 bridge 模式有意义；AMB 的入站由 Apple/MSP 侧配置。
    /// </summary>
    Task<IMessageTestResult> RegisterWebhookAsync();
}

/// <summary>iMessage 回复结果</summary>
public class IMessageReplyResult
{
    /// <summary>消息是否发送成功</summary>
    public bool Sent { get; set; }

    /// <summary>线程记录是否创建成功（已发出但落库失败时为 false）</summary>
    public bool ThreadCreated { get; set; } = true;

    /// <summary>创建的回复线程 ID</summary>
    public int? ThreadId { get; set; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 未发出的附件清单（文件名；请求里给了 id 但库里查不到的附件按 <c>#id</c> 记）。
    /// 仅在附件有失败时非空：正文已送达时 Sent 仍为 true，调用方据此提示坐席哪些附件没发出去。
    /// </summary>
    public List<string>? FailedAttachments { get; set; }
}

/// <summary>iMessage 连接状态 DTO</summary>
public class IMessageStatusDto
{
    public bool Configured { get; set; }
    public bool Enabled { get; set; }

    /// <summary>当前传输模式：bridge / amb_msp / amb_apple_direct</summary>
    public string Mode { get; set; } = "bridge";

    /// <summary>amb_apple_direct 需要显式实验开关才可用（当前开关状态）</summary>
    public bool AppleDirectAllowed { get; set; }

    /// <summary>桥接服务器 / MSP 网关是否可达</summary>
    public bool ApiReachable { get; set; }

    /// <summary>本系统对外回调地址（webhook）</summary>
    public string? CallbackUrl { get; set; }

    /// <summary>
    /// 会话可回复状态：ok（存在未结案会话）/ expired（会话已过期或已关闭）/ unknown（无会话）。
    /// 注意：AMB 是会话制而非时间窗口制，此值只反映本系统的会话状态。
    /// </summary>
    public string WindowStatus { get; set; } = "unknown";

    /// <summary>距最近一条客户消息的小时数（无会话为 0）</summary>
    public int HoursSinceCustomerMessage { get; set; }

    /// <summary>最近一条客户消息时间</summary>
    public DateTime? LastCustomerMessageAt { get; set; }

    /// <summary>探测到的 BlueBubbles 服务器版本（低于 MinServerVersion 时前端告警）</summary>
    public string? ServerVersion { get; set; }

    /// <summary>已登记的 webhook 地址（bridge 模式，来自 /api/v1/push 列表；未配置为 null）</summary>
    public string? RegisteredWebhookUrl { get; set; }
}

/// <summary>iMessage 测试连接结果</summary>
public class IMessageTestResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>探测到的服务器版本（bridge 模式）</summary>
    public string? ServerVersion { get; set; }
}
