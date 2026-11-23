using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 日历事件服务接口
/// </summary>
public interface ICalendarService
{
    Task<(List<CalendarEventDto> Items, int Total)> GetListAsync(CalendarEventQuery query);
    Task<CalendarEventDto?> GetByIdAsync(int id);
    Task<CalendarEventDto> CreateAsync(CreateCalendarEventRequest request);
    Task<CalendarEventDto?> UpdateAsync(int id, UpdateCalendarEventRequest request);
    Task<bool> DeleteAsync(int id, string deleteMode = "single", string? ifMatch = null);
    Task<bool> UpdateStatusAsync(int id, string status, string? ifMatch = null);
    Task<CalendarOverviewResponse> GetOverviewAsync();

    // 软删除
    Task<(List<CalendarEventDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword);
    Task<int> GetTrashCountAsync();
    Task<bool> RestoreAsync(int id, string? ifMatch = null);
    Task<bool> HardDeleteAsync(int id, string? ifMatch = null);
    Task<int> ClearTrashAsync();
    Task<int> PurgeTrashAsync(int retentionDays = 30);

    // 提醒
    Task<int> ScanAndSendRemindersAsync();

    // P1-2: 批量操作
    Task<CalendarBatchOperationResponse> BatchOperationAsync(CalendarBatchOperationRequest request);
}

/// <summary>
/// 日历事件模板服务接口（P1-3）
/// </summary>
public interface ICalendarEventTemplateService
{
    Task<(List<CalendarEventTemplateDto> Items, int Total)> GetListAsync(int? mailboxId = null, bool includeShared = true);
    Task<CalendarEventTemplateDto?> GetByIdAsync(int id);
    Task<CalendarEventTemplateDto> CreateAsync(CreateCalendarEventTemplateRequest request);
    Task<CalendarEventTemplateDto?> UpdateAsync(int id, UpdateCalendarEventTemplateRequest request);
    Task<bool> DeleteAsync(int id);
}
