#nullable enable
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services.Calendar;

public interface ICalendarQueryService
{
    Task<(List<CalendarEventDto> Items, int Total)> GetListAsync(CalendarEventQuery query, CancellationToken ct = default);
    Task<CalendarEventDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Domain.Entities.CalendarEvent?> GetByIdTrackedEntityAsync(int id, CancellationToken ct = default);
    Task<Domain.Entities.CalendarEvent?> GetByIdTrackedEntityAsync(int id, bool includeDeleted, CancellationToken ct = default);
    Task<CalendarOverviewResponse> GetOverviewAsync(CancellationToken ct = default);
    Task<(List<CalendarEventDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword, CancellationToken ct = default);
    Task<int> GetTrashCountAsync(CancellationToken ct = default);
}
