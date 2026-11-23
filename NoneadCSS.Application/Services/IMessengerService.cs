using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Messenger 管理服务 - 配置读写、连接状态查询、坐席回复（含 24h 窗口与 message_tag）、
/// sender_action 转发、Webhook 字段订阅。
/// 与 WhatsApp 的差异：无模板消息概念（窗口外主动回复用 message_tag=HUMAN_AGENT），
/// Page Access Token 无刷新端点（到期需人工换发，状态页须报出 190 失效时间）。
/// </summary>
public interface IMessengerService
{
    /// <summary>获取当前 Messenger 配置（敏感字段脱敏，只回报 HasXxx）。</summary>
    Task<MessengerSettingsDto> GetSettingsAsync();

    /// <summary>更新 Messenger 配置，持久化到 SystemOptions 数据库表并刷新内存缓存。</summary>
    Task<MessengerSettingsDto> UpdateSettingsAsync(UpdateMessengerSettingsRequest request);

    /// <summary>获取 Messenger 连接状态（是否已配置、是否已启用、Graph API 可达性、24h 窗口状态、令牌失效时间）。</summary>
    Task<MessengerStatusDto> GetStatusAsync();

    /// <summary>
    /// 通过 Graph Send API 回复客户消息。
    /// 窗口外（默认 24h）按 MessageTagPolicy 处理：auto=带 message_tag 发送，none=拒绝并提示坐席。
    /// 文本按 TextChunkChars（默认 2000）分片，逐片串行发送（同 PSID 不并发，规避 613 限流）。
    /// </summary>
    Task<MessengerReplyResult> ReplyAsync(MessengerReplyRequest request, int userId);

    /// <summary>转发坐席输入状态/已读标记（sender_action：typing_on / typing_off / mark_seen）。</summary>
    Task<bool> SendSenderActionAsync(int conversationId, string action);

    /// <summary>订阅 Webhook 字段（POST /{ver}/me/subscribed_apps）；需 pages_manage_metadata 权限。</summary>
    Task<MessengerSubscribeResult> SubscribeFieldsAsync(string? fields = null);

    /// <summary>测试连接 — 探测 Graph API 可达性并校验 Page Access Token 有效（不向客户发送消息）。</summary>
    Task<MessengerTestResult> TestConnectionAsync();
}

/// <summary>Messenger 回复结果</summary>
public class MessengerReplyResult
{
    /// <summary>消息是否发送成功（分片场景指至少第一片成功；部分片失败见 ErrorMessage）</summary>
    public bool Sent { get; set; }

    /// <summary>线程记录是否创建成功（消息已发送但线程记录失败时为 false）</summary>
    public bool ThreadCreated { get; set; } = true;

    /// <summary>创建的回复线程 ID</summary>
    public int? ThreadId { get; set; }

    /// <summary>出站消息 mid（首片）</summary>
    public string? MessageId { get; set; }

    /// <summary>本次实际发送的分片数</summary>
    public int ChunkCount { get; set; }

    /// <summary>本次是否带 message_tag 发送（窗口外主动回复）</summary>
    public bool MessageTagApplied { get; set; }

    /// <summary>错误信息（含 Graph code 与 fbtrace_id，便于报障）</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Messenger 订阅字段结果</summary>
public class MessengerSubscribeResult
{
    public bool Success { get; set; }
    public string? Fields { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Messenger 测试连接结果</summary>
public class MessengerTestResult
{
    public bool Success { get; set; }
    public string? PageId { get; set; }
    public string? ApiVersion { get; set; }

    /// <summary>最近一次 Graph 错误（code 190 表示令牌失效，需人工换发）</summary>
    public MessengerApiError? Error { get; set; }

    public string? ErrorMessage { get; set; }
}
