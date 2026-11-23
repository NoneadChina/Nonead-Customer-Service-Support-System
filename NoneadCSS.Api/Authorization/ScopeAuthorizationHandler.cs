using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Api.Authorization;

/// <summary>
/// Scope 授权要求 - 要求当前请求的 API Key 包含指定 Scope
/// </summary>
public class ScopeRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// 必需的 Scope
    /// </summary>
    public string Scope { get; }

    public ScopeRequirement(string scope)
    {
        Scope = scope;
    }
}

/// <summary>
/// API Key Scope 校验特性 - 配合 [Scope("xxx")] 限制 API Key 必须具备相应 Scope
/// </summary>
public class ScopeAttribute : AuthorizeAttribute
{
    public ScopeAttribute(string scope)
    {
        Policy = $"Scope_{scope}";
    }
}

/// <summary>
/// Scope 授权处理器 - 检查当前用户（API Key）的 scope claim 是否包含所需 scope
/// </summary>
public class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    private readonly ILogger<ScopeAuthorizationHandler> _logger;

    public ScopeAuthorizationHandler(ILogger<ScopeAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        // HIGH-5 修复：[Scope("xxx")] 对所有身份（JWT / API Key）一视同仁。
        // 历史逻辑是"JWT 用户默认拥有所有 scope"，这会让误用 [Scope] 的接口在 JWT 身份下
        // 丧失最小权限约束。现改为：
        //   1. 未认证 → 失败。
        //   2. 请求方显式声明了 scope claim（无论 API Key 或 JWT）→ 必须匹配（含通配符 *）。
        //   3. 无 scope claim 但已通过身份认证（典型 JWT 登录用户，使用角色/权限特性）
        //      → 放行，保持与现有 [RequireSuperAdmin]/[Permission] 的组合语义兼容。
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Fail(new AuthorizationFailureReason(this, "未认证的用户无法通过 Scope 校验"));
            return Task.CompletedTask;
        }

        var scopeClaims = user.FindAll("scope").ToList();
        if (scopeClaims.Count > 0)
        {
            // 显式声明了 scope → 严格匹配，拒绝"scope 写了 A 却要求 B 也过"。
            var scopes = scopeClaims
                .SelectMany(c => (c.Value ?? string.Empty).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (scopes.Contains(requirement.Scope) || scopes.Contains("*"))
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("身份已认证但缺少必需 Scope：{Scope}，实际 scopes={ActualScopes}",
                    requirement.Scope, string.Join(",", scopes));
                context.Fail(new AuthorizationFailureReason(this,
                    $"缺少必需的 Scope：{requirement.Scope}"));
            }
        }
        else
        {
            // S9: 无 scope claim 的已认证身份（JWT 登录用户 / 无 scopes 的 API Key）→ 一律拒绝。
            // 原逻辑"放行"会让误用 [Scope] 的端点对任意登录用户放开（最小权限失效）。
            // 当前无任何端点使用 [Scope]，收紧无回归；使用 [Scope] 时必须显式声明并匹配。
            _logger.LogWarning("身份已认证但未声明任何 scope，拒绝访问 Scope={Scope} 的端点", requirement.Scope);
            context.Fail(new AuthorizationFailureReason(this,
                $"缺少必需的 Scope：{requirement.Scope}"));
        }

        return Task.CompletedTask;
    }
}
