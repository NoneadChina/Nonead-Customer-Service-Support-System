using System.Linq;
using System.Linq.Expressions;

namespace NoneadCSS.Domain.Repositories;

public interface IRepository<T> where T : class
{
    /// <summary>
    /// 按 ID 查询实体（AsNoTracking，适用于只读场景）
    /// </summary>
    Task<T?> GetByIdAsync(int id);

    /// <summary>
    /// 按 ID 查询实体（tracking，适用于后续修改 + SaveChanges 的场景）
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GetByIdAsync"/> 的区别：返回的实体被 EF Core 变更跟踪器跟踪，
    /// 修改属性后只需调用 SaveChangesAsync 即可持久化，无需调用 UpdateAsync。
    /// 避免 AsNoTracking + UpdateAsync 导致的整个对象图被标记为 Modified 的问题。
    /// </remarks>
    Task<T?> GetByIdTrackedAsync(int id);

    Task<IEnumerable<T>> GetAllAsync();

    /// <summary>
    /// 获取所有实体（tracking，适用于批量修改 + SaveChanges 的场景）
    /// </summary>
    Task<IEnumerable<T>> GetAllTrackedAsync();

    IQueryable<T> GetQueryable(bool asNoTracking = true);
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task<(IEnumerable<T> Items, int Total)> GetPagedAsync(int page, int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        bool asNoTracking = true);
    Task<T> AddAsync(T entity);
    /// <summary>
    /// 批量添加实体到变更跟踪器（不立即持久化，需配合 SaveChangesAsync）。
    /// </summary>
    Task AddRangeAsync(IEnumerable<T> entities);
    Task DeleteAsync(T entity);
    Task<int> CountAsync();
    Task<bool> ExistsAsync(int id);
    T? FindTracked(int id);
}
