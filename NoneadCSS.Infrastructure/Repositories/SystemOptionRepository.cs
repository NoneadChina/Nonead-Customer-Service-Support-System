using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace NoneadCSS.Infrastructure.Repositories;

public class SystemOptionRepository : Repository<SystemOption>, ISystemOptionRepository
{
    /// <summary>内存缓存项前缀，避免与其他缓存键碰撞。</summary>
    private const string CachePrefix = "__sys_opt_cache:";

    /// <summary>
    /// 开关值在内存中的缓存窗口。
    /// - 足够短以确保前端管理员在 Settings 页改值后 30s 内能生效；
    /// - 足够长以避免每封邮件/每次聊天转工单都直查 DB。
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;

    public SystemOptionRepository(NoneadCSSDbContext context, IMemoryCache cache) : base(context)
    {
        _cache = cache;
    }

    public async Task<SystemOption?> GetByNameAsync(string name)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(o => o.Name == name);
    }

    /// <summary>
    /// 按 name 查询系统配置（tracking，适用于后续修改 + SaveChanges 的场景）
    /// - 当通过 tracking 查询写入时，同步使内存缓存失效，确保下一次读取取到新值。
    /// </summary>
    public async Task<SystemOption?> GetByNameTrackedAsync(string name)
    {
        var option = await _dbSet.FirstOrDefaultAsync(o => o.Name == name);
        if (option != null)
            _cache.Remove(CachePrefix + name);
        return option;
    }

    public async Task<IEnumerable<SystemOption>> GetByCategoryAsync(string category)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(o => o.Category == category)
            .OrderBy(o => o.Name)
            .ToListAsync();
    }

    /// <summary>
    /// 按 name 读取单个配置值（热路径：邮件/聊天开关每次主流程都会调用）。
    /// 带 30 秒 IMemoryCache 缓存，命中时不走 DB；
    /// SetAsync / SetRange 写入时主动 Remove 对应键。
    /// </summary>
    public async Task<string?> GetValueAsync(string name)
    {
        var cacheKey = CachePrefix + name;
        if (_cache.TryGetValue(cacheKey, out var cached) && cached is string cachedStr)
            return cachedStr.Length == 0 ? null : cachedStr; // 约定：空串表示 DB 中明确不存在/为空（非 null）；此处统一 null 语义

        var option = await GetByNameAsync(name);
        var value = option?.Value;
        // 保存到缓存：null → 写 "" 作为负缓存（防止不存在的键持续穿透 DB）
        _cache.Set(cacheKey, value ?? string.Empty, CacheDuration);
        return value;
    }

    /// <summary>
    /// 按 name 集合批量读取配置值（单次 DB 查询，避免逐键 N+1）。
    /// 未找到的键不会出现在返回字典中，调用方应按 TryGetValue 处理缺失。
    /// </summary>
    public async Task<Dictionary<string, string?>> GetValuesByKeysAsync(IEnumerable<string> keys)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0) return new Dictionary<string, string?>();

        var options = await _dbSet.AsNoTracking()
            .Where(o => keyList.Contains(o.Name))
            .ToListAsync();
        return options.ToDictionary(o => o.Name, o => (string?)o.Value);
    }

    /// <summary>
    /// 写入单个配置。若配置不存在则创建；存在则仅更新 Value/Category。
    /// - 写入成功后立即使内存缓存失效，保证后续读一致。
    /// </summary>
    public async Task SetAsync(string name, string value, string? category = null)
    {
        // 使用 tracking 查询，避免 AsNoTracking + UpdateAsync 反模式（已知会导致 EF Core 追踪冲突和全字段 UPDATE）
        var option = await _dbSet.FirstOrDefaultAsync(o => o.Name == name);
        if (option == null)
        {
            option = new SystemOption
            {
                Name = name,
                Value = value,
                Category = category
            };
            await _dbSet.AddAsync(option);
        }
        else
        {
            option.Value = value;
            if (category != null)
            {
                option.Category = category;
            }
            option.UpdatedAt = DateTime.UtcNow;
            // 直接 SaveChangesAsync 即可，无需 UpdateAsync（实体已被 tracking）
        }

        // 失效缓存（写后必读最新值）
        _cache.Remove(CachePrefix + name);
    }

    /// <summary>
    /// 使指定 name 的内存缓存失效。
    /// 供外部批量更新路径（如 SystemOptionService.BatchUpdateAsync）在直接修改实体后调用。
    /// </summary>
    public void InvalidateCache(string name)
    {
        _cache.Remove(CachePrefix + name);
    }

    public async Task<IEnumerable<SystemOption>> GetAllWithOptionsAsync()
    {
        return await _dbSet
            .AsNoTracking()
            .OrderBy(o => o.Category ?? "")
            .ThenBy(o => o.Name)
            .ToListAsync();
    }
}
