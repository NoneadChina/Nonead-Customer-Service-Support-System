#nullable enable
using System;
using System.Text.RegularExpressions;

namespace NoneadCSS.Application.Common;

public static partial class ErrorSanitizer
{
    private const int MaxErrorLength = 2000;

    [GeneratedRegex(@"(?i)Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)([?&](code|token|access_token|refresh_token|id_token|auth|password|connectionstring)=)[^&\s]+", RegexOptions.Compiled)]
    private static partial Regex SensitiveQueryRegex();

    public static string Sanitize(Exception? ex)
    {
        if (ex == null) return string.Empty;
        return Sanitize($"{ex.GetType().Name}: {ex.Message}");
    }

    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var span = raw.Length > MaxErrorLength ? raw.Substring(0, MaxErrorLength) : raw;
        span = BearerTokenRegex().Replace(span, "Bearer ***REDACTED***");
        span = SensitiveQueryRegex().Replace(span, "$1***REDACTED***");
        return span;
    }
}
