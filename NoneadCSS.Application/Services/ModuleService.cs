using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public class ModuleService : IModuleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration;
    private const string AllModulesCacheKey = "Module:All";
    private const string CoreModuleLockedMessage = "核心模块无法禁用/删除";
    private const string CoreModuleMustBeActiveMessage = "核心模块必须处于启用状态";

    public ModuleService(IUnitOfWork unitOfWork, IMemoryCache cache, IOptions<CacheOptions> cacheOptions)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.DefaultCacheMinutes);
    }

    /// <summary>
    /// 使模块缓存失效（在创建、更新、删除、切换激活状态时调用）
    /// </summary>
    private void InvalidateModuleCache()
    {
        _cache.Remove(AllModulesCacheKey);
    }

    public async Task<ModuleOverviewResponse> GetOverviewAsync()
    {
        var all = await GetAllModulesCachedAsync();
        // 核心模块永远视为已启用，overview 统计保持正确
        static bool EffectiveActive(Module m) => m.IsSystemModule || m.IsActive;
        var active = all.Where(EffectiveActive).ToList();
        var categories = all
            .GroupBy(m => m.Category)
            .Select(g => new ModuleCategoryStats
            {
                Category = g.Key,
                CategoryLabel = GetCategoryLabel(g.Key),
                Count = g.Count(),
                ActiveCount = g.Count(EffectiveActive)
            })
            .OrderBy(c => c.Category)
            .ToList();

        return new ModuleOverviewResponse
        {
            Total = all.Count,
            Active = active.Count,
            Inactive = all.Count - active.Count,
            Categories = categories
        };
    }

    public async Task<(List<ModuleDto> Items, int TotalCount)> GetPagedAsync(ModuleQuery query)
    {
        var (items, total) = await _unitOfWork.Modules.GetPagedAsync(
            query.Category, query.IsActive, query.Search, query.Page, query.PageSize);
        return (items.Select(m => m.ToDto()).ToList(), total);
    }

    public async Task<List<ModuleDto>> GetAllAsync()
    {
        var modules = await GetAllModulesCachedAsync();
        return modules.Select(m => m.ToDto()).ToList();
    }

    public async Task<ModuleDto?> GetByNameAsync(string name)
    {
        var module = await _unitOfWork.Modules.GetByNameAsync(name);
        return module?.ToDto();
    }

    public async Task<List<ModuleDto>> GetByCategoryAsync(string category)
    {
        var modules = await GetAllModulesCachedAsync();
        return modules.Where(m => string.Equals(m.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.ToDto())
            .ToList();
    }

    public async Task<List<ModuleDto>> GetActiveAsync()
    {
        var modules = await GetAllModulesCachedAsync();
        // 核心模块永远被视为已启用，即使 DB 中被误改
        return modules.Where(m => m.IsSystemModule || m.IsActive).Select(m => m.ToDto()).ToList();
    }

    public async Task<ModuleDto?> CreateAsync(CreateModuleRequest request)
    {
        if (await _unitOfWork.Modules.ExistsByNameAsync(request.Name))
        {
            throw new InvalidOperationException($"模块 '{request.Name}' 已存在");
        }

        var module = new Module
        {
            Name = request.Name,
            Title = request.Title,
            Description = request.Description,
            Version = request.Version,
            Author = request.Author,
            Icon = request.Icon,
            IsActive = false,
            Category = request.Category,
            Settings = request.Settings,
            SortOrder = request.SortOrder
        };

        var created = await _unitOfWork.Modules.AddAsync(module);
        await _unitOfWork.SaveChangesAsync();
        InvalidateModuleCache();
        return created.ToDto();
    }

    public async Task<ModuleDto?> UpdateAsync(string name, UpdateModuleRequest request)
    {
        var updated = await _unitOfWork.Modules.UpdateAsync(name, new Module
        {
            Title = request.Title,
            Description = request.Description,
            Version = request.Version,
            Author = request.Author,
            Icon = request.Icon,
            Category = request.Category,
            Settings = request.Settings,
            SortOrder = request.SortOrder
        });
        await _unitOfWork.SaveChangesAsync();
        InvalidateModuleCache();
        return updated?.ToDto();
    }

    public async Task<bool> DeleteAsync(string name)
    {
        // 核心模块：永远不能被删除
        if (string.Equals(name, Module.CoreModuleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(CoreModuleLockedMessage);
        }

        var result = await _unitOfWork.Modules.DeleteAsync(name);
        if (result)
        {
            await _unitOfWork.SaveChangesAsync();
            InvalidateModuleCache();
        }
        return result;
    }

    public async Task<ModuleDto?> ToggleActiveAsync(string name)
    {
        // 核心模块：永远不能被关闭（打开也直接返回已启用状态，保持幂等）
        if (string.Equals(name, Module.CoreModuleName, StringComparison.OrdinalIgnoreCase))
        {
            // 确保 DB 中也永远是 true（防历史数据被误改）
            var core = await _unitOfWork.Modules.GetByNameAsync(name);
            if (core != null && !core.IsActive)
            {
                await _unitOfWork.Modules.SetActiveAsync(core.Id, true);
                await _unitOfWork.SaveChangesAsync();
                InvalidateModuleCache();
            }
            return core?.ToDto();
        }

        var module = await _unitOfWork.Modules.ToggleActiveAsync(name);
        if (module != null)
        {
            await _unitOfWork.SaveChangesAsync();
            InvalidateModuleCache();
        }
        return module?.ToDto();
    }

    /// <summary>
    /// 从 IMemoryCache 获取所有模块（10 分钟缓存），未命中时查数据库。
    /// ModuleAuthorizationFilter 每请求都会调用 GetAllAsync，缓存可显著降低数据库压力。
    /// </summary>
    private async Task<List<Module>> GetAllModulesCachedAsync()
    {
        if (_cache.TryGetValue(AllModulesCacheKey, out List<Module>? cached) && cached != null)
        {
            return cached;
        }

        var modules = (await _unitOfWork.Modules.GetAllAsync()).ToList();
        _cache.Set(AllModulesCacheKey, modules, _cacheDuration);
        return modules;
    }

    private static string GetCategoryLabel(string category)
    {
        return category switch
        {
            "integration" => "集成",
            "communication" => "通讯",
            "reports" => "报表",
            "tools" => "工具",
            "general" => "通用",
            "automation" => "自动化",
            _ => category
        };
    }
}
