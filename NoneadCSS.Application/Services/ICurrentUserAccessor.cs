namespace NoneadCSS.Application.Services;

/// <summary>
/// 当前用户访问器，用于在 Service 层获取当前登录用户信息及邮箱访问权限。
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>当前用户 ID（未登录返回 0）</summary>
    int UserId { get; }

    /// <summary>是否为超级管理员</summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// 判断当前用户是否有权访问指定邮箱。
    /// mailboxId 为 null 时返回 true（全局资源）；超管始终返回 true。
    /// </summary>
    Task<bool> CanAccessMailboxAsync(int? mailboxId);

    /// <summary>
    /// 获取当前用户可访问的邮箱 ID 列表。
    /// 超管返回 null（表示不限制）；普通用户返回其授权邮箱 ID 数组。
    /// 调用方约定：null 表示不限制，空数组表示仅允许全局资源（mailboxId 为 null 的数据）。
    /// </summary>
    Task<int[]?> GetAccessibleMailboxIdsAsync();
}
