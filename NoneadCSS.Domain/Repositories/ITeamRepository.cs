using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ITeamRepository : IRepository<Team>
{
    Task<List<Team>> GetByMailboxIdAsync(int? mailboxId);
    Task<Team?> GetWithMembersAsync(int id);
    Task<List<Team>> GetByUserIdAsync(int userId);
    Task<(IEnumerable<Team> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? mailboxId = null, bool? isActive = null, string? keyword = null);
    Task<Dictionary<string, int>> GetStatsAsync();
    Task<(IEnumerable<Team> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}

public interface ITeamMemberRepository : IRepository<TeamMember>
{
    Task<List<TeamMember>> GetByTeamIdAsync(int teamId);
    Task<List<TeamMember>> GetByTeamIdsAsync(IEnumerable<int> teamIds);
    Task<List<TeamMember>> GetByUserIdAsync(int userId);
    Task<TeamMember?> GetByTeamAndUserAsync(int teamId, int userId);
    Task<bool> IsMemberAsync(int teamId, int userId);
    Task<bool> IsLeaderAsync(int teamId, int userId);
}
