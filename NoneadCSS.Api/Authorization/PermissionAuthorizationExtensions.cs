using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NoneadCSS.Api.Authentication;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Api.Authorization;

public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ScopeAuthorizationHandler>();

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();

        return services;
    }
}

public class PermissionAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var policy = await base.GetPolicyAsync(policyName);
        if (policy != null)
        {
            return policy;
        }

        if (policyName.StartsWith("Permission_"))
        {
            var permission = policyName["Permission_".Length..];
            var requirement = new PermissionRequirement(permission);

            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(requirement)
                .Build();
        }

        if (policyName.StartsWith("Scope_"))
        {
            var scope = policyName["Scope_".Length..];
            var requirement = new ScopeRequirement(scope);

            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(requirement)
                .Build();
        }

        return null;
    }
}