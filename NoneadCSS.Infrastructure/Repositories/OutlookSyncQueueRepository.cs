using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class OutlookSyncQueueRepository : Repository<OutlookSyncQueueItem>, IOutlookSyncQueueRepository
{
    public OutlookSyncQueueRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<OutlookSyncQueueItem> EnqueueAsync(OutlookSyncQueueItem item, bool autoSave = false, CancellationToken ct = default)
    {
        item.Status ??= "pending";
        item.CreatedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbSet.AddAsync(item, ct);
        if (autoSave)
            await _context.SaveChangesAsync(ct);
        return item;
    }

    public async Task<List<OutlookSyncQueueItem>> DequeueBatchAsync(int batchSize = 20, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // NOTE: EF Core SQLite provider does NOT translate DateTimeOffset comparisons
        // (direct nor via .ToUnixTimeMilliseconds()) into SQL.
        // Since queue size remains small by design (pending/retrying batches), pull
        // pending candidates to memory and filter/sort client-side before atomic update.
        var pendingItems = await _dbSet.AsNoTracking()
            .Where(x => x.Status == "pending")
            .ToListAsync(ct);

        var candidateIds = pendingItems
            .Where(x => x.NextRetryAt == null || x.NextRetryAt.Value <= now)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToList();

        if (candidateIds.Count == 0)
            return [];

        // 2. Use ExecuteUpdate to atomically mark as Processing (prevents concurrent double-consume)
        await _dbSet
            .Where(x => candidateIds.Contains(x.Id) && x.Status == "pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "processing")
                .SetProperty(x => x.UpdatedAt, now), ct);

        // 3. Reload the marked items
        var items = await _dbSet.AsNoTracking()
            .Where(x => candidateIds.Contains(x.Id) && x.Status == "processing")
            .ToListAsync(ct);

        return items;
    }

    public async Task MarkDeadLetteredAsync(int id, string lastError, CancellationToken ct = default)
    {
        await _dbSet
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "deadlettered")
                .SetProperty(x => x.LastError, lastError.Length > 4000 ? lastError[..4000] : lastError)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task<bool> IncrementRetryAsync(int id, string lastError, CancellationToken ct = default)
    {
        var item = await _dbSet.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item == null) return false;

        item.RetryCount += 1;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.LastError = lastError.Length > 4000 ? lastError[..4000] : lastError;

        if (item.RetryCount >= item.MaxRetry)
        {
            // 超过最大重试次数：调用方应调用 MarkDeadLetteredAsync
            item.Status = "failed";
            await _context.SaveChangesAsync(ct);
            return false;
        }

        // 指数退避：2^RetryCount * 30s
        var delaySeconds = (int)(Math.Pow(2, item.RetryCount) * 30);
        item.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
        item.Status = "retrying";
        // 下一轮扫描时 Retrying 不会自动出队；为简化处理，状态仍写回 pending + NextRetryAt
        item.Status = "pending";
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task MarkCompletedAsync(int id, CancellationToken ct = default)
    {
        // 完成后直接删除，避免队列无限膨胀
        var item = await _dbSet.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item != null)
        {
            _dbSet.Remove(item);
            await _context.SaveChangesAsync(ct);
        }
    }
}
