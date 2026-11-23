using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ITimeEntryService
{
    Task<(List<TimeEntryDto> Items, int Total)> GetListAsync(TimeEntryQuery query);
    Task<TimeEntryDto?> GetByIdAsync(int id);
    Task<TimeEntryDto> CreateAsync(CreateTimeEntryRequest request);
    Task<TimeEntryDto?> UpdateAsync(int id, UpdateTimeEntryRequest request, string? ifMatch = null);
    Task<bool> DeleteAsync(int id);
    Task<TimeEntryDto> StartTimerAsync(StartTimerRequest request);
    Task<TimeEntryDto?> StopTimerAsync(int id, string? ifMatch = null);
    Task<TimeEntryDto?> GetActiveTimerAsync();
    Task<TimeTrackingOverviewResponse> GetOverviewAsync();
    Task<TimeTrackingReportResponse> GetReportAsync(DateTime? startDate, DateTime? endDate);
    Task<byte[]> ExportAsync(TimeEntryQuery query);
    Task<(List<TimeEntryDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword);
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();
}
