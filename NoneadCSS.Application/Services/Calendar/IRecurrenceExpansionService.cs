#nullable enable
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services.Calendar;

public interface IRecurrenceExpansionService
{
    DateTime GetNextOccurrenceStart(CalendarEvent evt, DateTime now, DateTime scanWindow);
    List<CalendarEventDto> ExpandSeries(List<CalendarEventDto> events, DateTime startDate, DateTime endDate);
}
