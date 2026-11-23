using Microsoft.AspNetCore.Authorization;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Api.Authorization;

/// <summary>
/// 限制仅超级管理员（SuperAdmin）与客服主管（AgentSupervisor）可访问。
/// </summary>
public class RequireSuperAdminOrSupervisorAttribute : AuthorizeAttribute
{
    public RequireSuperAdminOrSupervisorAttribute()
    {
        // 使用枚举常量替代硬编码角色 ID，避免角色值变更时遗漏修改
        Roles = $"{(int)UserRoleType.SuperAdmin},{(int)UserRoleType.AgentSupervisor}";
    }
}
