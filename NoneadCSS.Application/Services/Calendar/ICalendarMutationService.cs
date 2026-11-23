#nullable enable
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services.Calendar;

public interface ICalendarMutationService
{
    Task<CalendarEventDto> CreateAsync(CreateCalendarEventRequest request, CancellationToken ct = default);
    Task<CalendarEventDto?> UpdateAsync(int id, UpdateCalendarEventRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, string deleteMode = "single", string? ifMatch = null, CancellationToken ct = default);
    Task<bool> UpdateStatusAsync(int id, string status, string? ifMatch = null, CancellationToken ct = default);
    Task<bool> RestoreAsync(int id, string? ifMatch = null, CancellationToken ct = default);
    Task<int> HardDeleteAsync(int id, CalendarEvent? existingEntity, string? ifMatch = null, CancellationToken ct = default);
    Task<int> ClearTrashAsync(CancellationToken ct = default);
    Task<int> PurgeTrashAsync(int retentionDays = 0, CancellationToken ct = default);
    Task<int> ConfirmReminderAsync(int eventId, int currentUserId, bool isAdmin, string mode, string? ifMatch = null, CancellationToken ct = default);
    /// <summary>P1-2 批量操作：delete / setStatus / setEventType / moveOffset。</summary>
    Task<CalendarBatchOperationResponse> BatchOperationAsync(CalendarBatchOperationRequest request, CancellationToken ct = default);

    /// <summary>复制日历事件：基于已存在事件创建新事件，可选调整时间/标题。</summary>
    Task<CalendarEventDto> DuplicateAsync(int id, DuplicateCalendarEventRequest? request = null, CancellationToken ct = default);

    /// <summary>快速更新：仅更新指定字段（status/priority/progress/location 等），其余保持不变。</summary>
    Task<CalendarEventDto?> QuickUpdateAsync(int id, QuickUpdateCalendarEventRequest request, CancellationToken ct = default);
}
