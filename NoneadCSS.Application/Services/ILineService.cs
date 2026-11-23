using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// LINE 渠道业务服务：设置读写、状态自检、客服回复、人工接管与连通性测试。
/// </summary>
public interface ILineService
{
    Task<LineSettingsDto> GetSettingsAsync();

    Task<LineSettingsDto> UpdateSettingsAsync(UpdateLineSettingsRequest request);

    Task<LineStatusDto> GetStatusAsync();

    /// <summary>客服回复：优先使用最近的 replyToken（不耗配额），过期自动回落 push（耗配额、需好友）</summary>
    Task<LineReplyResult> ReplyAsync(LineReplyRequest request, int userId);

    /// <summary>设置页主动 push 测试（消耗配额）</summary>
    Task<LineSendResult> PushTestAsync(LinePushTestRequest request);

    /// <summary>Webhook 连通性自检（LINE 侧发起测试事件，属 60/小时限流族）</summary>
    Task<LineWebhookCheckResult> CheckWebhookAsync(LineWebhookCheckRequest request);

    /// <summary>把本系统回调地址登记到 LINE 并开关 webhook</summary>
    Task<LineWebhookCheckResult> ApplyWebhookEndpointAsync(bool enabled);

    /// <summary>人工接管：机器人自动回复静默一段时间（LINE 无会话接管 API，用系统内标记实现）</summary>
    Task<bool> SetHumanTakeoverAsync(LineTakeoverRequest request, int userId);

    /// <summary>结束人工接管，恢复机器人自动回复</summary>
    Task<bool> ReleaseHumanTakeoverAsync(int conversationId, int userId);

    /// <summary>轻量探活</summary>
    Task<LineTestResult> TestConnectionAsync();
}

/// <summary>客服回复结果</summary>
public class LineReplyResult
{
    public bool Sent { get; set; }

    /// <summary>消息已发出但会话线程写入失败（HTTP 207 语义）</summary>
    public bool ThreadCreated { get; set; } = true;

    public int? ThreadId { get; set; }

    /// <summary>实际发送方式：reply / push</summary>
    public string Via { get; set; } = "push";

    /// <summary>分片/请求数</summary>
    public int ChunkCount { get; set; }

    /// <summary>本次是否触发 replyToken → push 回落</summary>
    public bool FallbackFromReplyToken { get; set; }

    /// <summary>目标不可达（未添加好友 / 已屏蔽 / 配额）⇒ 会话已被标记为不可达</summary>
    public bool Unreachable { get; set; }

    public List<string> MediaErrors { get; set; } = [];

    public string? ErrorMessage { get; set; }
}
