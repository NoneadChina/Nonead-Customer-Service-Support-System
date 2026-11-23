using Microsoft.AspNetCore.Authorization;
using NoneadCSS.Application.Services;
using System.Security.Claims;

namespace NoneadCSS.Api.Authorization;

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserRoleResolver _userRoleResolver;

    public PermissionAuthorizationHandler(IUserRoleResolver userRoleResolver)
    {
        _userRoleResolver = userRoleResolver;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            context.Fail();
            return;
        }

        var hasPermission = await _userRoleResolver.HasPermissionAsync(userId, requirement.Permission);
        if (hasPermission)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}