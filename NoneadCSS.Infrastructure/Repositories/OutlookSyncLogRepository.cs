using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class OutlookSyncLogRepository : Repository<OutlookSyncLog>, IOutlookSyncLogRepository
{
    public OutlookSyncLogRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<OutlookSyncLog> CreateAsync(OutlookSyncLog log, bool autoSave = false, CancellationToken ct = default)
    {
        await _dbSet.AddAsync(log, ct);
        if (autoSave)
            await _context.SaveChangesAsync(ct);
        return log;
    }

    public async Task<(IEnumerable<OutlookSyncLog> Items, int Total)> ListByMailboxIdAsync(
        int mailboxId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _dbSet.AsNoTracking()
            .Where(x => x.MailboxId == mailboxId)
            .OrderByDescending(x => x.StartedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public override async Task<OutlookSyncLog?> GetByIdAsync(int id)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    }

    async Task<OutlookSyncLog?> IOutlookSyncLogRepository.GetByIdAsync(int id, CancellationToken ct)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }
}
