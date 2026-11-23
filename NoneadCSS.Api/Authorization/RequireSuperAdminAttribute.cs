using Microsoft.AspNetCore.Authorization;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Api.Authorization;

public class RequireSuperAdminAttribute : AuthorizeAttribute
{
    public RequireSuperAdminAttribute()
    {
        // 使用枚举常量替代硬编码角色 ID，避免角色值变更时遗漏修改
        Roles = ((int)UserRoleType.SuperAdmin).ToString();
    }
}
