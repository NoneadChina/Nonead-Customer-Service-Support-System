using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ISystemOptionRepository : IRepository<SystemOption>
{
    Task<SystemOption?> GetByNameAsync(string name);

    /// <summary>
    /// 按 name 查询系统配置（tracking，适用于后续修改 + SaveChanges 的场景）
    /// </summary>
    Task<SystemOption?> GetByNameTrackedAsync(string name);

    Task<IEnumerable<SystemOption>> GetByCategoryAsync(string category);
    Task<string?> GetValueAsync(string name);

    /// <summary>
    /// 按 name 集合批量读取配置值（单次 DB 查询，避免逐键 N+1）。
    /// 未找到的键不会出现在返回字典中，调用方应按 TryGetValue 处理缺失。
    /// </summary>
    Task<Dictionary<string, string?>> GetValuesByKeysAsync(IEnumerable<string> keys);

    Task SetAsync(string name, string value, string? category = null);
    Task<IEnumerable<SystemOption>> GetAllWithOptionsAsync();

    /// <summary>
    /// 使指定 name 的内存缓存失效。
    /// 供外部批量更新路径（如 BatchUpdateAsync）在直接修改实体后调用，
    /// 确保 GetValueAsync 的 30 秒缓存不会延迟读到旧值。
    /// </summary>
    void InvalidateCache(string name);
}
