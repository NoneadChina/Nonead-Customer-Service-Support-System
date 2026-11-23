using Microsoft.EntityFrameworkCore;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

public class TeamService : ITeamService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public TeamService(IUnitOfWork unitOfWork, ICurrentUserAccessor currentUserAccessor)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
    }

    public async Task<(IEnumerable<TeamDto> Items, int Total)> GetPagedAsync(TeamQuery query)
    {
        var (items, total) = await _unitOfWork.Teams.GetPagedAsync(
            query.Page, query.PageSize,
            query.MailboxId, query.IsActive, query.Keyword);

        var teamIds = items.Select(t => t.Id).ToList();
        var allMembers = await _unitOfWork.TeamMembers.GetByTeamIdsAsync(teamIds);
        var memberDict = allMembers.GroupBy(m => m.TeamId).ToDictionary(g => g.Key, g => g.ToList());

        var dtos = items.Select(t =>
        {
            var members = memberDict.GetValueOrDefault(t.Id, []);
            return MapToDto(t, members);
        }).ToList();

        return (dtos, total);
    }

    public async Task<TeamDetailDto?> GetByIdAsync(int id)
    {
        var team = await _unitOfWork.Teams.GetWithMembersAsync(id);
        if (team == null || team.DeletedAt != null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(team.MailboxId))
            return null;

        return new TeamDetailDto
        {
            Id = team.Id,
            Name = team.Name,
            Description = team.Description,
            MailboxId = team.MailboxId,
            MailboxName = team.Mailbox?.Name,
            IsActive = team.IsActive,
            SortOrder = team.SortOrder,
            CreatedAt = team.CreatedAt,
            UpdatedAt = team.UpdatedAt,
            Members = team.Members.Select(MapMemberToDto).ToList()
        };
    }

    public async Task<TeamDto> CreateAsync(CreateTeamRequest request)
    {
        var team = new Team
        {
            Name = request.Name,
            Description = request.Description,
            MailboxId = request.MailboxId,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Teams.AddAsync(team);
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(team, []);
    }

    public async Task<TeamDto?> UpdateAsync(int id, UpdateTeamRequest request)
    {
        var team = await _unitOfWork.Teams.GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (team == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(team.MailboxId))
            return null;

        team.Name = request.Name;
        team.Description = request.Description;
        team.MailboxId = request.MailboxId;
        team.IsActive = request.IsActive;
        team.SortOrder = request.SortOrder;
        team.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        var members = await _unitOfWork.TeamMembers.GetByTeamIdAsync(id);
        return MapToDto(team, members);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var team = await _unitOfWork.Teams.GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (team == null) return false;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(team.MailboxId))
            return false;

        team.DeletedAt = DateTime.UtcNow;
        team.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<TeamOverviewResponse> GetOverviewAsync()
    {
        var stats = await _unitOfWork.Teams.GetStatsAsync();
        var (topItems, _) = await _unitOfWork.Teams.GetPagedAsync(1, 5, isActive: true);

        var topTeamIds = topItems.Select(t => t.Id).ToList();
        var topMembers = await _unitOfWork.TeamMembers.GetByTeamIdsAsync(topTeamIds);
        var topMemberDict = topMembers.GroupBy(m => m.TeamId).ToDictionary(g => g.Key, g => g.ToList());

        var topDtos = topItems.Select(t => MapToDto(t, topMemberDict.GetValueOrDefault(t.Id, []))).ToList();

        return new TeamOverviewResponse
        {
            TotalTeams = stats.GetValueOrDefault("total", 0),
            ActiveTeams = stats.GetValueOrDefault("active", 0),
            InactiveTeams = stats.GetValueOrDefault("inactive", 0),
            TotalMembers = stats.GetValueOrDefault("totalMembers", 0),
            TeamsPerMailbox = stats.GetValueOrDefault("mailboxSpecific", 0),
            TopTeams = topDtos
        };
    }

    public async Task<List<TeamDto>> GetByMailboxIdAsync(int? mailboxId)
    {
        var teams = await _unitOfWork.Teams.GetByMailboxIdAsync(mailboxId);
        var teamIds = teams.Select(t => t.Id).ToList();
        var allMembers = await _unitOfWork.TeamMembers.GetByTeamIdsAsync(teamIds);
        var memberDict = allMembers.GroupBy(m => m.TeamId).ToDictionary(g => g.Key, g => g.ToList());

        return teams.Select(t => MapToDto(t, memberDict.GetValueOrDefault(t.Id, []))).ToList();
    }

    public async Task<List<TeamDto>> GetByUserIdAsync(int userId)
    {
        var teams = await _unitOfWork.Teams.GetByUserIdAsync(userId);
        var teamIds = teams.Select(t => t.Id).ToList();
        var allMembers = await _unitOfWork.TeamMembers.GetByTeamIdsAsync(teamIds);
        var memberDict = allMembers.GroupBy(m => m.TeamId).ToDictionary(g => g.Key, g => g.ToList());

        return teams.Select(t => MapToDto(t, memberDict.GetValueOrDefault(t.Id, []))).ToList();
    }

    public async Task<TeamMemberDto> AddMemberAsync(int teamId, AddTeamMemberRequest request)
    {
        var existing = await _unitOfWork.TeamMembers.GetByTeamAndUserAsync(teamId, request.UserId);
        if (existing != null)
        {
            throw new InvalidOperationException("该用户已在团队中");
        }

        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team == null)
        {
            throw new InvalidOperationException("团队不存在");
        }

        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId);
        if (user == null)
        {
            throw new InvalidOperationException("用户不存在");
        }

        var member = new TeamMember
        {
            TeamId = teamId,
            UserId = request.UserId,
            Role = (TeamMemberRole)request.Role,
            JoinedAt = DateTime.UtcNow
        };

        await _unitOfWork.TeamMembers.AddAsync(member);
        await _unitOfWork.SaveChangesAsync();

        return new TeamMemberDto
        {
            Id = member.Id,
            TeamId = teamId,
            TeamName = team.Name,
            UserId = user.Id,
            UserName = $"{user.FirstName} {user.LastName}",
            UserEmail = user.Email,
            Role = member.Role,
            RoleName = GetRoleName(member.Role),
            JoinedAt = member.JoinedAt
        };
    }

    public async Task<TeamMemberDto?> UpdateMemberRoleAsync(int teamId, int userId, int role)
    {
        var member = await _unitOfWork.TeamMembers.GetByTeamAndUserAsync(teamId, userId);
        if (member == null) return null;

        member.Role = (TeamMemberRole)role;
        await _unitOfWork.SaveChangesAsync();

        return MapMemberToDto(member);
    }

    public async Task<bool> RemoveMemberAsync(int teamId, int userId)
    {
        var member = await _unitOfWork.TeamMembers.GetByTeamAndUserAsync(teamId, userId);
        if (member == null) return false;

        await _unitOfWork.TeamMembers.DeleteAsync(member);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<List<TeamMemberDto>> GetMembersAsync(int teamId)
    {
        var members = await _unitOfWork.TeamMembers.GetByTeamIdAsync(teamId);
        return members.Select(MapMemberToDto).ToList();
    }

    private TeamDto MapToDto(Team team, List<TeamMember> members)
    {
        return new TeamDto
        {
            Id = team.Id,
            Name = team.Name,
            Description = team.Description,
            MailboxId = team.MailboxId,
            MailboxName = team.Mailbox?.Name,
            IsActive = team.IsActive,
            SortOrder = team.SortOrder,
            MemberCount = members.Count,
            LeaderCount = members.Count(m => m.Role == TeamMemberRole.Leader),
            CreatedAt = team.CreatedAt,
            UpdatedAt = team.UpdatedAt,
            DeletedAt = team.DeletedAt
        };
    }

    private TeamMemberDto MapMemberToDto(TeamMember member)
    {
        return new TeamMemberDto
        {
            Id = member.Id,
            TeamId = member.TeamId,
            TeamName = member.Team?.Name ?? string.Empty,
            UserId = member.UserId,
            UserName = member.User != null ? $"{member.User.FirstName} {member.User.LastName}" : string.Empty,
            UserEmail = member.User?.Email ?? string.Empty,
            Role = member.Role,
            RoleName = GetRoleName(member.Role),
            JoinedAt = member.JoinedAt
        };
    }

    private string GetRoleName(TeamMemberRole role)
    {
        return role switch
        {
            TeamMemberRole.Leader => "团队负责人",
            TeamMemberRole.Member => "成员",
            _ => role.ToString()
        };
    }

    public async Task<(IEnumerable<TeamDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.Teams.GetDeletedAsync(page, pageSize, keyword);
        var teamIds = items.Select(t => t.Id).ToList();
        var allMembers = await _unitOfWork.TeamMembers.GetByTeamIdsAsync(teamIds);
        var memberDict = allMembers.GroupBy(m => m.TeamId).ToDictionary(g => g.Key, g => g.ToList());

        var dtos = items.Select(t =>
        {
            var members = memberDict.GetValueOrDefault(t.Id, []);
            return MapToDto(t, members);
        }).ToList();

        return (dtos, total);
    }

    public async Task<int> GetTrashCountAsync()
    {
        return await _unitOfWork.Teams.CountDeletedAsync();
    }

    public async Task<bool> RestoreAsync(int id)
    {
        return await _unitOfWork.Teams.RestoreAsync(id);
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        return await _unitOfWork.Teams.HardDeleteAsync(id);
    }

    public async Task<int> ClearTrashAsync()
    {
        return await _unitOfWork.Teams.HardDeleteAllDeletedAsync();
    }
}
