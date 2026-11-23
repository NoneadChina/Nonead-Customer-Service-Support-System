using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IImportExportService
{
    Task<List<EntityTypeResponse>> GetAvailableEntitiesAsync();
    Task<byte[]> ExportDataAsync(ExportRequest request);
    Task<ImportPreviewResponse> PreviewImportAsync(ImportPreviewRequest request);
    Task<ImportResponse> ImportDataAsync(ImportRequest request);
}
