using System.Text.RegularExpressions;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 从文本中提取邮箱地址的工具。
/// 用于微信、WhatsApp 等无法通过 API 获取用户邮箱的渠道，
/// 在对话引导用户提供邮箱后，从消息文本中自动识别并提取。
/// </summary>
public static class EmailExtractor
{
    /// <summary>
    /// RFC 5322 简化版邮箱正则，覆盖常见邮箱格式。
    /// </summary>
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 从文本中提取第一个有效邮箱地址（小写归一化）。
    /// </summary>
    /// <param name="text">待解析的文本（如客户回复的消息内容）</param>
    /// <returns>提取到的邮箱地址；未找到返回 null</returns>
    public static string? ExtractFirst(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = EmailRegex.Match(text);
        return match.Success ? match.Value.ToLowerInvariant().Trim() : null;
    }

    /// <summary>
    /// 从文本中提取所有有效邮箱地址（去重，小写归一化）。
    /// </summary>
    /// <param name="text">待解析的文本</param>
    /// <returns>邮箱列表；无匹配返回空列表</returns>
    public static List<string> ExtractAll(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var matches = EmailRegex.Matches(text);
        return matches
            .Cast<Match>()
            .Select(m => m.Value.ToLowerInvariant().Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
