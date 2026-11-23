using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IWebhookRepository : IRepository<Webhook>
{
    /// <summary>
    /// 获取某个事件触发的所有启用中的 Webhook（按邮箱 + 全局匹配，AsNoTracking，仅用于读取）。
    /// </summary>
    Task<List<Webhook>> GetActiveForEventAsync(string eventType, int? mailboxId = null);

    /// <summary>
    /// 原子递增 Webhook 触发统计（TriggerCount/SuccessCount/FailCount/LastTriggeredAt/LastError）。
    /// 使用 ExecuteUpdateAsync 直发 UPDATE ... SET 计数 = 计数 + 1，
    /// 不经过 ChangeTracker、不校验 RowVersion，避免并发触发时 DbUpdateConcurrencyException
    /// 导致调用方（工作流 trigger_webhook 动作）整体回滚。
    /// </summary>
    Task IncrementTriggerStatsAsync(int webhookId, bool isSuccess, string? errorMessage, CancellationToken cancellationToken = default);

    Task<(IEnumerable<Webhook> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? mailboxId = null, bool? isActive = null, string? keyword = null, string? eventType = null);

    Task<List<Webhook>> GetAllWithMailboxAsync();
    Task<Dictionary<string, int>> GetStatsAsync();

    Task<Webhook?> GetDeletedByIdAsync(int id);
    Task<(IEnumerable<Webhook> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}

public interface IWebhookLogRepository : IRepository<WebhookLog>
{
    Task<List<WebhookLog>> GetByWebhookAsync(int webhookId, int limit = 50);

    Task<(IEnumerable<WebhookLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? webhookId = null, string? eventType = null, bool? isSuccess = null);

    Task<Dictionary<string, int>> GetStatsAsync(DateTime? start = null, DateTime? end = null);
}
