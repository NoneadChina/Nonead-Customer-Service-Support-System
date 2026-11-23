using System.Linq;
using System.Linq.Expressions;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    protected readonly NoneadCSSDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public Repository(NoneadCSSDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        return await _dbSet.AsNoTracking()
            .FirstOrDefaultAsync(e => EF.Property<int>(e, "Id") == id);
    }

    /// <summary>
    /// 按 ID 查询实体（tracking，适用于后续修改 + SaveChanges 的场景）
    /// </summary>
    public virtual async Task<T?> GetByIdTrackedAsync(int id)
    {
        // FindAsync 优先从变更跟踪器查找，未命中则查数据库，且返回的实体被跟踪
        return await _dbSet.FindAsync(id);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _dbSet.AsNoTracking().ToListAsync();
    }

    /// <summary>
    /// 获取所有实体（tracking，适用于批量修改 + SaveChanges 的场景）
    /// </summary>
    public virtual async Task<IEnumerable<T>> GetAllTrackedAsync()
    {
        return await _dbSet.ToListAsync();
    }

    public virtual IQueryable<T> GetQueryable(bool asNoTracking = true)
    {
        return asNoTracking ? _dbSet.AsNoTracking() : _dbSet.AsQueryable();
    }

    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await _dbSet.AsNoTracking().Where(predicate).ToListAsync();
    }

    public virtual async Task<(IEnumerable<T> Items, int Total)> GetPagedAsync(int page, int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        bool asNoTracking = true)
    {
        var query = _dbSet.AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();
        if (predicate != null) query = query.Where(predicate);

        var total = await query.CountAsync();

        // 调用方未指定排序时，默认按主键（Id）排序，避免 Skip/Take 无 OrderBy 触发 EF Core 警告并产生不可预测结果
        query = orderBy != null
            ? orderBy(query)
            : query.OrderBy(e => EF.Property<object>(e, "Id"));

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
        return entity;
    }

    /// <summary>
    /// 批量添加实体到变更跟踪器（不立即持久化，需配合 SaveChangesAsync）。
    /// </summary>
    public virtual Task AddRangeAsync(IEnumerable<T> entities)
    {
        return _dbSet.AddRangeAsync(entities);
    }

    public virtual Task DeleteAsync(T entity)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }

    public virtual async Task<int> CountAsync()
    {
        return await _dbSet.AsNoTracking().CountAsync();
    }

    public virtual async Task<bool> ExistsAsync(int id)
    {
        return await _dbSet.AsNoTracking()
            .AnyAsync(e => EF.Property<int>(e, "Id") == id);
    }

    public virtual T? FindTracked(int id)
    {
        // 仅在变更跟踪器的 Local 视图中查找已跟踪的实体（不查数据库）。
        // 注意：EF.Property<T> 只能在 EF LINQ 查询表达式中使用，不能在内存迭代中使用，
        // 否则抛 InvalidOperationException: The EF.Property<T> method may only be used within Entity Framework LINQ queries.
        // 因此这里直接用反射读取 Id 属性。
        foreach (var entity in _dbSet.Local)
        {
            var entityId = (int?)entity?.GetType().GetProperty("Id")?.GetValue(entity, null);
            if (entityId == id)
                return entity;
        }
        return null;
    }
}
