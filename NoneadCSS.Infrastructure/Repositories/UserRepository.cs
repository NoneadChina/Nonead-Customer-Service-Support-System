using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    private IQueryable<User> ApplyIncludes(IQueryable<User> query)
    {
        return query.Include(u => u.MailboxUsers);
    }

    /// <summary>
    /// 按 ID 列表批量获取用户（避免 N+1 查询）
    /// </summary>
    public async Task<List<User>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<User>();
        }

        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(u => idList.Contains(u.Id) && u.DeletedAt == null)
            .ToListAsync();
    }

    public override async Task<User?> GetByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);
    }

    public override async Task<IEnumerable<User>> GetAllAsync()
    {
        return await ApplyIncludes(_dbSet.AsNoTracking())
            .Where(u => u.Status != UserStatus.Deleted && u.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<(IEnumerable<User> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? keyword = null, int? status = null)
    {
        var query = ApplyIncludes(_dbSet.AsNoTracking())
            .Where(u => u.DeletedAt == null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(u =>
                (u.FirstName + " " + u.LastName).Contains(keyword) ||
                u.Email.Contains(keyword));
        }

        if (status.HasValue)
        {
            query = query.Where(u => (int)u.Status == status.Value);
        }
        else
        {
            query = query.Where(u => u.Status != UserStatus.Deleted);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(int Total, int Active, int Disabled, int Pending)> GetStatsAsync()
    {
        var query = _dbSet.AsNoTracking().Where(u => u.DeletedAt == null && u.Status != UserStatus.Deleted);

        var total = await query.CountAsync();
        var active = await query.Where(u => u.Status == UserStatus.Active).CountAsync();
        var disabled = await query.Where(u => u.Status == UserStatus.Disabled).CountAsync();
        var pending = await query.Where(u => u.Status == UserStatus.PendingActivation).CountAsync();

        return (total, active, disabled, pending);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email && u.Status != UserStatus.Deleted && u.DeletedAt == null);
    }

    public async Task<User?> GetByEmailAndPasswordAsync(string email, string passwordHash)
    {
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email && u.Password == passwordHash && u.Status != UserStatus.Deleted && u.DeletedAt == null);
    }

    public async Task<IEnumerable<User>> GetByRoleAsync(int role)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(u => (int)u.Role == role && u.Status != UserStatus.Deleted && u.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _dbSet
            .AsNoTracking()
            .AnyAsync(u => u.Email == email && u.Status != UserStatus.Deleted && u.DeletedAt == null);
    }

    public async Task<IEnumerable<User>> GetDeletedAsync()
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .Where(u => u.DeletedAt != null)
            .OrderByDescending(u => u.DeletedAt)
            .ToListAsync();
    }

    public async Task<User?> GetDeletedByIdAsync(int id)
    {
        return await ApplyIncludes(_dbSet.IgnoreQueryFilters().AsNoTracking())
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt != null);
    }

    public async Task HardDeleteAsync(int id)
    {
        // 物理删除涉及多表关联数据清理，必须在事务内调用以保证原子性
        if (_context.Database.CurrentTransaction == null)
            throw new InvalidOperationException("HardDelete 要求在事务内调用");

        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(u => u.Id == id && u.DeletedAt != null);
        if (!exists) return;

        // 清理未配置级联删除的关联数据（保留原有清理范围）
        await _context.Followers.IgnoreQueryFilters()
            .Where(f => f.UserId == id)
            .ExecuteDeleteAsync();

        await _context.Stars.IgnoreQueryFilters()
            .Where(s => s.UserId == id)
            .ExecuteDeleteAsync();

        await _context.PasswordResetTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == id)
            .ExecuteDeleteAsync();

        // 删除用户本身（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters()
            .Where(u => u.Id == id && u.DeletedAt != null)
            .ExecuteDeleteAsync();
    }

    public async Task HardDeleteAllDeletedAsync()
    {
        var ids = await _dbSet.IgnoreQueryFilters()
            .Where(u => u.DeletedAt != null)
            .Select(u => u.Id)
            .ToListAsync();

        if (ids.Count == 0) return;

        // 清理未配置级联删除的关联数据
        await _context.Followers.IgnoreQueryFilters()
            .Where(f => ids.Contains(f.UserId))
            .ExecuteDeleteAsync();

        await _context.Stars.IgnoreQueryFilters()
            .Where(s => ids.Contains(s.UserId))
            .ExecuteDeleteAsync();

        await _context.PasswordResetTokens.IgnoreQueryFilters()
            .Where(t => ids.Contains(t.UserId))
            .ExecuteDeleteAsync();

        // 删除所有已软删除的用户
        await _dbSet.IgnoreQueryFilters()
            .Where(u => u.DeletedAt != null)
            .ExecuteDeleteAsync();
    }
}
