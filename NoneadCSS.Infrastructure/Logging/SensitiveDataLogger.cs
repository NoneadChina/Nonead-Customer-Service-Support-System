using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace NoneadCSS.Infrastructure.Logging;

/// <summary>
/// 敏感数据脱敏辅助类
/// </summary>
public static class SensitiveDataHelper
{
    /// <summary>
    /// 敏感字段的键名（不区分大小写匹配）
    /// </summary>
    private static readonly string[] SensitiveKeys = new[]
    {
        "password", "pwd", "pass",
        "token", "accesstoken", "refreshtoken", "auth",
        "secret", "apikey", "api_key", "key",
        "credential", "privatekey"
    };

    /// <summary>
    /// 需要部分脱敏的字段键名
    /// </summary>
    private static readonly string[] PartialMaskKeys = new[]
    {
        "email", "mail"
    };

    /// <summary>
    /// 对单个键值对进行脱敏处理
    /// </summary>
    /// <param name="key">键名</param>
    /// <param name="value">值</param>
    /// <returns>脱敏后的值</returns>
    public static string? Sanitize(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var keyLower = key.ToLowerInvariant().Replace("_", "").Replace("-", "");

        // 完全脱敏：密码、Token、密钥等
        if (SensitiveKeys.Any(s => keyLower.Contains(s)))
        {
            return "***";
        }

        // 部分脱敏：邮箱
        if (PartialMaskKeys.Any(s => keyLower.Contains(s)) && value.Contains("@"))
        {
            return MaskEmail(value);
        }

        return value;
    }

    /// <summary>
    /// 对字典数据进行脱敏处理
    /// </summary>
    /// <param name="data">原始字典数据</param>
    /// <returns>脱敏后的字典数据</returns>
    public static Dictionary<string, object> SanitizeDictionary(Dictionary<string, object> data)
    {
        if (data == null)
        {
            return new Dictionary<string, object>();
        }

        var result = new Dictionary<string, object>();
        foreach (var kvp in data)
        {
            if (kvp.Value == null)
            {
                result[kvp.Key] = null!;
                continue;
            }

            var valueStr = kvp.Value?.ToString();
            result[kvp.Key] = Sanitize(kvp.Key, valueStr) ?? kvp.Value ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// 对对象进行脱敏处理（通过反射）
    /// </summary>
    /// <param name="obj">原始对象</param>
    /// <returns>脱敏后的字典</returns>
    public static Dictionary<string, object> SanitizeObject(object? obj)
    {
        if (obj == null)
        {
            return new Dictionary<string, object>();
        }

        var result = new Dictionary<string, object>();
        var properties = obj.GetType().GetProperties();

        foreach (var prop in properties)
        {
            if (!prop.CanRead)
            {
                continue;
            }

            try
            {
                var value = prop.GetValue(obj);
                if (value == null)
                {
                    result[prop.Name] = null!;
                    continue;
                }

                var valueStr = value.ToString();
                result[prop.Name] = Sanitize(prop.Name, valueStr) ?? value;
            }
            catch
            {
                // 忽略无法读取的属性
            }
        }

        return result;
    }

    /// <summary>
    /// 对日志消息进行脱敏处理（处理包含敏感信息的文本）
    /// </summary>
    /// <param name="message">原始消息</param>
    /// <returns>脱敏后的消息</returns>
    public static string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        // 脱敏邮箱地址
        message = Regex.Replace(message, @"\b[\w\.-]+@[\w\.-]+\.\w+\b", match =>
        {
            var email = match.Value;
            return MaskEmail(email);
        });

        // 脱敏可能的 Token（长字符串）
        message = Regex.Replace(message, @"\b[a-zA-Z0-9]{32,}\b", "***");

        return message;
    }

    /// <summary>
    /// 邮箱部分脱敏：保留首字符和@域名，中间用*代替
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <returns>脱敏后的邮箱</returns>
    internal static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
        {
            return email;
        }

        var parts = email.Split('@');
        if (parts.Length != 2)
        {
            return email;
        }

        var localPart = parts[0];
        var domainPart = parts[1];

        if (localPart.Length <= 1)
        {
            return $"{localPart}@{domainPart}";
        }

        return $"{localPart[0]}***@{domainPart}";
    }

    /// <summary>
    /// API Key 部分脱敏：保留前4位和后4位，中间用*代替
    /// </summary>
    /// <param name="apiKey">API Key</param>
    /// <returns>脱敏后的 API Key</returns>
    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
        {
            return "***";
        }

        return $"{apiKey.Substring(0, 4)}***{apiKey.Substring(apiKey.Length - 4)}";
    }
}

/// <summary>
/// 格式化日志值辅助类
/// </summary>
internal class FormattedLogValues : IReadOnlyCollection<KeyValuePair<string, object>>
{
    private readonly List<KeyValuePair<string, object>> _values;

    public FormattedLogValues(List<KeyValuePair<string, object>> values)
    {
        _values = values;
    }

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => _values.Count;

    public override string ToString()
    {
        return string.Join(", ", _values.Select(v => $"{v.Key}={v.Value}"));
    }
}

/// <summary>
/// 敏感数据日志 Enricher - Serilog 管道中对 LogEvent 的 Properties 进行脱敏
/// 拦截所有 Properties 中 key 名包含敏感词（password/token/key 等）的值，统一替换为 ***
/// 仅对值类型为 string 的属性生效；对象/字典不递归（避免误伤）
/// </summary>
public class SensitiveDataEnricher : Serilog.Core.ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties == null || logEvent.Properties.Count == 0)
        {
            return;
        }

        foreach (var prop in logEvent.Properties.ToList())
        {
            if (prop.Key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                || prop.Key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                || prop.Key.IndexOf("apikey", StringComparison.OrdinalIgnoreCase) >= 0
                || prop.Key.IndexOf("api_key", StringComparison.OrdinalIgnoreCase) >= 0
                || prop.Key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || prop.Key.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0
                || prop.Key.IndexOf("privatekey", StringComparison.OrdinalIgnoreCase) >= 0
                || prop.Key.Equals("pwd", StringComparison.OrdinalIgnoreCase)
                || prop.Key.Equals("pass", StringComparison.OrdinalIgnoreCase)
                || prop.Key.Equals("auth", StringComparison.OrdinalIgnoreCase)
                || prop.Key.Equals("key", StringComparison.OrdinalIgnoreCase))
            {
                // 替换敏感属性值为 "***"
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(prop.Key, "***"));
            }
            else if ((prop.Key.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0
                      || prop.Key.IndexOf("mail", StringComparison.OrdinalIgnoreCase) >= 0)
                     && prop.Value is ScalarValue scalar
                     && scalar.Value is string emailStr
                     && emailStr.Contains('@'))
            {
                // 邮箱部分脱敏：a***@example.com
                logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(prop.Key, SensitiveDataHelper.MaskEmail(emailStr)));
            }
        }
    }
}

/// <summary>
/// ILogger 扩展方法
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// 记录包含敏感信息的日志（自动脱敏）
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="logLevel">日志级别</param>
    /// <param name="message">日志消息</param>
    /// <param name="args">参数</param>
    public static void LogSensitive(this ILogger logger, LogLevel logLevel, string message, params object[] args)
    {
        var sanitizedArgs = new object[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is Dictionary<string, object> dict)
            {
                sanitizedArgs[i] = SensitiveDataHelper.SanitizeDictionary(dict);
            }
            else if (args[i] != null && !args[i].GetType().IsPrimitive && !(args[i] is string))
            {
                sanitizedArgs[i] = SensitiveDataHelper.SanitizeObject(args[i]);
            }
            else
            {
                sanitizedArgs[i] = args[i];
            }
        }

        logger.Log(logLevel, message, sanitizedArgs);
    }

    /// <summary>
    /// 记录信息日志（自动脱敏）
    /// </summary>
    public static void LogInformationSensitive(this ILogger logger, string message, params object[] args)
    {
        LogSensitive(logger, LogLevel.Information, message, args);
    }

    /// <summary>
    /// 记录警告日志（自动脱敏）
    /// </summary>
    public static void LogWarningSensitive(this ILogger logger, string message, params object[] args)
    {
        LogSensitive(logger, LogLevel.Warning, message, args);
    }

    /// <summary>
    /// 记录错误日志（自动脱敏）
    /// </summary>
    public static void LogErrorSensitive(this ILogger logger, string message, params object[] args)
    {
        LogSensitive(logger, LogLevel.Error, message, args);
    }

    /// <summary>
    /// 记录调试日志（自动脱敏）
    /// </summary>
    public static void LogDebugSensitive(this ILogger logger, string message, params object[] args)
    {
        LogSensitive(logger, LogLevel.Debug, message, args);
    }
}