using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新邮箱权限请求
/// </summary>
public class MailboxPermissionRequest
{
    [Required]
    public List<MailboxUserPermissionItem> UserPermissions { get; set; } = new();
}

public class MailboxUserPermissionItem
{
    [Required]
    public int UserId { get; set; }

    public bool CanView { get; set; } = true;
    public bool CanReply { get; set; } = true;
    public bool CanDelete { get; set; }
    public bool CanManage { get; set; }
}
