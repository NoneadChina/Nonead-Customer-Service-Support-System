using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// LINE Messaging API 客户端（api.line.me / api-data.line.me）。
/// 约定：所有方法不抛网络异常，失败以返回值 + 日志表达；成功响应体为空对象，LINE 不返回消息 id。
/// </summary>
public interface ILineClient
{
    /// <summary>
    /// 发送消息对象列表。replyToken 非空时优先用 reply 接口（不消耗月度配额），
    /// 令牌缺失/已用/过期时自动回落 push（消耗配额，且只对好友生效）。
    /// 超过 MaxMessagesPerRequest 会自动拆成多次请求；超出单条文本上限的文本由 SendTextAsync 分片。
    /// </summary>
    Task<LineSendResult> SendMessagesAsync(
        IReadOnlyList<LineMessageObject> messages,
        string? replyToken = null,
        string? toUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>发送纯文本（按 TextChunkChars 分片，逐批发送；返回首个失败原因）</summary>
    Task<LineSendResult> SendTextAsync(
        string text,
        string? replyToken = null,
        string? toUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>标记会话已读（聊天启用功能，需官方开通；未开通时返回 false 且不影响主流程）</summary>
    Task<bool> MarkAsReadAsync(string markAsReadToken, CancellationToken cancellationToken = default);

    /// <summary>显示"正在输入…"（/v2/bot/chat/loading/start）</summary>
    Task<bool> StartLoadingAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>读取客户资料；非好友或已屏蔽返回 null（LINE 返回 404）</summary>
    Task<LineProfile?> GetProfileAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>下载入站消息内容（api-data.line.me/v2/bot/message/{id}/content）。超上限或不可得返回 null。</summary>
    Task<LineContentResult?> DownloadContentAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>读取月度推送配额（reply 不计入消耗）</summary>
    Task<LineQuotaInfo?> GetQuotaAsync(CancellationToken cancellationToken = default);

    /// <summary>读取令牌信息（/v2/oauth/verify），失败返回 null</summary>
    Task<LineTokenInfo?> VerifyTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>读取 LINE 侧登记的 Webhook 回调地址</summary>
    Task<LineWebhookEndpointInfo?> GetWebhookEndpointAsync(CancellationToken cancellationToken = default);

    /// <summary>设置 LINE 侧 Webhook 回调地址（可编程配置）</summary>
    Task<bool> SetWebhookEndpointAsync(string webhookUrl, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>LINE 侧 Webhook 连通性自检（发送测试事件，属 60/小时限流族）</summary>
    Task<LineWebhookCheckResult> CheckWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default);

    /// <summary>用消息配额接口做轻量探活</summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>入站消息内容下载结果</summary>
public class LineContentResult
{
    public byte[] Content { get; set; } = [];
    public string? MimeType { get; set; }
    public string? FileName { get; set; }

    /// <summary>因超过 MaxInboundMediaBytes 而未下载</summary>
    public bool TooLarge { get; set; }
}

/// <summary>月度配额信息</summary>
public class LineQuotaInfo
{
    /// <summary>none / limited / unlimited（unlimited 时 Quota 为 -1）</summary>
    public string Type { get; set; } = "unknown";

    /// <summary>月度配额数；-1 表示未知或不限</summary>
    public int Quota { get; set; } = -1;

    /// <summary>本月已消耗（push/multicast/narrowcast/broadcast 按收件人计数，reply 不计）</summary>
    public int TotalUsage { get; set; } = -1;
}

/// <summary>访问令牌信息</summary>
public class LineTokenInfo
{
    public string? ClientId { get; set; }

    /// <summary>剩余有效秒数；长期令牌不返回 ⇒ null</summary>
    public int? ExpiresInSeconds { get; set; }

    /// <summary>scope，例如 "profile message"</summary>
    public string? Scope { get; set; }
}

/// <summary>LINE 侧 Webhook 端点配置</summary>
public class LineWebhookEndpointInfo
{
    public string? Endpoint { get; set; }
    public bool Active { get; set; }
}
