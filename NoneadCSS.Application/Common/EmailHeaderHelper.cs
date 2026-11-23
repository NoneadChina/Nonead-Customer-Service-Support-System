using System.Text.RegularExpressions;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 邮件头解析辅助工具
/// </summary>
/// <remarks>
/// 用于从原始邮件头字符串中提取 References 等头字段，并构建符合 RFC 5322 §3.6.4 的引用链。
/// 引用链规则：新邮件的 References = 父邮件的 References + 父邮件的 Message-Id
/// </remarks>
public static class EmailHeaderHelper
{
    private static readonly Regex ReferencesHeaderRegex =
        new(@"^References:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// 从原始邮件头字符串中提取 References 头的值（空格分隔的 Message-Id 列表）
    /// </summary>
    /// <param name="rawHeaders">原始邮件头字符串（MIME 格式）</param>
    /// <returns>References 头的原始值；若无则返回 null</returns>
    public static string? ExtractReferences(string? rawHeaders)
    {
        if (string.IsNullOrWhiteSpace(rawHeaders))
        {
            return null;
        }

        var match = ReferencesHeaderRegex.Match(rawHeaders);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// 构建回复邮件的 References 头值（符合 RFC 5322 §3.6.4）
    /// </summary>
    /// <param name="parentReferences">父邮件的 References 头原始值（可为 null）</param>
    /// <param name="parentMessageId">父邮件的 Message-Id（不可为空）</param>
    /// <returns>
    /// 完整的引用链：父邮件 References + 父邮件 Message-Id。
    /// 若父邮件无 References，则仅返回父邮件 Message-Id。
    /// </returns>
    /// <remarks>
    /// RFC 5322 §3.6.4 要求 References 头包含祖先消息 ID 链。
    /// 仅返回父 Message-Id 会导致邮件客户端无法正确按线程聚合。
    /// </remarks>
    public static string? BuildReferences(string? parentReferences, string? parentMessageId)
    {
        if (string.IsNullOrWhiteSpace(parentMessageId))
        {
            return parentReferences;
        }

        // 拼接父邮件已有的 References 与父邮件自身的 Message-Id
        // 去重处理：避免父 Message-Id 已存在于 References 中
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(parentReferences))
        {
            // References 头以空格分隔多个 Message-Id
            parts.AddRange(parentReferences.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        if (!parts.Contains(parentMessageId, StringComparer.Ordinal))
        {
            parts.Add(parentMessageId);
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }
}
