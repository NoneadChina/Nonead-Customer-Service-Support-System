using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 从客户回复的文本中提取联系方式（姓名、手机号、邮箱）。
/// 用于微信、WhatsApp 等无法通过 API 获取完整客户资料的渠道，
/// 在对话引导客户提供信息后，从消息文本中自动识别并提取。
///
/// 提取策略（按优先级）：
///   1. 手机号：先匹配中国大陆 11 位手机号，再匹配国际号码
///   2. 邮箱：复用 EmailExtractor 的 RFC 5322 简化正则
///   3. 姓名：从原文移除手机号、邮箱后，清洗剩余文本（去除前缀、分隔符、空白），
///      再用 ChineseNameSplitter 拆分为姓/名
///
/// 容错策略：提取到几个字段就返回几个，不强制三项齐全。
/// 日志策略：在关键分支（正则匹配成功/失败、姓名清洗过滤）打印 Debug 级别日志；
///           通过 ILogger 参数注入，调用方可传入自己的 logger（推荐），传入 null 则不打日志。
/// </summary>
public static class ContactInfoExtractor
{
    /// <summary>
    /// 中国大陆手机号正则：1[3-9] 开头 + 9 位数字，共 11 位。
    /// 前后用边界断言避免从更长数字串中截取片段。
    /// </summary>
    private static readonly Regex CnMobileRegex = new(
        @"(?<!\d)1[3-9]\d{9}(?!\d)",
        RegexOptions.Compiled);

    /// <summary>
    /// 国际手机号正则：+国家码 空格可选 + 6~14 位数字。
    /// 放在 CN 正则之后匹配，避免误匹配中国大陆号。
    /// </summary>
    private static readonly Regex IntlMobileRegex = new(
        @"\+\d{1,3}[\s\-]?\d{6,14}",
        RegexOptions.Compiled);

    /// <summary>
    /// 邮箱正则（与 EmailExtractor 保持一致）。
    /// </summary>
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 姓名前缀清洗：去除"我叫"、"我是"、"姓名:"等常见引导词。
    /// 大小写不敏感，支持中英文冒号。
    /// </summary>
    private static readonly Regex NamePrefixRegex = new(
        @"^(我叫|我是|我叫是|我是叫|本人|姓名|名字|大名|name|my name is|my name's|i am|i'm)\s*[:：]?\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 姓名后缀清洗：去除"你好"、"谢谢"等无关词。
    /// </summary>
    private static readonly Regex NameSuffixRegex = new(
        @"(你好|您好|谢谢|多谢|麻烦了|辛苦了|thanks|thank you|thx)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 噪声词集合（单词级别完整匹配）：这些词单独出现时不是姓名，应跳过。
    /// 包括英文前缀词（my, name, is, i, am 等）和中文短引导词（我叫、我是、本人等）。
    /// 注意：此处仅做完整 token 匹配；子串匹配见 <see cref="NonNameKeywords"/>。
    /// </summary>
    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // 英文噪声词
        "my", "name", "is", "i", "am", "i'm", "im", "me", "the", "a", "an",
        // 中文噪声词（短词，完整匹配）
        "我叫", "我是", "本人", "姓名", "名字", "大名", "我的", "邮箱", "手机", "电话",
        "你好", "您好", "谢谢", "多谢", "麻烦了", "辛苦了", "感谢",
        "我的邮箱", "我的手机", "我的电话", "邮箱是", "手机是", "电话是",
        "我的邮箱是", "我的手机是", "我的电话是"
    };

    /// <summary>
    /// 非姓名关键词（子串匹配）：token 包含这些词时不是姓名，应跳过。
    /// 用于过滤两类文本：
    ///   1. 联系方式描述："我的邮箱是xxx"、"手机号是xxx"（含"邮箱"、"手机"等）
    ///   2. 咨询/服务类语句："我想咨询一下产品信息"、"请问订单问题"（含"咨询"、"产品"等）
    /// </summary>
    private static readonly string[] NonNameKeywords =
    {
        // 联系方式相关
        "邮箱", "email", "手机", "phone", "电话", "tel", "mobile", "号码", "地址",
        // 咨询/服务相关动词和名词
        "咨询", "产品", "信息", "了解", "需要", "请问", "问题", "订单", "服务", "价格"
    };

    /// <summary>
    /// 分隔符：空格、逗号、顿号、斜杠、竖线、换行、制表符、中文标点。
    /// </summary>
    private static readonly char[] Separators =
        { ' ', '\t', '\r', '\n', ',', '，', '、', '/', '\\', '|', '；', ';', '：', ':' };

    /// <summary>
    /// 邮件签名分隔符（RFC 3676 §4.3）：独占一行的 <c>-- </c>。
    /// 用于 preferBottom 模式下切出签名块，优先在签名块内识别联系方式。
    /// </summary>
    private static readonly Regex SignatureDelimiterRegex = new(
        @"(?:\r?\n|^)-- \s*(?:\r?\n|$)",
        RegexOptions.Compiled);

    /// <summary>
    /// 从文本中提取联系方式（姓名、手机号、邮箱）。
    /// 提取到几个字段就返回几个，未提取到的字段为空字符串。
    /// </summary>
    /// <param name="text">客户回复的文本</param>
    /// <param name="logger">可选的日志对象，用于输出提取过程的 Debug 信息；传入 null 则不输出日志</param>
    /// <param name="preferBottom">
    /// 是否优先从文本底部提取（默认 false）。
    /// 邮件场景应传 true：邮件签名块通常在底部，且正文上方常含引用的旧邮件（带噪声联系方式）。
    /// 启用后：
    ///   1. 优先在 <c>-- </c> 签名块内查找；签名块无命中时回退到全文底部 N 字符
    ///   2. 邮箱/手机号取最后一个匹配（更接近底部）
    ///   3. 姓名从 tokens 末尾向前遍历
    /// </param>
    /// <returns>包含姓名、手机号、邮箱的提取结果</returns>
    public static ContactInfo Extract(string? text, ILogger? logger = null, bool preferBottom = false)
    {
        var result = new ContactInfo();

        if (string.IsNullOrWhiteSpace(text))
        {
            logger?.LogDebug("联系方式提取：输入文本为空或仅空白，跳过");
            return result;
        }

        // 文本长度限制（避免处理超长无意义内容）
        if (text.Length > 2000)
        {
            logger?.LogWarning("联系方式提取：输入文本过长（Length={Length} > 2000），跳过以避免正则性能风险", text.Length);
            return result;
        }

        logger?.LogDebug("联系方式提取：开始分析文本，Length={Length}，preferBottom={PreferBottom}，原文预览={Preview}",
            text.Length, preferBottom, text.Length > 80 ? text.Substring(0, 80) : text);

        // preferBottom 模式：优先用签名块文本，签名块无命中再回退到全文底部 800 字符
        string searchText = text;
        string? signatureBlock = null;
        if (preferBottom)
        {
            signatureBlock = ExtractSignatureBlock(text);
            if (!string.IsNullOrWhiteSpace(signatureBlock))
            {
                logger?.LogDebug("联系方式提取：检测到签名块（长度 {Len}），优先在签名块内查找", signatureBlock.Length);
                searchText = signatureBlock;
            }
            else
            {
                // 无签名块：取底部 800 字符作为搜索范围（兼顾性能与准确度）
                searchText = text.Length > 800 ? text.Substring(text.Length - 800) : text;
                logger?.LogDebug("联系方式提取：未检测到签名块，回退到全文底部 {Len} 字符", searchText.Length);
            }
        }

        // 1. 提取邮箱（可能有多个；preferBottom 取最后一个，否则取第一个）
        var emailMatches = EmailRegex.Matches(searchText);
        if (emailMatches.Count > 0)
        {
            var picked = preferBottom ? emailMatches[^1] : emailMatches[0];
            result.Email = picked.Value.ToLowerInvariant().Trim();
            logger?.LogDebug("联系方式提取：邮箱匹配成功，数量={Count}，preferBottom={PreferBottom}，选中={Email}，全部={AllEmails}",
                emailMatches.Count, preferBottom,
                result.Email,
                string.Join(";", emailMatches.Select(m => m.Value)));
        }
        else if (preferBottom && signatureBlock != null)
        {
            // 签名块未命中，回退到全文再找一次（取最后一个）
            var fullMatches = EmailRegex.Matches(text);
            if (fullMatches.Count > 0)
            {
                result.Email = fullMatches[^1].Value.ToLowerInvariant().Trim();
                logger?.LogDebug("联系方式提取：签名块未命中邮箱，回退全文取最后一个={Email}", result.Email);
                emailMatches = fullMatches;
            }
            else
            {
                logger?.LogDebug("联系方式提取：邮箱未匹配（无符合 RFC 简化格式的地址）");
            }
        }
        else
        {
            logger?.LogDebug("联系方式提取：邮箱未匹配（无符合 RFC 简化格式的地址）");
        }

        // 2. 提取手机号（先 CN，再国际；preferBottom 取最后一个，否则取第一个）
        ExtractPhone(searchText, preferBottom, logger, out var pickedPhone, out var allPhoneMatches);
        if (pickedPhone != null)
        {
            result.Phone = pickedPhone;
            logger?.LogDebug("联系方式提取：匹配到手机号，Phone={Phone}", result.Phone);
        }
        else if (preferBottom && signatureBlock != null)
        {
            // 签名块未命中，回退全文再找一次（取最后一个）
            ExtractPhone(text, true, logger, out var fallbackPhone, out _);
            if (fallbackPhone != null)
            {
                result.Phone = fallbackPhone;
                logger?.LogDebug("联系方式提取：签名块未命中手机号，回退全文取最后一个={Phone}", result.Phone);
                allPhoneMatches = new List<string> { fallbackPhone };
            }
            else
            {
                logger?.LogDebug("联系方式提取：国际手机号也未匹配");
            }
        }
        else
        {
            logger?.LogDebug("联系方式提取：国际手机号也未匹配");
        }

        // 3. 提取姓名：从搜索文本移除已识别的邮箱和手机号，再清洗剩余文本
        logger?.LogDebug("联系方式提取：开始姓名提取，移除邮箱/手机号后的原文将被切分分析");
        var remaining = searchText;
        if (emailMatches.Count > 0)
        {
            foreach (Match m in emailMatches)
            {
                remaining = remaining.Replace(m.Value, " ");
            }
            logger?.LogDebug("联系方式提取：已从剩余文本移除 {Count} 个邮箱匹配", emailMatches.Count);
        }
        if (allPhoneMatches != null && allPhoneMatches.Count > 0)
        {
            foreach (var p in allPhoneMatches)
            {
                remaining = remaining.Replace(p, " ");
            }
            logger?.LogDebug("联系方式提取：已从剩余文本移除 {Count} 个手机号匹配", allPhoneMatches.Count);
        }
        else if (!string.IsNullOrEmpty(result.Phone))
        {
            remaining = remaining.Replace(result.Phone, " ");
            logger?.LogDebug("联系方式提取：已从剩余文本移除手机号匹配 {Phone}", result.Phone);
        }

        // 按分隔符切分；preferBottom 从末尾向前找第一个非噪声 token，否则从前往后
        var tokens = remaining.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        logger?.LogDebug("联系方式提取：剩余文本切分为 {Count} 个 token，preferBottom={PreferBottom}，逐一过滤",
            tokens.Length, preferBottom);
        for (int i = 0; i < tokens.Length; i++)
        {
            // preferBottom: 从末尾向前（索引 tokens.Length-1-i）；正序: i
            int idx = preferBottom ? tokens.Length - 1 - i : i;
            if (idx < 0) break;

            var token = tokens[idx];
            var cleaned = CleanNameToken(token, out var filterReason);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                result.Name = cleaned;
                logger?.LogDebug("联系方式提取：命中姓名 Token[{Index}]={Token} -> 清洗后={Cleaned}",
                    idx, token, cleaned);
                break;
            }

            if (!string.IsNullOrEmpty(filterReason))
            {
                logger?.LogDebug("联系方式提取：Token[{Index}]={Token} 被过滤，原因：{Reason}",
                    idx, token, filterReason);
            }
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            logger?.LogDebug("联系方式提取：姓名未匹配（所有 Token 均被清洗/过滤掉）");
        }

        logger?.LogDebug("联系方式提取：结束，最终结果 Name={Name}, Phone={Phone}, Email={Email}, HasAny={HasAny}",
            result.Name, result.Phone, result.Email, result.HasAny);

        return result;
    }

    /// <summary>
    /// 切出邮件签名块（RFC 3676 §4.3：以独占一行的 "-- " 分隔）。
    /// 返回分隔符之后的文本；若无分隔符返回 null。
    /// </summary>
    private static string? ExtractSignatureBlock(string text)
    {
        var match = SignatureDelimiterRegex.Match(text);
        if (!match.Success)
            return null;

        var sig = text.Substring(match.Index + match.Length);
        return string.IsNullOrWhiteSpace(sig) ? null : sig;
    }

    /// <summary>
    /// 提取手机号：先 CN 再国际；preferBottom 取最后一个，否则取第一个。
    /// 成功时通过 <paramref name="allMatches"/> 返回所有命中值（用于姓名提取阶段移除）。
    /// </summary>
    private static void ExtractPhone(string text, bool preferBottom, ILogger? logger,
        out string? picked, out List<string>? allMatches)
    {
        picked = null;
        allMatches = null;

        var cnMatches = CnMobileRegex.Matches(text);
        if (cnMatches.Count > 0)
        {
            picked = (preferBottom ? cnMatches[^1] : cnMatches[0]).Value;
            allMatches = cnMatches.Cast<Match>().Select(m => m.Value).ToList();
            return;
        }

        logger?.LogDebug("联系方式提取：中国大陆手机号匹配失败，尝试匹配国际手机号");
        var intlMatches = IntlMobileRegex.Matches(text);
        if (intlMatches.Count > 0)
        {
            picked = (preferBottom ? intlMatches[^1] : intlMatches[0]).Value;
            allMatches = intlMatches.Cast<Match>().Select(m => m.Value).ToList();
        }
    }

    /// <summary>
    /// 清洗姓名候选片段：去除前缀（我叫、姓名: 等）和后缀（你好、谢谢 等），
    /// 并过滤噪声词、非姓名关键词、纯数字标点、过长文本等明显非姓名内容。
    /// 限制长度 1~20 字符。
    /// </summary>
    /// <param name="token">待清洗的原始 Token</param>
    /// <param name="filterReason">若返回空字符串，这里记录过滤原因（用于日志）；未过滤则返回空</param>
    /// <returns>清洗后的姓名；若被过滤返回空字符串</returns>
    private static string CleanNameToken(string token, out string filterReason)
    {
        filterReason = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            filterReason = "Token 为空或仅空白";
            return string.Empty;
        }

        var cleaned = token.Trim();

        // 多次清洗前缀（应对"我的名字是张三"这类复合前缀）
        for (int i = 0; i < 3; i++)
        {
            var after = NamePrefixRegex.Replace(cleaned, string.Empty);
            if (after == cleaned)
                break;
            cleaned = after;
        }

        // 清洗后缀
        cleaned = NameSuffixRegex.Replace(cleaned, string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            filterReason = "前缀/后缀清洗后为空";
            return string.Empty;
        }

        // 噪声词过滤：单独的噪声词不是姓名（完整匹配，大小写不敏感）
        // 例如："my"、"name"、"你好"、"邮箱是" 等
        if (NoiseWords.Contains(cleaned))
        {
            filterReason = $"命中噪声词集合 NoiseWords（完整匹配）: {cleaned}";
            return string.Empty;
        }

        // 非姓名关键词过滤：token 包含这些词时不是姓名（子串匹配）
        // 例如："我的邮箱是"（含"邮箱"）、"我想咨询一下产品信息"（含"咨询"、"产品"）
        foreach (var kw in NonNameKeywords)
        {
            if (cleaned.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                filterReason = $"命中非姓名关键词 NonNameKeywords（子串匹配）: {kw}";
                return string.Empty;
            }
        }

        // 过滤纯数字、纯标点
        if (cleaned.All(c => char.IsDigit(c) || char.IsPunctuation(c)))
        {
            filterReason = "全部为数字或标点字符";
            return string.Empty;
        }

        // 过长（>20字符可能是误识别的地址/备注）
        if (cleaned.Length > 20)
        {
            filterReason = $"过长（Length={cleaned.Length} > 20）";
            return string.Empty;
        }

        return cleaned;
    }
}

/// <summary>
/// 联系方式提取结果。未提取到的字段为空字符串（非 null）。
/// </summary>
public sealed class ContactInfo
{
    /// <summary>姓名（已清洗，未拆分姓/名；调用方可用 ChineseNameSplitter 进一步拆分）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>手机号（中国大陆或国际格式）</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>邮箱地址（已小写归一化）</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>是否提取到任一字段</summary>
    public bool HasAny => !string.IsNullOrWhiteSpace(Name)
        || !string.IsNullOrWhiteSpace(Phone)
        || !string.IsNullOrWhiteSpace(Email);
}
