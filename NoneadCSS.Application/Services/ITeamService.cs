using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ITeamService
{
    Task<(IEnumerable<TeamDto> Items, int Total)> GetPagedAsync(TeamQuery query);
    Task<TeamDetailDto?> GetByIdAsync(int id);
    Task<TeamDto> CreateAsync(CreateTeamRequest request);
    Task<TeamDto?> UpdateAsync(int id, UpdateTeamRequest request);
    Task<bool> DeleteAsync(int id);
    Task<TeamOverviewResponse> GetOverviewAsync();
    Task<List<TeamDto>> GetByMailboxIdAsync(int? mailboxId);
    Task<List<TeamDto>> GetByUserIdAsync(int userId);
    Task<TeamMemberDto> AddMemberAsync(int teamId, AddTeamMemberRequest request);
    Task<TeamMemberDto?> UpdateMemberRoleAsync(int teamId, int userId, int role);
    Task<bool> RemoveMemberAsync(int teamId, int userId);
    Task<List<TeamMemberDto>> GetMembersAsync(int teamId);
    Task<(IEnumerable<TeamDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null);
    Task<int> GetTrashCountAsync();
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();
}
