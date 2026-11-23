using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class TeamRepository : Repository<Team>, ITeamRepository
{
    public TeamRepository(NoneadCSSDbContext context) : base(context) { }

    private IQueryable<Team> ApplyIncludes(IQueryable<Team> query)
    {
        return query.Include(t => t.Mailbox);
    }

    public async Task<List<Team>> GetByMailboxIdAsync(int? mailboxId)
    {
        var query = ApplyIncludes(_dbSet).AsQueryable().Where(t => t.DeletedAt == null);

        if (mailboxId.HasValue)
        {
            query = query.Where(t => t.MailboxId == null || t.MailboxId == mailboxId.Value);
        }

        return await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<Team?> GetWithMembersAsync(int id)
    {
        return await _dbSet
            .Include(t => t.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
    }

    public async Task<List<Team>> GetByUserIdAsync(int userId)
    {
        return await ApplyIncludes(_dbSet)
            .Where(t => t.Members.Any(m => m.UserId == userId) && t.DeletedAt == null)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<(IEnumerable<Team> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? mailboxId = null, bool? isActive = null, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet).AsQueryable().Where(t => t.DeletedAt == null);

        if (mailboxId.HasValue)
        {
            query = query.Where(t => t.MailboxId == null || t.MailboxId == mailboxId.Value);
        }
        if (isActive.HasValue)
        {
            query = query.Where(t => t.IsActive == isActive.Value);
        }
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(t =>
                t.Name.Contains(keyword) ||
                (t.Description != null && t.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Dictionary<string, int>> GetStatsAsync()
    {
        var total = await _dbSet.CountAsync(t => t.DeletedAt == null);
        var active = await _dbSet.CountAsync(t => t.IsActive && t.DeletedAt == null);
        var global = await _dbSet.CountAsync(t => t.MailboxId == null && t.DeletedAt == null);
        var mailboxSpecific = total - global;

        var memberCount = await _context.Set<TeamMember>().CountAsync();

        var result = new Dictionary<string, int>
        {
            { "total", total },
            { "active", active },
            { "inactive", total - active },
            { "global", global },
            { "mailboxSpecific", mailboxSpecific },
            { "totalMembers", memberCount }
        };

        return result;
    }

    public async Task<(IEnumerable<Team> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_dbSet.IgnoreQueryFilters()).Where(t => t.DeletedAt != null).AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(t =>
                t.Name.Contains(keyword) ||
                (t.Description != null && t.Description.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.DeletedAt)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<int> CountDeletedAsync()
    {
        return await _dbSet.IgnoreQueryFilters().CountAsync(t => t.DeletedAt != null);
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(t => t.Id == id && t.DeletedAt != null);
        if (!exists) return false;

        await _dbSet.IgnoreQueryFilters()
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.DeletedAt, (DateTime?)null)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow));
        return true;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var exists = await _dbSet.IgnoreQueryFilters().AnyAsync(t => t.Id == id && t.DeletedAt != null);
        if (!exists) return false;

        // 删除团队成员关联（外键依赖）
        await _context.TeamMembers.Where(m => m.TeamId == id).ExecuteDeleteAsync();
        // 再删除团队（绕过全局查询过滤器）
        await _dbSet.IgnoreQueryFilters().Where(t => t.Id == id).ExecuteDeleteAsync();
        return true;
    }

    public async Task<int> HardDeleteAllDeletedAsync()
    {
        var count = await _dbSet.IgnoreQueryFilters().CountAsync(t => t.DeletedAt != null);
        if (count > 0)
        {
            var ids = await _dbSet.IgnoreQueryFilters()
                .Where(t => t.DeletedAt != null)
                .Select(t => t.Id)
                .ToListAsync();

            // 删除团队成员关联（外键依赖）
            await _context.TeamMembers.Where(m => ids.Contains(m.TeamId)).ExecuteDeleteAsync();
            // 再删除所有已软删除的团队
            await _dbSet.IgnoreQueryFilters().Where(t => t.DeletedAt != null).ExecuteDeleteAsync();
        }
        return count;
    }
}

public class TeamMemberRepository : Repository<TeamMember>, ITeamMemberRepository
{
    public TeamMemberRepository(NoneadCSSDbContext context) : base(context) { }

    public async Task<List<TeamMember>> GetByTeamIdAsync(int teamId)
    {
        return await _dbSet
            .Include(m => m.User)
            .Include(m => m.Team)
            .Where(m => m.TeamId == teamId)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<List<TeamMember>> GetByTeamIdsAsync(IEnumerable<int> teamIds)
    {
        var ids = teamIds.ToList();
        if (ids.Count == 0) return new List<TeamMember>();
        return await _dbSet
            .Include(m => m.User)
            .Include(m => m.Team)
            .Where(m => ids.Contains(m.TeamId))
            .OrderBy(m => m.Role)
            .ThenBy(m => m.Id)
            .ToListAsync();
    }

    public async Task<List<TeamMember>> GetByUserIdAsync(int userId)
    {
        return await _dbSet
            .Include(m => m.Team!)
                .ThenInclude(t => t.Mailbox)
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Team!.SortOrder)
            .ToListAsync();
    }

    public async Task<TeamMember?> GetByTeamAndUserAsync(int teamId, int userId)
    {
        return await _dbSet
            .Include(m => m.User)
            .Include(m => m.Team)
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId);
    }

    public async Task<bool> IsMemberAsync(int teamId, int userId)
    {
        return await _dbSet.AnyAsync(m => m.TeamId == teamId && m.UserId == userId);
    }

    public async Task<bool> IsLeaderAsync(int teamId, int userId)
    {
        return await _dbSet.AnyAsync(m =>
            m.TeamId == teamId &&
            m.UserId == userId &&
            m.Role == TeamMemberRole.Leader);
    }
}
