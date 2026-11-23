using System.Text.Json;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class RoleRepository : Repository<Role>, IRoleRepository
{
    public RoleRepository(NoneadCSSDbContext context) : base(context) { }

    public async Task<Role?> GetBySlugAsync(string slug)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug && r.DeletedAt == null);
    }

    public async Task<(IEnumerable<Role> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? keyword = null)
    {
        var query = _dbSet.AsNoTracking().Where(r => r.DeletedAt == null).AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(r =>
                r.Name.Contains(keyword) ||
                r.Slug.Contains(keyword) ||
                r.Description != null && r.Description.Contains(keyword));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public new async Task<List<Role>> GetAllAsync()
    {
        return await _dbSet
            .AsNoTracking()
            .Where(r => r.DeletedAt == null)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToListAsync();
    }

    public async Task<List<Role>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<Role>();
        }

        return await _dbSet.AsNoTracking()
            .Where(r => idList.Contains(r.Id) && r.DeletedAt == null)
            .ToListAsync();
    }

    public async Task<List<Role>> GetByUserIdAsync(int userId, int? mailboxId = null)
    {
        IQueryable<Role> query = from ur in _context.UserRoles
                    join r in _dbSet on ur.RoleId equals r.Id
                    where ur.UserId == userId && r.DeletedAt == null
                    select r;

        if (mailboxId.HasValue)
        {
            var userRoleIds = _context.UserRoles
                .Where(ur => ur.UserId == userId &&
                    (ur.MailboxId == null || ur.MailboxId == mailboxId.Value))
                .Select(ur => ur.RoleId);
            query = _dbSet.Where(r => userRoleIds.Contains(r.Id) && r.DeletedAt == null);
        }

        return await query.AsNoTracking().Distinct().OrderBy(r => r.SortOrder).ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        var total = await _dbSet.AsNoTracking().CountAsync(r => r.DeletedAt == null);
        var system = await _dbSet.AsNoTracking().CountAsync(r => r.IsSystem && r.DeletedAt == null);
        var custom = total - system;

        return new Dictionary<string, int>
        {
            { "total", total },
            { "system", system },
            { "custom", custom },
        };
    }

    public async Task<bool> UserHasPermissionAsync(int userId, string permission, int? mailboxId = null)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return false;
        if (user.Role == UserRoleType.SuperAdmin) return true;

        var permissions = await GetUserPermissionsAsync(userId, mailboxId);
        return permissions.Contains(permission) || permissions.Contains("*");
    }

    public async Task<List<string>> GetUserPermissionsAsync(int userId, int? mailboxId = null)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return new List<string>();
        if (user.Role == UserRoleType.SuperAdmin)
        {
            return new List<string> { "*" };
        }

        var rolesQuery = from ur in _context.UserRoles
                        where ur.UserId == userId
                        select ur.RoleId;

        if (mailboxId.HasValue)
        {
            rolesQuery = from ur in _context.UserRoles
                         where ur.UserId == userId &&
                               (ur.MailboxId == null || ur.MailboxId == mailboxId.Value)
                         select ur.RoleId;
        }

        var roleIds = await rolesQuery.Distinct().ToListAsync();
        var roles = await _dbSet.Where(r => roleIds.Contains(r.Id) && r.DeletedAt == null).ToListAsync();

        var allPermissions = new HashSet<string>();
        foreach (var role in roles)
        {
            var perms = ParsePermissions(role.PermissionsJson);
            foreach (var p in perms) allPermissions.Add(p);
        }

        return allPermissions.ToList();
    }

    public async Task<(IEnumerable<Role> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = _dbSet.IgnoreQueryFilters().AsNoTracking().Where(r => r.DeletedAt != null).AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(r =>
                r.Name.Contains(keyword) ||
                r.Slug.Contains(keyword) ||
                r.Description != null && r.Description.Contains(keyword));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.DeletedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().AsNoTracking().CountAsync(r => r.DeletedAt != null);
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var role = await _dbSet.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt != null);
        if (role == null) return false;

        // 检查 slug 是否冲突
        var slugConflict = await _dbSet.AnyAsync(r => r.Slug == role.Slug && r.Id != id && r.DeletedAt == null);
        var newSlug = slugConflict
            ? $"{role.Slug}-restored-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : role.Slug;

        await _dbSet.IgnoreQueryFilters()
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DeletedAt, (DateTime?)null)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow)
                .SetProperty(r => r.Slug, newSlug));
        return true;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(r => r.Id == id && r.DeletedAt != null);
        if (!exists) return false;

        // 删除用户角色关联（外键依赖）
        await _context.UserRoles.Where(ur => ur.RoleId == id).ExecuteDeleteAsync();
        // 再删除角色（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters().Where(r => r.Id == id).ExecuteDeleteAsync();
        return true;
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(r => r.DeletedAt != null);
        if (count > 0)
        {
            var ids = await _dbSet.IgnoreQueryFilters()
                .Where(r => r.DeletedAt != null)
                .Select(r => r.Id)
                .ToListAsync();

            // 删除用户角色关联（外键依赖）
            await _context.UserRoles.Where(ur => ids.Contains(ur.RoleId)).ExecuteDeleteAsync();
            // 再删除所有已软删除的角色
            await _dbSet.IgnoreQueryFilters().Where(r => r.DeletedAt != null).ExecuteDeleteAsync();
        }
        return count;
    }

    private static List<string> ParsePermissions(string json)
    {
        if (string.IsNullOrEmpty(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}

public class UserRoleRepository : Repository<UserRole>, IUserRoleRepository
{
    public UserRoleRepository(NoneadCSSDbContext context) : base(context) { }

    public async Task<List<UserRole>> GetByUserIdAsync(int userId)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(ur => ur.Role)
            .Include(ur => ur.Mailbox)
            .Where(ur => ur.UserId == userId)
            .OrderBy(ur => ur.Id)
            .ToListAsync();
    }

    public async Task<List<UserRole>> GetByRoleIdAsync(int roleId)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(ur => ur.User)
            .Include(ur => ur.Mailbox)
            .Where(ur => ur.RoleId == roleId)
            .OrderBy(ur => ur.Id)
            .ToListAsync();
    }

    public async Task<UserRole?> GetByUserAndRoleAsync(int userId, int roleId, int? mailboxId = null)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(ur =>
            ur.UserId == userId && ur.RoleId == roleId && ur.MailboxId == mailboxId);
    }

    public async Task DeleteByUserIdAsync(int userId)
    {
        var items = await _dbSet.Where(ur => ur.UserId == userId).ToListAsync();
        _dbSet.RemoveRange(items);
    }

    public async Task DeleteByRoleIdAsync(int roleId)
    {
        var items = await _dbSet.Where(ur => ur.RoleId == roleId).ToListAsync();
        _dbSet.RemoveRange(items);
    }
}
