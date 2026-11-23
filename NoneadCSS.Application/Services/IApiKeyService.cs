using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IApiKeyService
{
    Task<ApiKeyOverviewResponse> GetOverviewAsync();
    Task<(List<ApiKeyDto> Items, int TotalCount)> GetPagedAsync(ApiKeyQuery query);
    Task<ApiKeyDto?> GetByIdAsync(int id);
    Task<ApiKeyDto> CreateAsync(CreateApiKeyRequest request);
    Task<ApiKeyDto?> UpdateAsync(UpdateApiKeyRequest request, string? ifMatch = null);
    Task<bool> DeleteAsync(int id);
    Task<ApiKeyDto?> ToggleActiveAsync(int id, string? ifMatch = null);
    Task<ApiKeyDto?> RegenerateKeyAsync(int id, string? ifMatch = null);
    Task<List<ApiKeyLogDto>> GetLogsAsync(int apiKeyId, int limit = 50);
    Task<(List<ApiKeyLogDto> Items, int TotalCount)> GetLogsPagedAsync(ApiKeyLogQuery query);
    Task<(List<ApiKeyDto> Items, int TotalCount)> GetTrashPagedAsync(int page, int pageSize, string? keyword);
    Task<ApiKeyDto?> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();
}
