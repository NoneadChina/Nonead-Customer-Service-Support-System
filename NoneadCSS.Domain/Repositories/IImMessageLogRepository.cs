using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// IM 消息发送状态常量
/// </summary>
public static class ImMessageLogStatus
{
    public const int Pending = 1;   // 待发送（含重试）
    public const int Sending = 2;   // 发送中（处理任务取出后的临时状态）
    public const int Sent = 3;      // 发送成功
    public const int Failed = 4;    // 最终失败（超过最大重试次数）

    public static bool IsSuccess(int status) => status == Sent;
    public static bool IsFailed(int status) => status == Failed;
    public static bool IsPending(int status) => status == Pending;
    public static bool IsSending(int status) => status == Sending;

    public static string GetLabel(int status) => status switch
    {
        Pending => "待发送",
        Sending => "发送中",
        Sent => "已发送",
        Failed => "最终失败",
        _ => "未知"
    };
}

/// <summary>
/// IM 消息发送日志仓储接口
/// </summary>
public interface IImMessageLogRepository : IRepository<ImMessageLog>
{
    /// <summary>
    /// 获取待发送的队列记录：状态为 Pending 且下次重试时间已到。
    /// 按 NextRetryAt 升序，限制每次处理数量。
    /// </summary>
    /// <param name="batchSize">每批最多取出的记录数，默认 100</param>
    Task<List<ImMessageLog>> GetPendingAsync(int batchSize = 100);

    /// <summary>
    /// 获取最近失败的发送记录
    /// </summary>
    Task<List<ImMessageLog>> GetRecentFailuresAsync(int count = 10);

    /// <summary>
    /// T3: 回收卡死的 Sending 记录（进程崩溃/异常遗留），重置为 Pending 以便下次重试。
    /// </summary>
    /// <param name="staleAfter">超过该时长仍处于 Sending 即视为卡死</param>
    Task<int> ResetStaleSendingAsync(TimeSpan staleAfter);
}
