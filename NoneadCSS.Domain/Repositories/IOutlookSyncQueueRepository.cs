using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// Outlook 同步重试队列仓储接口。
/// 任务 D#3：EnqueueAsync / DequeueBatchAsync / MarkDeadLetteredAsync / IncrementRetryAsync / MarkCompletedAsync。
/// </summary>
public interface IOutlookSyncQueueRepository : IRepository<OutlookSyncQueueItem>
{
    /// <summary>
    /// 加入重试队列：Status=Pending, NextRetryAt=null（立即）或指定时间。
    /// </summary>
    Task<OutlookSyncQueueItem> EnqueueAsync(OutlookSyncQueueItem item, bool autoSave = false, CancellationToken ct = default);

    /// <summary>
    /// 批量出队：取 Status == Pending 且 (NextRetryAt == null 或 NextRetryAt &lt;= now) 的前 batchSize 条，
    /// 同时将其标记为 Processing（使用 ExecuteUpdate 保证原子性并避免重复出队）。
    /// 返回已被标记为 Processing 的条目。
    /// </summary>
    Task<List<OutlookSyncQueueItem>> DequeueBatchAsync(int batchSize = 20, CancellationToken ct = default);

    /// <summary>
    /// 超过 MaxRetry 次数：标记为 DeadLettered。
    /// </summary>
    Task MarkDeadLetteredAsync(int id, string lastError, CancellationToken ct = default);

    /// <summary>
    /// 重试失败时：RetryCount + 1，NextRetryAt = Now + 2^RetryCount * 30s（指数退避），
    /// Status 若仍在 MaxRetry 范围内则为 Retrying，否则交由调用方 MarkDeadLettered。
    /// 返回是否仍可继续重试（false 表示已超限，应调用 MarkDeadLettered）。
    /// </summary>
    Task<bool> IncrementRetryAsync(int id, string lastError, CancellationToken ct = default);

    /// <summary>
    /// 标记完成（成功后直接删除或标记为 Completed；本实现按任务简化直接删除实体以避免表无限膨胀）。
    /// </summary>
    Task MarkCompletedAsync(int id, CancellationToken ct = default);
}
