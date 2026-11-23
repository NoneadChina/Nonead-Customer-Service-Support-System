namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 角色实体 - 定义一组权限的集合
/// </summary>
public class Role : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>角色名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>角色标识（用于代码中引用）</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>角色描述</summary>
    public string? Description { get; set; }

    /// <summary>
    /// 权限列表，JSON 格式存储。
    /// 如：["conversations.view", "conversations.create", "users.manage"]
    /// </summary>
    public string PermissionsJson { get; set; } = "[]";

    /// <summary>是否为系统角色（不可删除）</summary>
    public bool IsSystem { get; set; }

    /// <summary>排序</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>软删除标记（null 表示未删除，非空表示已删除并记录删除时间）</summary>
    public DateTime? DeletedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = [];
}

/// <summary>
/// 用户-角色 关联实体
/// </summary>
public class UserRole
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int RoleId { get; set; }

    /// <summary>邮箱 ID（可选），null 表示全局角色</summary>
    public int? MailboxId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public Role? Role { get; set; }
    public Mailbox? Mailbox { get; set; }
}
