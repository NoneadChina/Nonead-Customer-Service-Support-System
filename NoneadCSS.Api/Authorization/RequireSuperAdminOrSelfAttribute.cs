using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NoneadCSS.Domain.Enums;
using System.Security.Claims;
using System.Text.Json;

namespace NoneadCSS.Api.Authorization;

/// <summary>
/// 要求超管或访问自身资源（userId 路由参数/查询/Body）的授权特性
/// 防止 IDOR 攻击：仅允许超管查询任意用户的资源，
/// 或用户访问自身的资源。
///
/// 优先级：
/// 1. 未登录 → 401 Unauthorized
/// 2. 超管 → 直接放行
/// 3. 路由 / 查询 / Body 中任意位置包含 userId 字段且与当前用户 ID 匹配 → 放行
/// 4. 其余情况 → 403 Forbid
///
/// 适用场景：/api/users/{userId}/xxx、/api/roles/user/{userId}/xxx 等接口。
/// </summary>
public class RequireSuperAdminOrSelfAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>
    /// 允许的 userId 参数名（路由、查询、Body 字段名）
    /// </summary>
    private static readonly string[] UserIdParamNames = new[]
    {
        "userId", "id", "uid", "targetUserId", "user_id", "UserId"
    };

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // 超管直接放行
        if (user.IsInRole(((int)UserRoleType.SuperAdmin).ToString()))
        {
            return;
        }

        // 提取当前用户 ID
        var currentUserIdStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(currentUserIdStr, out var currentUserId) || currentUserId <= 0)
        {
            context.Result = new ForbidResult();
            return;
        }

        // 1. 先检查路由参数
        if (TryGetRouteUserId(context, out var routeUserId))
        {
            if (routeUserId == currentUserId) return;
            context.Result = new ForbidResult();
            return;
        }

        // 2. 再检查查询字符串
        if (TryGetQueryUserId(context, out var queryUserId))
        {
            if (queryUserId == currentUserId) return;
            context.Result = new ForbidResult();
            return;
        }

        // 3. 最后检查 Body（仅对 POST/PUT/PATCH 检查，且要求 body 已被读取）
        if (TryGetBodyUserId(context, out var bodyUserId))
        {
            if (bodyUserId == currentUserId) return;
            context.Result = new ForbidResult();
            return;
        }

        // 找不到 userId，超管已通过则到这里只能拒绝
        context.Result = new ForbidResult();
    }

    private static bool TryGetRouteUserId(AuthorizationFilterContext context, out int userId)
    {
        userId = 0;
        foreach (var name in UserIdParamNames)
        {
            if (context.RouteData.Values.TryGetValue(name, out var val)
                && int.TryParse(val?.ToString(), out var parsed)
                && parsed > 0)
            {
                userId = parsed;
                return true;
            }
        }
        return false;
    }

    private static bool TryGetQueryUserId(AuthorizationFilterContext context, out int userId)
    {
        userId = 0;
        var query = context.HttpContext.Request.Query;
        foreach (var name in UserIdParamNames)
        {
            if (query.TryGetValue(name, out var values)
                && int.TryParse(values.ToString(), out var parsed)
                && parsed > 0)
            {
                userId = parsed;
                return true;
            }
        }
        return false;
    }

    private static bool TryGetBodyUserId(AuthorizationFilterContext context, out int userId)
    {
        userId = 0;
        try
        {
            // 仅对有 body 的请求做检查
            var method = context.HttpContext.Request.Method;
            if (method != HttpMethods.Post && method != HttpMethods.Put && method != HttpMethods.Patch)
            {
                return false;
            }

            // 授权过滤器在模型绑定之前执行，无法通过 ActionArguments 获取已绑定参数。
            // 启用请求体缓冲，读取 JSON body 并解析 userId 字段。
            var request = context.HttpContext.Request;
            if (request.ContentLength is null or 0) return false;
            if (request.ContentType == null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) return false;

            // HIGH-2 / DoS：Body 大小硬上限 1 MB。
            // 未带 Content-Length 或 >1MB 的请求直接跳过，
            // 由后续模型绑定 / FormOptions / Kestrel MaxRequestBodySize 自行处理，
            // 避免在授权过滤器阶段被攻击者用 10GB 流式请求挂死。
            const int MaxBodyBytes = 1024 * 1024;
            if (request.ContentLength > MaxBodyBytes)
            {
                return false;
            }

            // 1 MB 缓冲限流：内存压力由 Kestrel 的 MaxRequestBodySize + 我们的 ContentLength 双保险兜底。
            request.EnableBuffering(bufferThreshold: MaxBodyBytes, bufferLimit: MaxBodyBytes);
            request.Body.Position = 0;

            // 同步读取 Body（IAuthorizationFilter 是同步接口，不支持 async/await）。
            // 1MB 缓冲限流：内存压力由 Kestrel 的 MaxRequestBodySize + 我们的 ContentLength 双保险兜底。
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            // 改用同步 ReadToEnd()：避免 async→sync 桥接的线程池饥饿风险。
            var bodyStr = reader.ReadToEnd();
            request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(bodyStr)) return false;

            // 流式 JSON DOM 解析：不预分配字符串之外的大对象
            using var doc = JsonDocument.Parse(bodyStr);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            foreach (var name in UserIdParamNames)
            {
                if (doc.RootElement.TryGetProperty(name, out var element)
                    && element.ValueKind == JsonValueKind.Number
                    && element.TryGetInt32(out var parsed)
                    && parsed > 0)
                {
                    userId = parsed;
                    return true;
                }
                if (doc.RootElement.TryGetProperty(name, out element)
                    && element.ValueKind == JsonValueKind.String
                    && int.TryParse(element.GetString(), out parsed)
                    && parsed > 0)
                {
                    userId = parsed;
                    return true;
                }
            }
        }
        catch
        {
            // 解析失败时静默忽略，避免对正常请求产生影响
        }
        return false;
    }
}
