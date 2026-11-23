namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 用户权限响应
/// 对应 PHP: app/UserPermissions.php
/// </summary>
public class UserPermissionsResponse
{
    public int UserId { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool CanViewAllConversations { get; set; }
    public bool CanViewAssignedConversationsOnly { get; set; }
    public bool CanCreateConversations { get; set; }
    public bool CanEditConversations { get; set; }
    public bool CanDeleteConversations { get; set; }
    public bool CanManageMailboxes { get; set; }
    public bool CanManageUsers { get; set; }
    public bool CanManageSettings { get; set; }
    public bool CanViewReports { get; set; }
    public bool CanExportData { get; set; }
    public List<int> AccessibleMailboxIds { get; set; } = new();
}
