using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IModuleService
{
    Task<ModuleOverviewResponse> GetOverviewAsync();
    Task<(List<ModuleDto> Items, int TotalCount)> GetPagedAsync(ModuleQuery query);
    Task<List<ModuleDto>> GetAllAsync();
    Task<ModuleDto?> GetByNameAsync(string name);
    Task<List<ModuleDto>> GetByCategoryAsync(string category);
    Task<List<ModuleDto>> GetActiveAsync();
    Task<ModuleDto?> CreateAsync(CreateModuleRequest request);
    Task<ModuleDto?> UpdateAsync(string name, UpdateModuleRequest request);
    Task<bool> DeleteAsync(string name);
    Task<ModuleDto?> ToggleActiveAsync(string name);
}
