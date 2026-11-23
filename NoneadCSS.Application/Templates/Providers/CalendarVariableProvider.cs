using NoneadCSS.Application.Configuration;
using Microsoft.Extensions.Options;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// 日历事件变量提供者 — event_title, event_start_time, event_location 等
/// </summary>
public class CalendarVariableProvider : ITemplateVariableProvider
{
    private readonly BrandOptions _brand;

    public int Order => 70;

    public CalendarVariableProvider(IOptions<BrandOptions> brand)
    {
        _brand = brand.Value;
    }

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        var vars = new Dictionary<string, string>();

        if (context.CalendarEvent is CalendarEvent evt)
        {
            vars["event_title"] = evt.Title ?? string.Empty;
            vars["event_type"] = evt.EventType ?? string.Empty;
            vars["event_start_time"] = evt.StartAt.ToString("yyyy-MM-dd HH:mm");
            vars["event_end_time"] = evt.EndAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
            vars["event_location"] = evt.Location ?? string.Empty;
            vars["event_meeting_url"] = evt.MeetingUrl ?? string.Empty;
            vars["event_description"] = evt.Description ?? string.Empty;
            vars["event_priority"] = evt.Priority?.ToString() ?? string.Empty;
            vars["event_url"] = $"{_brand.FrontendUrl}/calendar/events/{evt.Id}";
        }

        return Task.FromResult(vars);
    }
}
