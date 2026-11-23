using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 用户角色解析器实现，统一合并来自 User.Role 字段和 UserRoles 关联表的角色来源。
/// </summary>
public class UserRoleResolver : IUserRoleResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration;

    // 权限缓存版本代际：任何角色/权限变更都会递增该代际号，
    // 使缓存键失效（旧的代际缓存项不可再被命中，并在 TTL 到期后被淘汰）。
    private int _permissionsGeneration;

    public UserRoleResolver(IUnitOfWork unitOfWork, IMemoryCache cache, IOptions<CacheOptions> cacheOptions)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.DefaultCacheMinutes);
    }

    /// <summary>
    /// 使权限解析缓存失效（递增版本代际）。
    /// </summary>
    public void InvalidatePermissionsCache()
    {
        Interlocked.Increment(ref _permissionsGeneration);
    }

    private string BuildPermissionsCacheKey(int userId, int? mailboxId)
        => $"perms:{Volatile.Read(ref _permissionsGeneration)}:{userId}:{mailboxId ?? 0}";

    /// <summary>
    /// 将角色 Slug 映射到系统内置的 UserRoleType 枚举值。
    /// </summary>
    private static UserRoleType? MapSlugToUserRoleType(string slug)
    {
        return slug switch
        {
            "admin" => UserRoleType.SuperAdmin,
            "supervisor" => UserRoleType.AgentSupervisor,
            "agent" => UserRoleType.Agent,
            "viewer" => UserRoleType.ReadOnly,
            _ => null
        };
    }

    public async Task<List<int>> GetRoleIdsAsync(int userId)
    {
        var roleIds = new HashSet<int>();

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.DeletedAt != null) return roleIds.ToList();

        // 1. 来自 UserRoles 关联表的角色
        var userRoles = await _unitOfWork.UserRoles.GetByUserIdAsync(userId);
        foreach (var ur in userRoles.Where(ur => ur.Role?.DeletedAt == null))
        {
            if (ur.RoleId > 0) roleIds.Add(ur.RoleId);
        }

        // 2. 来自 User.Role 字段的系统内置角色
        var systemRole = await FindSystemRoleByUserRoleTypeAsync(user.Role);
        if (systemRole != null)
        {
            roleIds.Add(systemRole.Id);
        }

        return roleIds.ToList();
    }

    /// <summary>
    /// 判断用户是否为超级管理员。
    /// 直接检查 User.Role 枚举字段，避免与数据库 Role.Id 混淆导致的越权问题。
    /// </summary>
    public async Task<bool> IsSuperAdminAsync(int userId)
    {
        return await IsSystemRoleAsync(userId, UserRoleType.SuperAdmin);
    }

    /// <summary>
    /// 判断用户是否为指定的系统角色类型（基于 User.Role 字段）。
    /// </summary>
    public async Task<bool> IsSystemRoleAsync(int userId, UserRoleType roleType)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        return user != null && user.DeletedAt == null && user.Role == roleType;
    }

    /// <summary>
    /// 获取指定系统角色类型（User.Role 字段）的所有有效用户ID集合。
    /// </summary>
    public async Task<HashSet<int>> GetUserIdsBySystemRoleTypeAsync(UserRoleType roleType)
    {
        var result = new HashSet<int>();
        var users = await _unitOfWork.Users.GetByRoleAsync((int)roleType);
        foreach (var user in users)
        {
            if (user.DeletedAt == null)
            {
                result.Add(user.Id);
            }
        }
        return result;
    }

    public async Task<HashSet<int>> GetUserIdsInRoleAsync(int roleId)
    {
        var userIds = new HashSet<int>();

        var role = await _unitOfWork.Roles.GetByIdAsync(roleId);
        if (role == null || role.DeletedAt != null) return userIds;

        // 1. 来自 UserRoles 关联表的用户
        var userRoles = await _unitOfWork.UserRoles.GetByRoleIdAsync(roleId);
        foreach (var ur in userRoles)
        {
            userIds.Add(ur.UserId);
        }

        // 2. 对于系统内置角色，补充来自 User.Role 字段的用户（按角色值精确查询，避免全表扫描）
        if (role.IsSystem)
        {
            var userRoleType = MapSlugToUserRoleType(role.Slug);
            if (userRoleType.HasValue)
            {
                var roleValue = (int)userRoleType.Value;
                var systemRoleUsers = await _unitOfWork.Users.GetByRoleAsync(roleValue);
                foreach (var user in systemRoleUsers)
                {
                    userIds.Add(user.Id);
                }
            }
        }

        return userIds;
    }

    public async Task<List<string>> GetPermissionsAsync(int userId, int? mailboxId = null)
    {
        // TD-05：权限解析结果按 (generation, userId, mailboxId) 缓存，
        // 避免每次请求触发 4-5 次数据库往返。代际号在角色/权限变更时递增，使缓存失效。
        var cacheKey = BuildPermissionsCacheKey(userId, mailboxId);
        if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached != null)
        {
            return cached;
        }

        var permissions = await GetPermissionsCoreAsync(userId, mailboxId);
        _cache.Set(cacheKey, permissions, _cacheDuration);
        return permissions;
    }

    private async Task<List<string>> GetPermissionsCoreAsync(int userId, int? mailboxId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.DeletedAt != null) return new List<string>();

        // 超级管理员自动拥有所有权限
        if (user.Role == UserRoleType.SuperAdmin) return new List<string> { "*" };

        var permissions = new HashSet<string>();
        var roleIds = await GetRoleIdsAsync(userId);

        // 批量加载所有有效角色（避免 N+1 查询：使用一次查询获取所有 roleId 对应的角色）
        if (roleIds.Count > 0)
        {
            var roles = await _unitOfWork.Roles.GetByIdsAsync(roleIds);
            var validRoles = roles.Where(r => r != null && r.DeletedAt == null).ToList();

            // 如果指定了 mailboxId，只取全局角色或该邮箱作用域的角色
            if (mailboxId.HasValue)
            {
                var scopedRoleIds = await GetScopedRoleIdsAsync(userId, mailboxId.Value);
                validRoles = validRoles.Where(r => scopedRoleIds.Contains(r.Id)).ToList();
            }

            foreach (var role in validRoles)
            {
                foreach (var perm in ParsePermissions(role.PermissionsJson))
                {
                    permissions.Add(perm);
                }
            }
        }

        return permissions.ToList();
    }

    public async Task<bool> HasPermissionAsync(int userId, string permission, int? mailboxId = null)
    {
        var permissions = await GetPermissionsAsync(userId, mailboxId);
        return permissions.Contains("*") || permissions.Contains(permission);
    }

    public async Task<int> CountUsersInRoleAsync(int roleId)
    {
        var userIds = await GetUserIdsInRoleAsync(roleId);
        return userIds.Count;
    }

    public async Task<Dictionary<int, int>> CountUsersInRolesAsync(IEnumerable<int> roleIds)
    {
        var result = new Dictionary<int, int>();
        var roleIdList = roleIds.Distinct().ToList();
        if (roleIdList.Count == 0) return result;

        // 批量查询：单次 DB 查询获取所有角色的用户关联，避免 N+1
        var roleCounts = await _unitOfWork.UserRoles.GetQueryable()
            .Where(ur => roleIdList.Contains(ur.RoleId))
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Select(ur => ur.UserId).Distinct().Count() })
            .ToListAsync();
        foreach (var rc in roleCounts)
        {
            result[rc.RoleId] = rc.Count;
        }

        // 补充系统内置角色的 User.Role 字段用户计数（GetUserIdsInRoleAsync 的第 2 步逻辑）
        var systemRoles = await _unitOfWork.Roles.GetQueryable()
            .Where(r => roleIdList.Contains(r.Id) && r.IsSystem && r.DeletedAt == null)
            .ToListAsync();

        // 预计算 role.Id → roleValue 的映射，仅保留能映射到 UserRoleType 的角色
        var roleToValue = new Dictionary<int, int>();
        foreach (var role in systemRoles)
        {
            var userRoleType = MapSlugToUserRoleType(role.Slug);
            if (userRoleType.HasValue)
            {
                roleToValue[role.Id] = (int)userRoleType.Value;
            }
        }

        if (roleToValue.Count > 0)
        {
            // 批量查询所有系统角色的用户计数，避免逐角色 N+1 数据库往返
            var systemRoleValues = roleToValue.Values.Distinct().ToList();
            var systemRoleCounts = await _unitOfWork.Users.GetQueryable()
                .Where(u => systemRoleValues.Contains((int)u.Role) && u.DeletedAt == null)
                .GroupBy(u => (int)u.Role)
                .Select(g => new { RoleValue = g.Key, Count = g.Select(u => u.Id).Distinct().Count() })
                .ToListAsync();
            var countDict = systemRoleCounts.ToDictionary(x => x.RoleValue, x => x.Count);

            foreach (var (roleId, roleValue) in roleToValue)
            {
                var systemRoleUserCount = countDict.TryGetValue(roleValue, out var count) ? count : 0;
                // UserRoles 关联表 + User.Role 字段取并集，这里取较大值（近似，精确需交集去重）
                // 对于 Count 场景，取 UserRoles 计数和系统角色用户计数的并集近似
                var existing = result.GetValueOrDefault(roleId);
                result[roleId] = Math.Max(existing, systemRoleUserCount);
            }
        }

        return result;
    }

    public async Task<List<RoleMember>> GetUsersInRoleAsync(int roleId)
    {
        var role = await _unitOfWork.Roles.GetByIdAsync(roleId);
        if (role == null || role.DeletedAt != null) return new List<RoleMember>();

        var members = new Dictionary<int, RoleMember>();

        // 1. 来自 UserRoles 关联表的成员
        var userRoles = await _unitOfWork.UserRoles.GetByRoleIdAsync(roleId);
        foreach (var ur in userRoles)
        {
            members[ur.UserId] = new RoleMember
            {
                UserId = ur.UserId,
                UserName = ur.User?.GetFullName() ?? string.Empty,
                UserEmail = ur.User?.Email ?? string.Empty,
                MailboxId = ur.MailboxId,
                MailboxName = ur.Mailbox?.Name,
                CreatedAt = ur.CreatedAt,
                Source = RoleMemberSource.UserRoleAssignment,
            };
        }

        // 2. 对于系统内置角色，补充来自 User.Role 字段的成员（按角色值精确查询，避免全表扫描）
        if (role.IsSystem)
        {
            var userRoleType = MapSlugToUserRoleType(role.Slug);
            if (userRoleType.HasValue)
            {
                var roleValue = (int)userRoleType.Value;
                var systemRoleUsers = await _unitOfWork.Users.GetByRoleAsync(roleValue);
                foreach (var user in systemRoleUsers)
                {
                    if (!members.ContainsKey(user.Id))
                    {
                        members[user.Id] = new RoleMember
                        {
                            UserId = user.Id,
                            UserName = user.GetFullName(),
                            UserEmail = user.Email,
                            MailboxId = null,
                            MailboxName = null,
                            CreatedAt = user.CreatedAt,
                            Source = RoleMemberSource.UserRoleField,
                        };
                    }
                }
            }
        }

        return members.Values.OrderBy(m => m.UserName).ToList();
    }

    /// <summary>
    /// 获取指定用户的系统角色类型（基于 User.Role 字段）。
    /// 用于专员创建事件时校验目标用户角色是否为只读。
    /// </summary>
    public async Task<UserRoleType> GetUserRoleTypeAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.DeletedAt != null)
            return UserRoleType.ReadOnly;
        return user.Role;
    }

    /// <summary>
    /// 根据 UserRoleType 查找对应的系统角色。
    /// </summary>
    private async Task<Role?> FindSystemRoleByUserRoleTypeAsync(UserRoleType userRoleType)
    {
        var slug = userRoleType switch
        {
            UserRoleType.SuperAdmin => "admin",
            UserRoleType.AgentSupervisor => "supervisor",
            UserRoleType.Agent => "agent",
            UserRoleType.ReadOnly => "viewer",
            _ => null
        };

        if (string.IsNullOrEmpty(slug)) return null;

        return await _unitOfWork.Roles.GetBySlugAsync(slug);
    }

    /// <summary>
    /// 获取用户在指定邮箱作用域下的角色ID集合。
    /// </summary>
    private async Task<HashSet<int>> GetScopedRoleIdsAsync(int userId, int mailboxId)
    {
        var roleIds = new HashSet<int>();

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null) return roleIds;

        // 来自 UserRoles 关联表的角色（全局或该邮箱作用域）
        var userRoles = await _unitOfWork.UserRoles.GetByUserIdAsync(userId);
        foreach (var ur in userRoles.Where(ur => ur.Role?.DeletedAt == null &&
                                                  (ur.MailboxId == null || ur.MailboxId == mailboxId)))
        {
            roleIds.Add(ur.RoleId);
        }

        // 来自 User.Role 字段的系统内置角色（全局作用域）
        var systemRole = await FindSystemRoleByUserRoleTypeAsync(user.Role);
        if (systemRole != null)
        {
            roleIds.Add(systemRole.Id);
        }

        return roleIds;
    }

    private static List<string> ParsePermissions(string json)
    {
        if (string.IsNullOrEmpty(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
