using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Responses;

public class TeamDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int MemberCount { get; set; }
    public int LeaderCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class TeamMemberDto
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public TeamMemberRole Role { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}

public class TeamDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<TeamMemberDto> Members { get; set; } = [];
}

public class TeamOverviewResponse
{
    public int TotalTeams { get; set; }
    public int ActiveTeams { get; set; }
    public int InactiveTeams { get; set; }
    public int TotalMembers { get; set; }
    public int TeamsPerMailbox { get; set; }
    public List<TeamDto> TopTeams { get; set; } = [];
}
