using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Common;

namespace NoneadCSS.Api.Filters;

/// <summary>
/// 出站响应消毒过滤器（纵深防御）：对控制器返回的 ObjectResult 值做递归 HTML 消毒。
/// 入站拦截器已对入库字符串做白名单消毒，本过滤器兜底处理那些绕过入站、
/// 直接由控制器/第三方数据构造的响应内容。
/// 仅对"明显包含完整 HTML 标签"的字符串触发消毒，保留纯文本原样，避免误编码。
/// 敏感属性（密码、令牌、密钥等）一律跳过，防御性不对其做任何变换。
/// 消毒失败不阻断响应，仅记录告警。
/// </summary>
public class HtmlSanitizingResponseFilter : IAsyncResultFilter
{
    private readonly ILogger<HtmlSanitizingResponseFilter> _logger;

    /// <summary>
    /// 敏感属性名黑名单：这些字段可能是凭据/令牌/密钥，消毒可能破坏其语义，必须跳过。
    /// 与入站拦截器 HtmlSanitizingSaveChangesInterceptor 保持一致。
    /// </summary>
    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "PasswordHash", "RefreshToken", "AccessToken",
        "OutPassword", "InPassword", "OauthAccessToken", "OauthRefreshToken",
        "Secret", "ApiKey", "ApiSecret", "ConnectionString", "Token",
        "SetupToken", "SecretKey", "PrivateKey", "Signature", "Hash"
    };

    /// <summary>
    /// 识别"明显包含 HTML 标签"的字符串：
    /// 匹配 &lt;tag ...&gt; 、&lt;/tag&gt; 这类完整标签。纯文本中的孤立 &lt;（如 "a &lt; b"）不会命中，
    /// 从而避免对非富文本字段做转义。
    /// 与入站拦截器 HtmlSanitizingSaveChangesInterceptor 保持一致。
    /// </summary>
    private static readonly Regex HtmlTagRegex = new(
        @"<[a-zA-Z][^>]*>|</[a-zA-Z][^>]*>",
        RegexOptions.Compiled);

    private static readonly int MaxDepth = 8;
    private static readonly int MaxItems = 1000;

    public HtmlSanitizingResponseFilter(ILogger<HtmlSanitizingResponseFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: not null } objectResult)
        {
            try
            {
                var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                SanitizeObject(objectResult.Value, null, visited, 0);
            }
            catch (Exception ex)
            {
                // 消毒失败不阻断响应，仅记录告警
                _logger.LogError(ex, "出站 HTML 消毒过滤器执行失败，跳过本次消毒（不影响响应）");
            }
        }

        await next();
    }

    private void SanitizeObject(object? obj, string? name, HashSet<object> visited, int depth)
    {
        if (obj == null || depth > MaxDepth)
        {
            return;
        }

        var objType = obj.GetType();
        if (IsLeafType(objType) || !visited.Add(obj))
        {
            return;
        }

        if (obj is IDictionary dictionary)
        {
            SanitizeDictionary(dictionary, visited, depth);
            return;
        }

        if (obj is IEnumerable enumerable)
        {
            SanitizeEnumerable(enumerable, visited, depth);
            return;
        }

        foreach (var property in objType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyName = property.Name;
            if (SensitiveProperties.Contains(propertyName))
            {
                continue;
            }

            object? currentValue;
            try
            {
                currentValue = property.GetValue(obj);
            }
            catch
            {
                continue;
            }

            if (currentValue == null)
            {
                continue;
            }

            if (currentValue is string str)
            {
                SanitizeStringProperty(property, obj, str);
            }
            else if (!IsLeafType(currentValue.GetType()))
            {
                SanitizeObject(currentValue, propertyName, visited, depth + 1);
            }
        }
    }

    private void SanitizeDictionary(IDictionary dictionary, HashSet<object> visited, int depth)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        var keys = new List<object?>();
        foreach (var key in dictionary.Keys)
        {
            keys.Add(key);
        }

        var processed = 0;
        foreach (var key in keys)
        {
            if (processed >= MaxItems)
            {
                break;
            }

            var keyName = key?.ToString();
            if (keyName != null && SensitiveProperties.Contains(keyName))
            {
                continue;
            }

            if (key == null || !dictionary.Contains(key))
            {
                continue;
            }

            var value = dictionary[key];
            if (value is string str)
            {
                var sanitized = HtmlSanitizerHelper.Sanitize(str, _logger);
                if (!string.Equals(sanitized, str, StringComparison.Ordinal))
                {
                    try
                    {
                        dictionary[key] = sanitized;
                    }
                    catch
                    {
                        // 只读集合等无法写入时忽略
                    }
                }
            }
            else if (value != null && !IsLeafType(value.GetType()))
            {
                SanitizeObject(value, keyName, visited, depth + 1);
            }

            processed++;
        }
    }

    private void SanitizeEnumerable(IEnumerable enumerable, HashSet<object> visited, int depth)
    {
        if (depth >= MaxDepth || enumerable is not IList list)
        {
            return;
        }

        var count = Math.Min(list.Count, MaxItems);
        for (var i = 0; i < count; i++)
        {
            var item = list[i];
            if (item is string str)
            {
                var sanitized = HtmlSanitizerHelper.Sanitize(str, _logger);
                if (!string.Equals(sanitized, str, StringComparison.Ordinal))
                {
                    try
                    {
                        list[i] = sanitized;
                    }
                    catch
                    {
                        // 固定大小/只读集合等无法写入时忽略
                    }
                }
            }
            else if (item != null && !IsLeafType(item.GetType()))
            {
                SanitizeObject(item, null, visited, depth + 1);
            }
        }
    }

    private void SanitizeStringProperty(PropertyInfo property, object target, string value)
    {
        if (!ShouldSanitize(value))
        {
            return;
        }

        var sanitized = HtmlSanitizerHelper.Sanitize(value, _logger);
        if (string.Equals(sanitized, value, StringComparison.Ordinal))
        {
            return;
        }

        if (!property.CanWrite)
        {
            return;
        }

        try
        {
            property.SetValue(target, sanitized);
        }
        catch
        {
            // init-only / 只读属性等无法写入时忽略
        }
    }

    private static bool ShouldSanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return HtmlTagRegex.IsMatch(value);
    }

    private static bool IsLeafType(Type type)
    {
        if (type == typeof(string))
        {
            return true;
        }

        if (type.IsValueType || type.IsPrimitive || type.IsEnum)
        {
            return true;
        }

        if (typeof(Stream).IsAssignableFrom(type))
        {
            return true;
        }

        if (type == typeof(byte[]))
        {
            return true;
        }

        return false;
    }
}
