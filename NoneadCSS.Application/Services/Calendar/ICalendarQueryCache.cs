#nullable enable
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services.Calendar;

public interface ICalendarQueryCache
{
    Task<(List<CalendarEventDto> Items, int Total)> GetListAsync(
        int currentUserId,
        CalendarEventQuery query,
        Func<Task<(List<CalendarEventDto> Items, int Total)>> factory,
        CancellationToken ct = default);

    Task<CalendarOverviewResponse> GetOverviewAsync(
        int currentUserId,
        Func<Task<CalendarOverviewResponse>> factory,
        CancellationToken ct = default);

    void InvalidateAllForUser(int userId);
}
