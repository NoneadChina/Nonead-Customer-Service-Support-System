using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 用户角色解析器，统一处理来自 User.Role 字段和 UserRoles 关联表的角色来源。
/// 所有需要查询用户角色、权限的地方都应通过此接口，避免直接操作两套数据源。
/// </summary>
public interface IUserRoleResolver
{
    /// <summary>
    /// 获取用户的所有有效角色ID（合并 User.Role 系统角色和 UserRoles 关联角色）。
    /// </summary>
    Task<List<int>> GetRoleIdsAsync(int userId);

    /// <summary>
    /// 判断用户是否为指定的系统角色类型（基于 User.Role 字段）。
    /// </summary>
    Task<bool> IsSystemRoleAsync(int userId, UserRoleType roleType);

    /// <summary>
    /// 获取指定系统角色类型（User.Role 字段）的所有有效用户ID集合。
    /// </summary>
    Task<HashSet<int>> GetUserIdsBySystemRoleTypeAsync(UserRoleType roleType);

    /// <summary>
    /// 判断用户是否为超级管理员（直接检查 User.Role 枚举字段，避免与数据库 Role.Id 混淆）。
    /// 这是判断超管身份的唯一权威方法，控制器层不要使用 GetRoleIdsAsync + Contains((int)UserRoleType.SuperAdmin) 模式。
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<bool> IsSuperAdminAsync(int userId);

    /// <summary>
    /// 获取指定角色的所有用户ID（合并 User.Role 系统角色和 UserRoles 关联角色）。
    /// </summary>
    Task<HashSet<int>> GetUserIdsInRoleAsync(int roleId);

    /// <summary>
    /// 获取用户的所有权限key。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="mailboxId">邮箱作用域（可选）</param>
    Task<List<string>> GetPermissionsAsync(int userId, int? mailboxId = null);

    /// <summary>
    /// 判断用户是否拥有指定权限。
    /// </summary>
    Task<bool> HasPermissionAsync(int userId, string permission, int? mailboxId = null);

    /// <summary>
    /// 统计指定角色的实际成员数量。
    /// </summary>
    Task<int> CountUsersInRoleAsync(int roleId);

    /// <summary>
    /// 批量统计多个角色的实际成员数量。
    /// </summary>
    Task<Dictionary<int, int>> CountUsersInRolesAsync(IEnumerable<int> roleIds);

    /// <summary>
    /// 获取指定角色的成员用户列表。
    /// </summary>
    Task<List<RoleMember>> GetUsersInRoleAsync(int roleId);

    /// <summary>
    /// 获取指定用户的系统角色类型（基于 User.Role 字段）。
    /// 用于专员创建事件时校验目标用户角色是否为只读。
    /// </summary>
    Task<UserRoleType> GetUserRoleTypeAsync(int userId);

    /// <summary>
    /// 使权限解析缓存失效。在用户的角色/权限发生变更（分配角色、移除角色、修改角色权限、删除角色等）后调用。
    /// </summary>
    void InvalidatePermissionsCache();
}

/// <summary>
/// 角色成员信息。
/// </summary>
public class RoleMember
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public DateTime CreatedAt { get; set; }
    public RoleMemberSource Source { get; set; }
}

/// <summary>
/// 角色成员来源。
/// </summary>
public enum RoleMemberSource
{
    /// <summary>来自 UserRoles 关联表。</summary>
    UserRoleAssignment,

    /// <summary>来自 User.Role 系统内置角色字段。</summary>
    UserRoleField,
}
