using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class ImMessageLogRepository : Repository<ImMessageLog>, IImMessageLogRepository
{
    public ImMessageLogRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<List<ImMessageLog>> GetPendingAsync(int batchSize = 100)
    {
        var now = DateTime.UtcNow;
        // 待发送（含重试）：状态为 Pending 且（NextRetryAt 为空或已到期）
        // 使用 OrderBy 确保 Skip/Take 结果可预测
        return await _dbSet
            .Where(s => s.Status == ImMessageLogStatus.Pending
                        && (s.NextRetryAt == null || s.NextRetryAt <= now))
            .OrderBy(s => s.NextRetryAt ?? s.CreatedAt)
            .ThenBy(s => s.Id)
            .Take(batchSize)
            .ToListAsync();
    }

    public async Task<List<ImMessageLog>> GetRecentFailuresAsync(int count = 10)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(s => s.Status == ImMessageLogStatus.Failed)
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> ResetStaleSendingAsync(TimeSpan staleAfter)
    {
        var cutoff = DateTime.UtcNow.Subtract(staleAfter);
        // T3: 进程崩溃/异常遗留的 Sending 记录回收为 Pending，避免消息永久丢失
        return await _dbSet
            .Where(s => s.Status == ImMessageLogStatus.Sending
                        && s.UpdatedAt != null
                        && s.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, ImMessageLogStatus.Pending)
                .SetProperty(s => s.NextRetryAt, DateTime.UtcNow)
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow));
    }
}
