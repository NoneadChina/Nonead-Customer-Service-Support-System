using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NoneadCSS.Api.Localization;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Exceptions;
using Serilog;

namespace NoneadCSS.Api.Middleware;

/// <summary>
/// 全局异常处理中间件，统一捕获并处理所有未被业务代码捕获的异常。
/// 记录详细错误日志，并返回标准化的错误响应格式。
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="next">下一个中间件</param>
    public GlobalExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// 中间件执行入口
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
        {
            // 客户端取消请求（浏览器关闭、超时等），不记录为错误，返回 499
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// 处理异常，记录日志并返回标准化错误响应
    /// </summary>
    /// <param name="context">HTTP 上下文</param>
    /// <param name="exception">异常对象</param>
    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;
        var path = context.Request.Path;
        var method = context.Request.Method;

        if (context.Response.HasStarted)
        {
            // L-1 修复：对异常消息做基础脱敏后再记录，避免密码/密钥/连接字符串等敏感信息写入日志
            LogException(exception,
                "全局异常捕获（响应已启动）| TraceId: {TraceId} | {Method} {Path} | 错误类型: {ExceptionType} | 消息: {Message}",
                traceId, method, path, exception);

            return Task.CompletedTask;
        }

        // L-1 修复：对异常消息做基础脱敏后再记录，避免密码/密钥/连接字符串等敏感信息写入日志
        LogException(exception,
            "全局异常捕获 | TraceId: {TraceId} | {Method} {Path} | 错误类型: {ExceptionType} | 消息: {Message}",
            traceId, method, path, exception);

        var statusCode = GetStatusCode(exception);
        var message = GetSafeMessage(exception, statusCode);

        // P0: 日历冲突异常附带冲突事件详情，供前端展示冲突信息；乐观锁冲突附带最新快照
        object responseData;
        if (exception is CalendarConflictException conflictEx)
        {
            responseData = new
            {
                traceId = traceId,
                path = path.Value,
                errorType = "calendar_conflict",
                overlapMinutes = conflictEx.OverlapMinutes,
                conflicts = conflictEx.Conflicts.Select(c => new
                {
                    id = c.Id,
                    title = c.Title,
                    startAt = c.StartAt.ToString("O"),
                    endAt = c.EndAt?.ToString("O")
                })
            };
        }
        else if (exception is ConcurrencyException concurrencyEx)
        {
            // 通用 409 契约：entityType + entityId + latest；前端按 entityType 路由到对应 store 刷新逻辑
            // 兼容 CalendarConcurrencyException（继承自 ConcurrencyException，entityType="CalendarEvent"），
            // 原有前端判断 errorType === "calendar_concurrency_conflict" 的分支依然命中，
            // 新模块判断 entityType 字段即可完成区分。
            var isCalendar = string.Equals(concurrencyEx.EntityType, "CalendarEvent", StringComparison.OrdinalIgnoreCase);
            responseData = new
            {
                traceId = traceId,
                path = path.Value,
                errorType = isCalendar ? "calendar_concurrency_conflict" : "concurrency_conflict",
                entityType = concurrencyEx.EntityType,
                entityId = concurrencyEx.EntityId,
                // 保留 eventId 字段以便旧前端（calendar 模块）兼容读取
                eventId = concurrencyEx.EntityId,
                latest = concurrencyEx.Latest
            };
        }
        else
        {
            responseData = new
            {
                traceId = traceId,
                path = path.Value,
                errorType = GetErrorType(statusCode)
            };
        }

        var response = new ApiResponse
        {
            Success = false,
            Code = statusCode,
            // 出口词典替换：状态码与 errorType 已在上文按异常类型定完，这里只换展示文案
            Message = ApiMessageLocalizer.Localize(message, ApiRequestCulture.Of(context)) ?? message,
            Data = responseData
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var result = JsonSerializer.Serialize(response, JsonOptions);

        return context.Response.WriteAsync(result);
    }

    /// <summary>
    /// 按异常类型选择日志级别：
    ///   - 预期业务异常（乐观锁并发冲突 409 / 日程时间冲突 409）→ Warning，避免正常业务拒绝污染错误日志
    ///   - 其余 → Error
    /// </summary>
    private static void LogException(Exception exception, string messageTemplate, string traceId, string method, PathString path, Exception ex)
    {
        if (exception is ConcurrencyException or CalendarConflictException)
        {
            // 业务预期冲突：客户端应刷新后重试，属正常交互而非系统错误
            Log.Warning(exception,
                "全局异常捕获（业务预期 409）| TraceId: {TraceId} | {Method} {Path} | 错误类型: {ExceptionType} | 消息: {Message}",
                traceId, method, path, exception.GetType().Name, SanitizeMessage(exception.Message));
        }
        else
        {
            Log.Error(exception,
                messageTemplate,
                traceId, method, path, exception.GetType().Name, SanitizeMessage(exception.Message));
        }
    }

    /// <summary>
    /// 共享的 JSON 序列化选项，避免每次请求重复创建
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// L-1 修复：敏感数据脱敏正则。
    /// 匹配密码、密钥、令牌、连接字符串等常见敏感键值对，将值部分替换为 ***。
    /// 覆盖场景：
    ///   - 连接字符串：Password=xxx; / Pwd=xxx; / User ID=xxx;
    ///   - 配置键值：secret=xxx / api_key=xxx / token=xxx / bearer xxx
    ///   - JSON 片段："password":"xxx" / "secret":"xxx"
    /// </summary>
    private static readonly Regex SensitiveDataRegex = new(
        @"(?i)((?:password|pwd|passwd|secret|api[_-]?key|access[_-]?token|auth[_-]?token|bearer|authorization|credential|private[_-]?key|client[_-]?secret)\s*[=:][""']?\s*)([^\s;,""'\]}]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// L-1 修复：对异常消息做基础脱敏，将敏感键值对中的值替换为 ***。
    /// 仅做模式匹配，不解析语义；无法识别的敏感信息仍可能泄露，敏感数据不应依赖此方法兜底。
    /// </summary>
    /// <param name="message">原始异常消息</param>
    /// <returns>脱敏后的消息</returns>
    private static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return message ?? string.Empty;

        return SensitiveDataRegex.Replace(message, "$1***");
    }

    /// <summary>
    /// 根据异常类型获取对应的 HTTP 状态码
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <returns>HTTP 状态码</returns>
    private static int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException or ArgumentException => (int)HttpStatusCode.BadRequest,
            // JsonException: 请求体 JSON 格式错误或手动反序列化失败，属于客户端输入问题
            JsonException => (int)HttpStatusCode.BadRequest,
            // CryptographicException: 解密失败（密文格式错误/认证标签不匹配），通常由客户端传入的加密数据无效导致
            CryptographicException => (int)HttpStatusCode.BadRequest,
            // P0: 日历事件时间硬冲突（重叠 > 15min），返回 409 + 冲突事件列表
            CalendarConflictException => (int)HttpStatusCode.Conflict,
            // P3: 通用实体乐观锁冲突（Calendar/Customer/ChatSession/User/ApiKey/Webhook 等）返回 409 + 最新快照
            ConcurrencyException => (int)HttpStatusCode.Conflict,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            // SecurityTokenException 及其子类（ExpiredException/InvalidSignatureException 等）: JWT 令牌无效或过期
            SecurityTokenException => (int)HttpStatusCode.Unauthorized,
            FileNotFoundException or KeyNotFoundException => (int)HttpStatusCode.NotFound,
            InvalidOperationException => (int)HttpStatusCode.Conflict,
            DbUpdateConcurrencyException => 409, // 并发冲突
            // L-Q4：DbUpdateException 不一定都是约束冲突，也可能是连接失败等暂时性错误。
            // 通过 InnerException 消息判断：含 UNIQUE/FOREIGN KEY/duplicate key 视为约束冲突返回 409，
            // 其余（如连接超时、磁盘满）返回 503 Service Unavailable，让客户端区分"数据冲突"与"服务暂时不可用"。
            DbUpdateException dbEx => IsConstraintViolation(dbEx) ? 409 : 503,
            TimeoutException => (int)HttpStatusCode.GatewayTimeout,
            HttpRequestException => (int)HttpStatusCode.BadGateway, // 外部服务调用失败
            _ => (int)HttpStatusCode.InternalServerError
        };
    }

    /// <summary>
    /// 判断 DbUpdateException 是否由数据库约束冲突引起（SQLite 与 SQL Server 兼容）。
    /// 约束冲突属于客户端可纠正的输入问题（409）；其余视为服务端暂时性故障（503）。
    /// </summary>
    private static bool IsConstraintViolation(DbUpdateException ex)
    {
        var msg = (ex.InnerException?.Message ?? ex.Message)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(msg)) return false;
        return msg.Contains("unique constraint")
               || msg.Contains("foreign key")
               || msg.Contains("duplicate key")
               || msg.Contains("violation of unique")
               || msg.Contains("constraint failed")
               || msg.Contains("primary key");
    }

    /// <summary>
    /// 获取安全的错误消息，所有异常均隐藏内部细节，仅返回通用提示
    /// </summary>
    /// <param name="exception">异常对象</param>
    /// <param name="statusCode">状态码</param>
    /// <returns>安全的错误消息</returns>
    private static string GetSafeMessage(Exception exception, int statusCode)
    {
        // P0: 日历冲突返回具体冲突描述（安全信息，不含敏感数据）
        if (exception is CalendarConflictException conflictEx)
        {
            return $"时间冲突：与 {conflictEx.Conflicts.Count} 个已有事件重叠 {conflictEx.OverlapMinutes} 分钟，请调整时间后重试。";
        }
        // P3: 通用乐观锁冲突给出可操作提示（包含 Calendar/客户/会话/用户/API Key/Webhook 等所有实体）
        if (exception is ConcurrencyException)
        {
            return "数据已被其他用户修改，请刷新后重新编辑。";
        }

        // T000105：参数校验类异常（ArgumentException/JsonException 映射为 400）的消息为开发者书写的
        // 安全中文提示（如 "URL 不能指向 192.168.x.x 内网地址"、"事件列表不能为空"），
        // 直接返回具体原因，避免用户面对笼统的"请求参数无效"无从修正；其余异常仍走下方通用文案防泄露。
        if (statusCode == (int)HttpStatusCode.BadRequest
            && (exception is ArgumentException or JsonException)
            && !string.IsNullOrWhiteSpace(exception.Message))
        {
            return SanitizeMessage(exception.Message);
        }

        return statusCode switch
        {
            400 => "请求参数无效，请检查输入。",
            401 => "未授权，请先登录。",
            403 => "没有权限执行此操作。",
            404 => "请求的资源不存在。",
            409 => "操作冲突，数据可能已被修改或存在约束冲突，请重试。",
            502 => "外部服务暂时不可用，请稍后重试。",
            503 => "服务暂时不可用，请稍后重试。",
            504 => "请求超时，请稍后重试。",
            _ => "服务器内部错误，请稍后重试或联系管理员。"
        };
    }

    private static string GetErrorType(int statusCode)
    {
        return statusCode switch
        {
            400 => "bad_request",
            401 => "unauthorized",
            403 => "forbidden",
            404 => "not_found",
            409 => "conflict",
            429 => "too_many_requests",
            502 => "bad_gateway",
            503 => "service_unavailable",
            504 => "gateway_timeout",
            500 => "internal_error",
            _ => "error"
        };
    }
}
