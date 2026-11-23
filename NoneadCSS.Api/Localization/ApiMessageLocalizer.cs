using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace NoneadCSS.Api.Localization;

/// <summary>
/// 后端「直出中文」文案的英文词典（出口层替换）。
///
/// 背景：本仓库约 690 条会进响应体的中文文案是在服务层与 DTO 注解里硬编码的，
/// 既没有走 resx，也没有 ErrorMessageResourceType。逐条改造要动 200+ 文件且风险高
/// （多处按中文字面量做分支判断，见 AuthController.cs:373、CustomReportsController.cs:28），
/// 因此采用「词典 + 出口点替换」：只在响应出口（ApiResponseWrapperFilter、
/// InvalidModelStateResponseFactory、GlobalExceptionMiddleware、限流 OnRejected、
/// CsrfProtectionMiddleware）按请求文化做匹配替换，状态码判定之前不动原文。
///
/// 键是「归一化后的中文原句」：数字串与引号串换成哨兵 \uE000，代表「运行时会变的一段」；
/// 英文值用 {0}、{1}… 按出现顺序回填这些片段。这样带插值的句子
/// （例如「请求过于频繁，请在 {retryAfter} 秒后重试」）也能命中，不会被固化成某个具体数字。
///
/// 归一化算法必须与生成词典的脚本（i18n-audit/msg-extract.mjs）一致；两侧都改时必须同步，
/// 否则词典整体失配。失配的后果是保持中文原样输出（不会误翻），因此可以安全降级。
/// </summary>
public static partial class ApiMessageLocalizer
{
    /// <summary>词典相对路径（相对项目根 / 输出目录）。</summary>
    public const string DictionaryRelativePath = "Localization/api-message-en.json";

    /// <summary>代表「可变片段」的哨兵字符（Unicode 私有区，正常文案里不会出现）。</summary>
    private const char SegmentSeparator = '\uE000';

    /// <summary>
    /// 运行期识别可变片段。引号串排在数字串之前，是为了让 "abc123" 整体算一个片段，
    /// 与词典生成脚本产出的键形态一致（脚本先换数字、再换引号串，最终键里两者合并成一个片段）。
    /// </summary>
    [GeneratedRegex("\"[^\"]*\"|'[^']*'|\\d+(?:[.,]\\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentRegex();

    [GeneratedRegex("\\{(\\d+)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex SlotRegex();

    private static readonly Lazy<LocalizationStore> Store = new(LoadStore);

    /// <summary>词典条目数（0 表示词典缺失或损坏，此时本功能等同关闭）。</summary>
    public static int EntryCount => Store.Value.Entries.Count;

    /// <summary>实际加载到的词典文件路径；未加载成功时为 null。</summary>
    public static string? SourcePath => Store.Value.Path;

    /// <summary>
    /// 按界面文化把中文文案换成英文。非英文文化返回 null，调用方保持原值。
    /// </summary>
    public static string? Localize(string? message, string? culture)
    {
        if (string.IsNullOrWhiteSpace(message) || !IsEnglish(culture))
        {
            return null;
        }

        return LocalizeToEnglish(message);
    }

    /// <summary>
    /// 词典查找：先整句精确匹配，再按片段归一化后匹配。找不到返回 null（不做任何猜测）。
    /// </summary>
    public static string? LocalizeToEnglish(string message)
    {
        var store = Store.Value;
        if (store.Entries.Count == 0)
        {
            return null;
        }

        if (store.Entries.TryGetValue(message, out var literal))
        {
            return FillSlots(literal, Array.Empty<string>());
        }

        if (message.IndexOf('"') >= 0 || message.IndexOf('\'') >= 0 || ContainsDigit(message))
        {
            // 只有存在可变片段时才有必要归一化：没有片段就不可能匹配到带哨兵的键。
            var captures = new List<string>();
            var key = SegmentRegex().Replace(message, m =>
            {
                captures.Add(m.Value);
                return SegmentSeparator.ToString();
            });

            if (store.Entries.TryGetValue(key, out var template))
            {
                return FillSlots(template, captures);
            }
        }

        // 精确匹配与片段归一化都没命中，最后尝试「前缀键」规则。
        return LocalizePrefix(store, message);
    }

    /// <summary>
    /// 前缀键匹配。源码里有一类「固定中文前缀 + 动态详情」的拼接（例如
    /// <c>LineClient.cs</c> 的 <c>"LINE 请求异常：" + ex.Message</c>、
    /// <c>TelegramService.cs:636</c> 的 <c>"getMe 异常：" + ex.Message</c>），
    /// 这类消息既不可能整句命中词典，动态部分也未必是数字或引号串，因此归一化也命中不了。
    /// 词典里这类键的特征是**以全角冒号结尾、且英文值不含槽位**（由 <c>scripts/i18n/api-message-dictionary.mjs</c>
    /// 从拼接字面量中原样抽取），这里把前缀换成英文、再把后面的动态详情原样接上。
    /// 只匹配「以冒号结尾的前缀键」且必须位于消息开头，是为了避免把客户数据里恰好包含这些字的句子误翻；
    /// 冒号后补一个空格，避免输出 <c>LINE request exception:System…</c> 这种粘连。
    /// </summary>
    private static string? LocalizePrefix(LocalizationStore store, string message)
    {
        foreach (var (prefix, english) in store.PrefixEntries)
        {
            if (prefix.Length >= message.Length || !message.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = message[prefix.Length..];
            return english.EndsWith(' ') ? english + rest : english + " " + rest;
        }

        return null;
    }

    private static bool ContainsDigit(string s)
    {
        foreach (var c in s)
        {
            if (c is >= '0' and <= '9')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 用捕获到的片段回填 {0}、{1}…。任一槽位缺数据就整体放弃：
    /// 宁可不翻译，也不输出「半句英文半句中文」的混合文案。
    /// </summary>
    private static string? FillSlots(string template, IReadOnlyList<string> captures)
    {
        var slots = SlotRegex().Matches(template);
        if (slots.Count == 0)
        {
            return template;
        }

        var sb = new StringBuilder(template.Length + 16);
        var last = 0;
        foreach (Match slot in slots)
        {
            var index = int.Parse(slot.Groups[1].Value, CultureInfo.InvariantCulture);
            if (index >= captures.Count)
            {
                return null;
            }

            sb.Append(template, last, slot.Index - last);
            sb.Append(captures[index]);
            last = slot.Index + slot.Length;
        }

        sb.Append(template, last, template.Length - last);
        return sb.ToString();
    }

    private static bool IsEnglish(string? culture)
    {
        return !string.IsNullOrWhiteSpace(culture)
            && culture.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    private static LocalizationStore LoadStore()
    {
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(root, DictionaryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (parsed is not { Count: > 0 })
                {
                    continue;
                }

                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in parsed)
                {
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    {
                        entries[key] = value;
                    }
                }

                return new LocalizationStore(entries, path)
                {
                    PrefixEntries = BuildPrefixEntries(entries)
                };
            }
            catch (JsonException)
            {
                // 词典损坏时不把异常抛进请求管道，等同关闭该功能
                return new LocalizationStore(new Dictionary<string, string>(StringComparer.Ordinal), null);
            }
        }

        return new LocalizationStore(new Dictionary<string, string>(StringComparer.Ordinal), null);
    }

    /// <summary>
    /// 从前缀型词典键构建匹配表。筛选条件见 <see cref="LocalizePrefix"/>；按键长降序，
    /// 让更具体的前缀先命中（词典里这类键目前只有 2 条，线性扫描代价可忽略）。
    /// </summary>
    private static IReadOnlyList<(string Prefix, string English)> BuildPrefixEntries(Dictionary<string, string> entries)
    {
        var list = new List<(string Prefix, string English)>();
        foreach (var (key, value) in entries)
        {
            if (key[^1] != '：' || key.Contains(SegmentSeparator) || SlotRegex().Matches(value).Count > 0)
            {
                continue;
            }

            list.Add((key, value));
        }

        list.Sort(static (a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
        return list;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == null)
            {
                break;
            }

            yield return Path.Combine(parent, "NoneadCSS.Api");
            yield return parent;
            dir = parent;
        }
    }

    /// <summary>
    /// 就地翻译错误分支里返回体对象的「消息字段」，返回被翻译的条数。
    /// 只改可写、且属性名在白名单里的 string 属性（Message / ErrorMessage / Detail），
    /// 因此客户写的正文不会被当成系统文案替换；词典未收录的句子原样保留。
    /// 只在 HTTP ≥ 400 的分支调用，进一步把范围锁死在错误响应上。
    /// </summary>
    public static int LocalizeMessageProperties(object? graph, HttpContext? context)
    {
        var culture = ApiRequestCulture.Of(context);
        if (graph is null || !IsEnglish(culture) || Store.Value.Entries.Count == 0)
        {
            return 0;
        }

        var translated = 0;
        VisitGraph(graph, 0, new HashSet<object>(ReferenceEqualityComparer.Instance), culture, ref translated);
        return translated;
    }

    private const int MaxGraphDepth = 4;
    private const int MaxSequenceItems = 100;
    private static readonly string[] TranslatablePropertyNames = ["Message", "ErrorMessage", "Detail"];

    /// <summary>字典键名是否与属性白名单同名（JSON 里是 camelCase，所以忽略大小写比较）。</summary>
    private static bool IsTranslatableKey(string keyName) =>
        Array.Find(TranslatablePropertyNames, name => string.Equals(name, keyName, StringComparison.OrdinalIgnoreCase)) is not null;

    private static void VisitGraph(object node, int depth, HashSet<object> visited, string? culture, ref int translated)
    {
        if (depth > MaxGraphDepth || node is string || !visited.Add(node))
        {
            return;
        }

        if (node is System.Collections.IEnumerable sequence && node is not System.Collections.IDictionary)
        {
            var taken = 0;
            foreach (var item in sequence)
            {
                if (item is null || ++taken > MaxSequenceItems)
                {
                    break;
                }

                if (!item.GetType().IsValueType)
                {
                    VisitGraph(item, depth + 1, visited, culture, ref translated);
                }
            }

            return;
        }

        if (node is System.Collections.IDictionary dictionary)
        {
            // 有些出口（例如登录失败）会把文案同时写进信封 Message 和 Data 里的 "message" 键，
            // 而 Data 是字典而非对象，属性反射碰不到。这里按同一个白名单补一遍，
            // 只改键名为 message / errorMessage / detail 的字符串值，其余键（客户数据）不动。
            var keys = new List<object>(dictionary.Count);
            foreach (var key in dictionary.Keys)
            {
                if (key is not null)
                {
                    keys.Add(key);
                }
            }

            foreach (var key in keys)
            {
                var value = dictionary[key];

                if (value is string text && key is string keyName && IsTranslatableKey(keyName))
                {
                    if (dictionary.IsReadOnly)
                    {
                        continue;
                    }

                    var localized = Localize(text, culture);
                    if (localized is not null && localized != text)
                    {
                        dictionary[key] = localized;
                        translated++;
                    }

                    continue;
                }

                if (value is not null && !value.GetType().IsValueType)
                {
                    VisitGraph(value, depth + 1, visited, culture, ref translated);
                }
            }

            return;
        }

        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            if (value is null)
            {
                continue;
            }

            if (value is string text)
            {
                if (property.CanWrite && Array.IndexOf(TranslatablePropertyNames, property.Name) >= 0)
                {
                    var localized = Localize(text, culture);
                    if (localized is not null && localized != text)
                    {
                        property.SetValue(node, localized);
                        translated++;
                    }
                }

                continue;
            }

            var valueType = value.GetType();
            if (valueType.IsValueType || value is DateTime || value is Guid || value is Uri)
            {
                continue;
            }

            VisitGraph(value, depth + 1, visited, culture, ref translated);
        }
    }

    private sealed record LocalizationStore(Dictionary<string, string> Entries, string? Path)
    {
        /// <summary>
        /// 「以全角冒号结尾、英文值不含槽位」的前缀键，按键长降序（长前缀优先匹配，避免短前缀抢先）。
        /// 构建一次，随词典缓存。
        /// </summary>
        public IReadOnlyList<(string Prefix, string English)> PrefixEntries { get; init; } = Array.Empty<(string, string)>();
    }
}
