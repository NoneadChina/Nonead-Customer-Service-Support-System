using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Api.Filters;

/// <summary>
/// 操作审计过滤器 - 自动记录控制器操作日志
/// </summary>
public class AuditActionFilter : IAsyncActionFilter
{
    private readonly IAuditService _auditService;
    private readonly ILogger<AuditActionFilter> _logger;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="auditService">审计服务</param>
    /// <param name="logger">日志记录器</param>
    public AuditActionFilter(IAuditService auditService, ILogger<AuditActionFilter> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// 动作执行时的拦截
    /// </summary>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executedContext = await next();

        var httpContext = context.HttpContext;
        var request = httpContext.Request;

        // 获取操作信息
        var controllerName = context.ActionDescriptor.RouteValues["controller"] ?? string.Empty;
        var actionName = context.ActionDescriptor.RouteValues["action"] ?? string.Empty;
        var httpMethod = request.Method;

        // 跳过不需要记录的操作
        if (ShouldSkip(controllerName, actionName, httpMethod))
        {
            return;
        }

        // 获取实体类型（从控制器名称推断）
        var entityType = GetEntityTypeFromController(controllerName);

        // 获取操作类型
        var actionType = GetActionType(actionName, httpMethod);

        // 获取实体ID（从路由参数中提取）
        int? entityId = GetEntityIdFromRoute(context);

        // 只记录有意义的操作
        if (string.IsNullOrEmpty(actionType))
        {
            return;
        }

        try
        {
            // 记录操作日志
            var oldValues = string.Empty;
            var newValues = string.Empty;

            // 如果是POST/PUT且有请求体，记录参数概要
            if ((httpMethod == "POST" || httpMethod == "PUT") && context.ActionArguments.Count > 0)
            {
                var args = new Dictionary<string, object?>();
                foreach (var arg in context.ActionArguments)
                {
                    // 过滤敏感字段
                    var sanitizedValue = SanitizeSensitiveData(arg.Value);
                    // 跳过过大的对象，避免日志膨胀
                    var json = JsonSerializer.Serialize(sanitizedValue);
                    if (json.Length < 2000)
                    {
                        args[arg.Key] = sanitizedValue;
                    }
                }

                // 如果有未处理的异常，附加异常信息到 newValues
                // 安全要求：审计日志不写原始 Exception.Message（可能含 SQL/路径/凭据等敏感信息），
                // 仅记录异常类型 + 结构化错误码 + traceId，详细 Message 留给 Serilog 日志文件
                if (executedContext.Exception != null && !executedContext.ExceptionHandled)
                {
                    args["_error"] = new
                    {
                        type = executedContext.Exception.GetType().Name,
                        code = MapExceptionToErrorCode(executedContext.Exception),
                        traceId = httpContext.TraceIdentifier
                    };
                }

                newValues = JsonSerializer.Serialize(args);
            }
            else if (executedContext.Exception != null && !executedContext.ExceptionHandled)
            {
                // 对于非POST/PUT请求的异常，单独记录异常信息
                // 同样不写原始 Exception.Message，仅记录类型 + 错误码 + traceId
                var errorInfo = new Dictionary<string, object?>
                {
                    ["_error"] = new
                    {
                        type = executedContext.Exception.GetType().Name,
                        code = MapExceptionToErrorCode(executedContext.Exception),
                        traceId = httpContext.TraceIdentifier
                    }
                };
                newValues = JsonSerializer.Serialize(errorInfo);
            }

            await _auditService.LogAsync(
                action: actionType,
                entityType: entityType,
                entityId: entityId,
                oldValues: oldValues,
                newValues: newValues,
                cancellationToken: httpContext.RequestAborted
            );
        }
        catch (Exception ex)
        {
            // 审计日志失败不应影响正常业务，但需要记录以便排查
            _logger.LogError(ex, "记录审计日志失败: Action={Action}, EntityType={EntityType}, EntityId={EntityId}",
                actionType, entityType, entityId);
        }
    }

    /// <summary>
    /// 判断是否应跳过该操作的审计记录
    /// </summary>
    private static bool ShouldSkip(string controller, string action, string method)
    {
        // 跳过审计日志控制器本身，避免循环记录
        if (controller.Equals("AuditLogs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 跳过GET查询操作（由SaveChanges拦截器记录实体变更）
        if (method == "GET" && !IsDetailAction(action))
        {
            return true;
        }

        // 跳过认证相关的查询操作
        if (controller.Equals("Auth", StringComparison.OrdinalIgnoreCase) &&
            action.Equals("GetProfile", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 登录/登出在 AuthController 中显式调用 IAuditService.LogAsync（并传入真实 userId/ip/ua）。
        // 此处跳过，避免重复记录：
        // - Login：过滤器执行时 HttpContext.User 尚未填充，若在过滤器记录会导致 UserId=null，
        //   前端按 userId 查询的登录历史永远匹配不到。
        // - Logout：过滤器执行的顺序位于 JWT 认证中间件之后、但 sign-out 清理 cookie 之前，
        //   虽能拿到 User，但为了保持 login/logout 两条记录在同一入口写入，也统一在 AuthController 内写。
        var lowerAction = action.ToLowerInvariant();
        if (controller.Equals("Auth", StringComparison.OrdinalIgnoreCase) &&
            (lowerAction.Contains("login") || lowerAction == "signin" ||
             lowerAction.Contains("logout") || lowerAction == "signout"))
        {
            return true;
        }

        // 跳过健康检查等
        if (controller.Equals("Health", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 判断是否为详情操作
    /// </summary>
    private static bool IsDetailAction(string action)
    {
        var detailActions = new[] { "GetById", "Detail", "Details", "Get", "GetConversations" };
        return detailActions.Any(a =>
            action.Equals(a, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 从控制器名称获取实体类型
    /// </summary>
    private static string GetEntityTypeFromController(string controllerName)
    {
        // 移除"Controller"后缀和复数形式
        return controllerName.Replace("Controller", string.Empty).TrimEnd('s');
    }

    /// <summary>
    /// 根据操作名和HTTP方法获取操作类型
    /// </summary>
    private static string GetActionType(string actionName, string httpMethod)
    {
        // 显式命名的操作优先
        var actionLower = actionName.ToLowerInvariant();
        if (actionLower.Contains("login") || actionLower == "signin") return "login";
        if (actionLower.Contains("logout") || actionLower == "signout") return "logout";
        if (actionLower.Contains("reply")) return "reply";
        if (actionLower.Contains("assign")) return "assign";
        if (actionLower.Contains("status") || actionLower.Contains("changestatus")) return "status_change";
        if (actionLower.Contains("note")) return "note";
        if (actionLower.Contains("password")) return "password_change";
        if (actionLower.Contains("activate") || actionLower.Contains("deactivate")) return "status_change";

        // 根据HTTP方法推断
        return httpMethod switch
        {
            "POST" => actionLower.Contains("update") || actionLower.Contains("edit") ? "update" : "create",
            "PUT" => "update",
            "PATCH" => "update",
            "DELETE" => "delete",
            _ => string.Empty
        };
    }

    /// <summary>
    /// 从路由参数中提取实体ID
    /// </summary>
    private static int? GetEntityIdFromRoute(ActionExecutingContext context)
    {
        // 检查路由参数中的 id
        if (context.RouteData.Values.TryGetValue("id", out var idValue) && idValue != null)
        {
            if (int.TryParse(idValue.ToString(), out var id))
            {
                return id;
            }
        }

        // 检查Action参数中的Id
        if (context.ActionArguments.TryGetValue("id", out var argId) && argId is int argInt)
        {
            return argInt;
        }
        if (context.ActionArguments.TryGetValue("userId", out var userId) && userId is int userInt)
        {
            return userInt;
        }
        if (context.ActionArguments.TryGetValue("conversationId", out var convId) && convId is int convInt)
        {
            return convInt;
        }

        return null;
    }

    /// <summary>
    /// 过滤对象中的敏感字段，避免密码等敏感信息被记录到审计日志
    /// </summary>
    /// <param name="obj">要过滤的对象</param>
    /// <returns>过滤后的对象（如果是复杂类型则返回 Dictionary，否则返回原值）</returns>
    private static object? SanitizeSensitiveData(object? obj)
    {
        if (obj == null)
        {
            return null;
        }

        var type = obj.GetType();

        // 简单类型直接返回
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(Guid) || type.IsEnum)
        {
            return obj;
        }

        // 复杂类型：反射获取属性并过滤敏感字段
        var dict = new Dictionary<string, object?>();
        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var propName = prop.Name;
            if (IsSensitiveProperty(propName))
            {
                dict[propName] = "***";
            }
            else
            {
                try
                {
                    var value = prop.GetValue(obj);
                    dict[propName] = value;
                }
                catch
                {
                    dict[propName] = null;
                }
            }
        }

        return dict.Count > 0 ? dict : obj;
    }

    /// <summary>
    /// 判断属性是否为敏感字段
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <returns>是否为敏感字段</returns>
    private static bool IsSensitiveProperty(string propertyName)
    {
        var sensitiveNames = new[]
        {
            "Password", "PasswordHash", "PasswordSalt",
            "Token", "AccessToken", "RefreshToken",
            "Secret", "SecretKey", "ApiKey",
            "OauthAccessToken", "OauthRefreshToken",
            "InPassword", "OutPassword", "OldPassword",
            "NewPassword", "ConfirmPassword", "CurrentPassword"
        };
        return sensitiveNames.Any(s =>
            propertyName.Equals(s, StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 将异常映射为结构化错误码，避免在审计日志中泄露原始 Exception.Message。
    /// </summary>
    private static string MapExceptionToErrorCode(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => "unauthorized",
            ArgumentException => "validation_error",
            KeyNotFoundException => "not_found",
            InvalidOperationException => "conflict",
            TimeoutException => "timeout",
            _ => "internal_error"
        };
    }
}
