using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Entities;

public class Team : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public Mailbox? Mailbox { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public List<TeamMember> Members { get; set; } = [];

    public bool IsDeleted() => DeletedAt.HasValue;

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Restore()
    {
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsGlobalTeam() => !MailboxId.HasValue;

    public bool IsMailboxTeam() => MailboxId.HasValue;

    public bool AppliesToMailbox(int mailboxId) => !MailboxId.HasValue || MailboxId.Value == mailboxId;

    public bool HasMember(int userId) => Members.Any(m => m.UserId == userId);

    public bool HasAdmin(int userId) => Members.Any(m => m.UserId == userId && m.Role == TeamMemberRole.Leader);

    public int MemberCount => Members.Count;

    public bool IsEmpty() => Members.Count == 0;

    public bool AddMember(int userId, TeamMemberRole role = TeamMemberRole.Member)
    {
        if (HasMember(userId))
        {
            return false;
        }
        Members.Add(new TeamMember { TeamId = Id, UserId = userId, Role = role, JoinedAt = DateTime.UtcNow });
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool RemoveMember(int userId)
    {
        var member = Members.FirstOrDefault(m => m.UserId == userId);
        if (member == null)
        {
            return false;
        }
        Members.Remove(member);
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool UpdateMemberRole(int userId, TeamMemberRole newRole)
    {
        var member = Members.FirstOrDefault(m => m.UserId == userId);
        if (member == null)
        {
            return false;
        }
        member.Role = newRole;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateName(string newName)
    {
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDescription(string? description)
    {
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    public List<int> GetMemberIds() => Members.Select(m => m.UserId).ToList();

    public List<int> GetAdminIds() => Members.Where(m => m.Role == TeamMemberRole.Leader).Select(m => m.UserId).ToList();
}

public class TeamMember
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public TeamMemberRole Role { get; set; } = TeamMemberRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public bool IsAdmin() => Role == TeamMemberRole.Leader;

    public bool IsMember() => Role == TeamMemberRole.Member;

    public void PromoteToAdmin() => Role = TeamMemberRole.Leader;

    public void DemoteToMember() => Role = TeamMemberRole.Member;
}
