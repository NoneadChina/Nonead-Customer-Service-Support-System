using System.Collections.Concurrent;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NoneadCSS.Api.Filters;

public class ApiResponseWrapperFilter : IAsyncResultFilter
{
    // 信封级 message（每个响应都携带）按请求文化取词；由 DI 注入（见 Program.cs 的
    // `options.Filters.Add<ApiResponseWrapperFilter>()`，属类型过滤器 ⇒ 走构造注入）。
    private readonly ILocalizationService _localizer;

    public ApiResponseWrapperFilter(ILocalizationService localizer)
    {
        _localizer = localizer;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        var skipWrap = endpoint?.Metadata.GetMetadata<SkipApiResponseWrapperAttribute>();
        if (skipWrap != null)
        {
            await next();
            return;
        }

        if (context.Result is ObjectResult objectResult)
        {
            var value = objectResult.Value;

            if (value != null && IsAlreadyWrapped(value.GetType()))
            {
                // 业务 DTO 自带的信封（含 AuthService 这类 401/403 直出中文 Message）不再二次包装，
                // 但英文界面下仍需在出口把它的 Message / ErrorMessage 换成词典英文；只处理错误分支。
                var alreadyWrappedStatus = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode;
                if (alreadyWrappedStatus >= 400)
                {
                    NoneadCSS.Api.Localization.ApiMessageLocalizer.LocalizeMessageProperties(value, context.HttpContext);
                }

                await next();
                return;
            }

            var statusCode = objectResult.StatusCode ?? context.HttpContext.Response.StatusCode;
            var isSuccess = statusCode >= 200 && statusCode < 300;

            string? message = null;
            object? dataValue = value;

            // 成功分支的裸字符串载荷（Ok("中文")）同样没有 Message 属性，只能在出口整体替换；
            // 只换文案本身，不改信封结构，因此两种语言下的响应形状完全一致。
            if (isSuccess && value is string successPayload)
            {
                dataValue = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize(
                    successPayload, NoneadCSS.Api.Localization.ApiRequestCulture.Of(context.HttpContext)) ?? successPayload;
            }

            if (isSuccess)
            {
                message = _localizer["ApiSuccess"];
            }
            else
            {
                if (value is string stringPayload)
                {
                    // NotFound("中文文案") / Unauthorized("中文文案") 这类直接把字符串当载荷的返回值：
                    // 它没有 Message 属性（反射取不到），也不会被字段白名单走查器碰到，只能在出口整体替换。
                    // 只换 data 里的文案，信封 message 仍走状态码默认文案，保持中英文两种语言下结构一致。
                    dataValue = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize(
                        stringPayload, NoneadCSS.Api.Localization.ApiRequestCulture.Of(context.HttpContext)) ?? stringPayload;
                }

                if (value is { } objValue)
                {
                    var type = objValue.GetType();
                    var messageProp = type.GetProperty("Message") ?? type.GetProperty("message");
                    if (messageProp != null)
                    {
                        var msgValue = messageProp.GetValue(objValue) as string;
                        if (!string.IsNullOrEmpty(msgValue))
                        {
                            message = msgValue;
                        }
                    }
                }
                if (string.IsNullOrEmpty(message))
                {
                    message = GetErrorMessage(statusCode);
                }
                else
                {
                    // 服务层/DTO 硬编码的中文错误文案：词典命中才换，未命中保持原样
                    var originalMessage = message;
                    message = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize(
                        message, NoneadCSS.Api.Localization.ApiRequestCulture.Of(context.HttpContext)) ?? message;

                    // Data 里可能还带一份同样的文案（如 new { message = ... } 之外的 DTO），按字段白名单翻译
                    NoneadCSS.Api.Localization.ApiMessageLocalizer.LocalizeMessageProperties(value, context.HttpContext);

                    if (message != originalMessage && value is { } translatedValue)
                    {
                        // 匿名对象（new { message = "…" }）的属性只读，走查器改不动，
                        // 结果就是信封 message 是英文、data.message 仍是中文。这里把这种载荷摊平成字典再替换，
                        // 序列化出来的 JSON 键值与原对象一致，只是文案换成了英文。
                        dataValue = TryFlattenLocalizedMessage(translatedValue, message) ?? dataValue;
                    }
                }
            }

            var response = new ApiResponse
            {
                Success = isSuccess,
                Code = statusCode,
                Data = dataValue,
                Message = message
            };

            context.Result = new ObjectResult(response)
            {
                StatusCode = statusCode
            };
        }
        else if (context.Result is StatusCodeResult statusCodeResult)
        {
            var statusCode = statusCodeResult.StatusCode;
            var isSuccess = statusCode >= 200 && statusCode < 300;
            var response = new ApiResponse
            {
                Success = isSuccess,
                Code = statusCode,
                Message = isSuccess ? _localizer["ApiSuccess"] : GetErrorMessage(statusCode)
            };
            context.Result = new ObjectResult(response)
            {
                StatusCode = statusCode
            };
        }
        else if (context.Result is EmptyResult)
        {
            var response = new ApiResponse(true, _localizer["ApiSuccess"]);
            context.Result = new ObjectResult(response)
            {
                StatusCode = 200
            };
        }

        await next();
    }

    // 反射结果缓存，避免每次请求重复计算
    private static readonly ConcurrentDictionary<Type, bool> _wrapCache = new();

    private static bool IsAlreadyWrapped(Type type)
    {
        return _wrapCache.GetOrAdd(type, static t =>
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ApiResponse<>))
                return true;
            if (t == typeof(ApiResponse))
                return true;
            if (t.FullName?.StartsWith("NoneadCSS.Application.DTOs.Responses.ApiResponse") == true)
                return true;

            // 检测业务响应对象（包含 bool 类型的 Success/success 属性）
            // 这些对象已自带 success 状态字段（如 LoginResponse、ExecuteMacroResponse、
            // TestWebhookResponse 等业务 DTO，以及 new { success = true, ... } 匿名对象），
            // 不应再被 ApiResponse 包装器二次包装，避免出现：
            //   { success: true, code: 200, data: { success: false, message: "..." }, message: "操作成功" }
            // 这种外层与内层 success/message 语义矛盾的响应结构。
            //
            // 安全性说明：前端 client.ts 的 isApiResponse 判断要求响应同时包含
            // success(bool) + code(number) + data + message(string) 四个字段才会 unwrap，
            // 而这些业务 DTO 和匿名对象均不满足四字段全匹配，不会被误解包。
            var successProp = t.GetProperty("Success") ?? t.GetProperty("success");
            return successProp != null && successProp.PropertyType == typeof(bool);
        });
    }

    /// <summary>
    /// 兜住「只读载荷」这类错误返回：控制器写 new { message = "中文" } 时属性不可写，
    /// 走查器改不动，结果就是信封 Message 是英文而 data.message 仍是中文。
    /// 这里把这种对象摊平成同名字典再替换文案键，序列化出的 JSON 键值与原对象一致，只换文案。
    /// 属性可写的情况（DTO / ProblemDetails）已由走查器处理，这里返回 null 不做多余动作。
    /// </summary>
    private static object? TryFlattenLocalizedMessage(object value, string localizedMessage)
    {
        var type = value.GetType();
        var messageProperty = type.GetProperty("Message") ?? type.GetProperty("message");
        if (messageProperty is null || !messageProperty.CanRead || messageProperty.CanWrite
            || messageProperty.GetIndexParameters().Length > 0)
        {
            return null;
        }

        var flattened = new Dictionary<string, object?>();
        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            flattened[property.Name] = property.GetValue(value);
        }

        flattened[messageProperty.Name] = localizedMessage;
        return flattened;
    }

    private string GetErrorMessage(int statusCode)
    {
        var key = statusCode switch
        {
            400 => "ApiErrorBadRequest",
            401 => "ApiErrorUnauthorized",
            403 => "ApiErrorForbidden",
            404 => "ApiErrorNotFound",
            409 => "ApiErrorConflict",
            429 => "ApiErrorTooManyRequests",
            500 => "ApiErrorInternalServer",
            _ => "ApiErrorGeneric"
        };
        return _localizer[key];
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class SkipApiResponseWrapperAttribute : Attribute
{
}
