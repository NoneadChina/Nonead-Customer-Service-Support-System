namespace NoneadCSS.Application.DTOs.Responses;

public class UserResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Role { get; set; }
    public int Status { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    /// <summary>用户头像 URL（指向 /api/attachments/{id}/public-preview），null 表示无头像</summary>
    public string? Avatar { get; set; }
    public List<int> MailboxIds { get; set; } = [];
    public List<int> Permissions { get; set; } = [];
    /// <summary>细粒度权限键列表（与后端 PermissionItem.Key 对应，如 conversations.assign）</summary>
    public List<string> StringPermissions { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    /// <summary>乐观锁版本标记（RowVersion base64 字符串），资料编辑/状态修改时回传 ifMatch。</summary>
    public string? RowVersion { get; set; }
}
