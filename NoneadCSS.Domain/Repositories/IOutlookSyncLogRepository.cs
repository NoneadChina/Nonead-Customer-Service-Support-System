using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// Outlook 同步审计日志仓储。任务 D#2 要求接口：CreateAsync, ListByMailboxIdAsync, GetByIdAsync。
/// </summary>
public interface IOutlookSyncLogRepository : IRepository<OutlookSyncLog>
{
    /// <summary>
    /// 新增一条审计日志（即 AddAsync + SaveChanges 可选，默认仅 Add 到 ChangeTracker）。
    /// </summary>
    Task<OutlookSyncLog> CreateAsync(OutlookSyncLog log, bool autoSave = false, CancellationToken ct = default);

    /// <summary>
    /// 按 MailboxId 分页查询同步审计日志（按 StartedAt DESC）。
    /// </summary>
    Task<(IEnumerable<OutlookSyncLog> Items, int Total)> ListByMailboxIdAsync(
        int mailboxId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 按 ID 获取单条日志。
    /// </summary>
    Task<OutlookSyncLog?> GetByIdAsync(int id, CancellationToken ct = default);
}
