namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 邮箱权限响应
/// 对应 PHP: app/MailboxPermission.php
/// </summary>
public class MailboxPermissionResponse
{
    public int MailboxId { get; set; }
    public string MailboxName { get; set; } = string.Empty;
    public string MailboxEmail { get; set; } = string.Empty;
    public List<MailboxUserPermission> UserPermissions { get; set; } = new();
}

public class MailboxUserPermission
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public bool CanView { get; set; } = true;
    public bool CanReply { get; set; } = true;
    public bool CanDelete { get; set; }
    public bool CanManage { get; set; }
}
