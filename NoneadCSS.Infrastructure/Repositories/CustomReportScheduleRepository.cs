using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class CustomReportScheduleRepository : Repository<CustomReportSchedule>, ICustomReportScheduleRepository
{
    public CustomReportScheduleRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public override async Task<CustomReportSchedule?> GetByIdTrackedAsync(int id)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<List<CustomReportSchedule>> GetByReportIdAsync(int reportId)
    {
        return await _dbSet.AsNoTracking()
            .Where(r => r.CustomReportId == reportId && r.DeletedAt == null)
            .OrderBy(r => r.Id)
            .ToListAsync();
    }

    public async Task<List<CustomReportSchedule>> GetPendingScansAsync(DateTime now, int take = 100)
    {
        return await _dbSet.AsNoTracking()
            .Where(r =>
                r.IsActive == true &&
                r.NextRunAt != null &&
                r.NextRunAt <= now &&
                r.DeletedAt == null)
            .OrderBy(r => r.NextRunAt)
            .Take(take)
            .ToListAsync();
    }
}
