using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IDashboardService
{
    Task<DashboardResponse> GetDashboardStatsAsync(int? mailboxId = null);
}
