using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ICustomReportScheduleRepository : IRepository<CustomReportSchedule>
{
    Task<List<CustomReportSchedule>> GetByReportIdAsync(int reportId);
    Task<List<CustomReportSchedule>> GetPendingScansAsync(DateTime now, int take = 100);
}
