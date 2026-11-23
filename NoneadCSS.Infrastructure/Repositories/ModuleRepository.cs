using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class ModuleRepository : Repository<Module>, IModuleRepository
{
    public ModuleRepository(NoneadCSSDbContext context) : base(context) { }

    public override async Task<IEnumerable<Module>> GetAllAsync()
    {
        return await _dbSet
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<Module?> GetByNameAsync(string name)
    {
        return await _dbSet.FirstOrDefaultAsync(m => m.Name == name);
    }

    public async Task<List<Module>> GetByCategoryAsync(string category)
    {
        return await _dbSet
            .Where(m => m.Category == category)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<List<Module>> GetActiveAsync()
    {
        return await _dbSet
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<(List<Module> Items, int TotalCount)> GetPagedAsync(
        string? category, bool? isActive, string? search, int page, int pageSize)
    {
        var query = _dbSet.AsQueryable();

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(m => m.Category == category);
        }
        if (isActive.HasValue)
        {
            query = query.Where(m => m.IsActive == isActive.Value);
        }
        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(m =>
                m.Name.Contains(search) ||
                m.Title.Contains(search) ||
                m.Description!.Contains(search));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(m => m.SortOrder)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public override async Task<Module> AddAsync(Module module)
    {
        // 核心模块始终保持启用状态
        if (module.IsSystemModule)
            module.IsActive = true;
        module.CreatedAt = DateTime.UtcNow;
        module.UpdatedAt = DateTime.UtcNow;
        await _dbSet.AddAsync(module);
        return module;
    }

    public async Task<Module?> UpdateAsync(string name, Module module)
    {
        var existing = await _dbSet.FirstOrDefaultAsync(m => m.Name == name);
        if (existing == null) return null;

        existing.Title = module.Title;
        existing.Description = module.Description;
        existing.Version = module.Version;
        existing.Author = module.Author;
        existing.Icon = module.Icon;
        existing.Category = module.Category;
        existing.Settings = module.Settings;
        existing.SortOrder = module.SortOrder;
        existing.UpdatedAt = DateTime.UtcNow;
        // existing 已通过 tracked 查询，由 SaveChangesAsync 持久化，无需 _dbSet.Update
        return existing;
    }

    public async Task<bool> DeleteAsync(string name)
    {
        var module = await _dbSet.FirstOrDefaultAsync(m => m.Name == name);
        if (module == null) return false;

        // 核心模块：永远不能被删除（仓储层最后一道防线）
        if (module.IsSystemModule)
        {
            throw new InvalidOperationException("核心模块无法被删除");
        }

        _dbSet.Remove(module);
        return true;
    }

    public async Task<bool> ExistsByNameAsync(string name)
    {
        return await _dbSet.AnyAsync(m => m.Name == name);
    }

    public async Task<Module?> ToggleActiveAsync(string name)
    {
        var module = await _dbSet.FirstOrDefaultAsync(m => m.Name == name);
        if (module == null) return null;

        // 核心模块：永远不能被关闭（再次打开保持幂等）
        if (module.IsSystemModule)
        {
            module.IsActive = true;
            module.UpdatedAt = DateTime.UtcNow;
            return module;
        }

        module.IsActive = !module.IsActive;
        module.UpdatedAt = DateTime.UtcNow;
        // module 已通过 tracked 查询，由 SaveChangesAsync 持久化，无需 _dbSet.Update
        return module;
    }

    public async Task<Module?> SetActiveAsync(int id, bool isActive)
    {
        var module = await _dbSet.FindAsync(id);
        if (module == null) return null;

        module.IsActive = module.IsSystemModule ? true : isActive;
        module.UpdatedAt = DateTime.UtcNow;
        return module;
    }
}
