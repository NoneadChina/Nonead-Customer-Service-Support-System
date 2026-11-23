using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ICustomReportService
{
    Task<(List<CustomReportDto> Items, int TotalCount)> GetPagedAsync(
        string? search, string? category, string? visibility, int page, int pageSize);
    Task<List<CustomReportDto>> GetPinnedAsync(int limit);
    Task<CustomReportDto?> GetByIdAsync(int id);
    Task<CustomReportDto> CreateAsync(CreateCustomReportRequest req);
    Task<CustomReportDto> UpdateAsync(int id, UpdateCustomReportRequest req);
    Task DeleteAsync(int id);
    Task<(List<CustomReportDto> Items, int TotalCount)> GetTrashPagedAsync(
        string? search, int page, int pageSize);
    Task<CustomReportDto?> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();
    Task<CustomReportDto> ToggleDashboardPinAsync(int id, bool pinned, int? sortOrder);
    Task<CustomReportExecutionResultDto> ExecuteAsync(int id);
    Task<CustomReportExecutionResultDto> ExecutePreviewAsync(CreateCustomReportRequest req);
    Task<(byte[] Content, string ContentType, string FileName)> ExportAsync(int id, string format);
    Task<List<CustomReportScheduleDto>> ListSchedulesAsync(int reportId);
    Task<CustomReportScheduleDto> CreateScheduleAsync(int reportId, CreateScheduleRequest req);
    Task<CustomReportScheduleDto> UpdateScheduleAsync(int scheduleId, UpdateScheduleRequest req);
    Task DeleteScheduleAsync(int scheduleId);
    Task ProcessScheduledReportsAsync(DateTime now);
}
