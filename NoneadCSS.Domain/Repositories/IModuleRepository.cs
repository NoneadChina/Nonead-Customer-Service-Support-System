using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IModuleRepository
{
    Task<IEnumerable<Module>> GetAllAsync();
    Task<Module?> GetByIdAsync(int id);
    Task<Module?> GetByNameAsync(string name);
    Task<List<Module>> GetByCategoryAsync(string category);
    Task<List<Module>> GetActiveAsync();
    Task<(List<Module> Items, int TotalCount)> GetPagedAsync(string? category, bool? isActive, string? search, int page, int pageSize);
    Task<Module> AddAsync(Module module);
    Task<Module?> UpdateAsync(string name, Module module);
    Task<bool> DeleteAsync(string name);
    Task<bool> ExistsByNameAsync(string name);
    Task<Module?> ToggleActiveAsync(string name);
    /// <summary>
    /// 显式设置模块的启用状态（用于核心模块的强制启用修复）。
    /// 返回更新后的模块实体，如果未找到返回 null。
    /// </summary>
    Task<Module?> SetActiveAsync(int id, bool isActive);
}
