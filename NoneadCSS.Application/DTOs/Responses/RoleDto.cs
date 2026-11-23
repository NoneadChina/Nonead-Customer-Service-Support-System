namespace NoneadCSS.Application.DTOs.Responses;

public class RoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class RoleUserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RoleOverviewResponse
{
    public int TotalRoles { get; set; }
    public int SystemRoles { get; set; }
    public int CustomRoles { get; set; }
    public int TotalRoleAssignments { get; set; }
    public List<RoleDto> Roles { get; set; } = new();
    public Dictionary<string, int> UsersPerRole { get; set; } = new();
    public List<PermissionGroup> PermissionGroups { get; set; } = new();
}

public class PermissionGroup
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<PermissionItem> Permissions { get; set; } = new();
}

public class PermissionItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public int SortOrder { get; set; }
}

public class UpdateRoleRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public int SortOrder { get; set; }
}

public class AssignRoleRequest
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public int? MailboxId { get; set; }
}

public class BatchAssignRoleRequest
{
    public int RoleId { get; set; }
    public List<int> UserIds { get; set; } = new();
    public int? MailboxId { get; set; }
}

public class RoleQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Keyword { get; set; }
}
