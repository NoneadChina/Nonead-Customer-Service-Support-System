using System.Text;

namespace NoneadCSS.Application.Common;

/// <summary>
/// iMessage 身份归一化（docs/integration/imessage.md §5）。
/// 私聊：同一 1:1 会话可能以 iMessage;-;+1… / SMS;-;+1… / RCS;-;+1… / any;-;+1… / 裸 handle 送达
///        ⇒ 收敛到裸 handle，避免一个会话裂成多条。
/// 群聊：保留 GUID（MMS 群与 iMessage 群确实是不同会话；群没有单一 handle）。
/// 服务前缀绝不剥离（剥掉曾导致主动回复降级成 SMS）。
/// </summary>
public static class IMessageIdentity
{
    /// <summary>BlueBubbles chatGuid 分隔符：1:1 用 "iMessage;-;xxx"，群用 "iMessage;+;chatxxx"。</summary>
    private const string IndividualSeparator = ";-;";
    private const string GroupSeparator = ";+;";

    /// <summary>把传输模式写进外部 id，形成 (tenant, channel_mode, external_id) 作用域。
    /// 既有索引为 Customer.Channel + Customer.ChannelId（非唯一），AMB 不透明 id 与 BlueBubbles
    /// chatGuid 可能相撞 ⇒ 必须以模式前缀区分，而不是只用裸 id。</summary>
    public static string BuildScopedChannelId(string mode, string externalId)
        => $"{NormalizeMode(mode)}:{externalId}";

    /// <summary>取作用域化 ChannelId 的模式前缀（第一个冒号之前）；无冒号或前缀非法时按 bridge。
    /// 注意不能把整串交给 <see cref="NormalizeMode"/> —— AMB 的不透明 id 本身含冒号
    /// （如 amb_msp:urn:mbid:xxx），整串归一化会被判成 bridge，导致模式误判。</summary>
    public static string ExtractModePrefix(string? scopedChannelId)
    {
        if (string.IsNullOrWhiteSpace(scopedChannelId)) return "bridge";
        var idx = scopedChannelId.IndexOf(':');
        return NormalizeMode(idx <= 0 ? scopedChannelId : scopedChannelId[..idx]);
    }

    /// <summary>从作用域化的 ChannelId 取回裸外部 id（无冒号时原样返回）。</summary>
    public static string ExtractExternalId(string scopedChannelId)
    {
        if (string.IsNullOrWhiteSpace(scopedChannelId)) return scopedChannelId;
        var idx = scopedChannelId.IndexOf(':');
        return idx <= 0 ? scopedChannelId : scopedChannelId[(idx + 1)..];
    }

    public static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "bridge";
        var m = mode.Trim().ToLowerInvariant();
        return m is "amb_msp" or "amb_apple_direct" or "bridge" ? m : "bridge";
    }

    /// <summary>模式是否在白名单内（写库前校验，避免拼错导致两套传输都不生效）。</summary>
    public static bool IsModeValid(string? mode)
        => !string.IsNullOrWhiteSpace(mode)
           && mode.Trim().ToLowerInvariant() is "amb_msp" or "amb_apple_direct" or "bridge";

    /// <summary>该 ChannelId / 模式是否走 AMB（Apple Messages for Business）一侧。
    /// 口径：只有 amb_msp / amb_apple_direct 是 AMB，bridge（BlueBubbles 桥接）不是；裸模式与作用域化
    /// ChannelId 都只按冒号前的模式段判定，因此 "ambiguous:x" 这类整串前缀不会被误判为 AMB。
    /// amb_apple_direct 是否真的可用还要看 IMessageOptions.ExperimentalAppleDirect，那是配置层的门禁。</summary>
    public static bool IsAmbMode(string? modeOrScopedChannelId)
    {
        if (string.IsNullOrWhiteSpace(modeOrScopedChannelId)) return false;
        var mode = ExtractModePrefix(modeOrScopedChannelId);
        return mode is "amb_msp" or "amb_apple_direct";
    }

    /// <summary>
    /// 归一化私聊外部 id：邮箱小写去空格；电话按 E.164 口径保留 + 与数字（不猜国家码）。
    /// 群聊不要用这个方法 —— 群聊直接用 GUID。
    /// </summary>
    public static string NormalizeHandle(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle)) return string.Empty;
        var value = handle.Trim();

        var at = value.IndexOf('@');
        if (at > 0 && at < value.Length - 1)
            return value.ToLowerInvariant(); // 邮箱 handle

        return NormalizePhoneNumber(value);
    }

    /// <summary>电话归一：保留前导 +，去掉空格/连字符/括号/点；含非数字（除 +）时原样返回（视为不可信展示数据）。</summary>
    private static string NormalizePhoneNumber(string value)
    {
        var plus = value.StartsWith('+');
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch is ' ' or '-' or '(' or ')' or '.' or '\u00a0') continue;
            if (ch == '+' && i == 0) continue;   // 前导 + 单独记在 plus 上，不当成非法字符
            if (ch is < '0' or > '9') return value; // 不是纯电话号码，不做转换
            sb.Append(ch);
        }

        var digits = sb.ToString();
        if (digits.Length == 0) return value;
        return plus ? "+" + digits : digits;
    }

    /// <summary>chatGuid 是否为群会话（";+;" 或群 chatIdentifier 前缀 chat…）。</summary>
    public static bool IsGroupChatGuid(string? chatGuid)
    {
        if (string.IsNullOrWhiteSpace(chatGuid)) return false;
        if (chatGuid.Contains(GroupSeparator, StringComparison.Ordinal)) return true;
        var tail = GetIdentifierFromGuid(chatGuid);
        return tail.StartsWith("chat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>取 chatGuid 的 identifier 段（去掉 "service;-;" / "service;+;" 前缀）。</summary>
    public static string GetIdentifierFromGuid(string? chatGuid)
    {
        if (string.IsNullOrWhiteSpace(chatGuid)) return string.Empty;
        var idx = chatGuid.IndexOf(IndividualSeparator, StringComparison.Ordinal);
        if (idx >= 0) return chatGuid[(idx + IndividualSeparator.Length)..];
        idx = chatGuid.IndexOf(GroupSeparator, StringComparison.Ordinal);
        if (idx >= 0) return chatGuid[(idx + GroupSeparator.Length)..];
        return chatGuid;
    }

    /// <summary>取 chatGuid 的服务前缀（iMessage / SMS / RCS / any / unknown）。</summary>
    public static string GetServiceFromGuid(string? chatGuid)
    {
        if (string.IsNullOrWhiteSpace(chatGuid)) return "unknown";
        var idx = chatGuid.IndexOf(';');
        return idx > 0 ? chatGuid[..idx] : "unknown";
    }

    /// <summary>
    /// 回复用的 chatGuid（bridge）。<b>必须保留服务前缀</b>：剥掉前缀会让主动回复降级成 SMS
    /// （同类上游事故 openclaw #35624）。优先用入站预热过的 GUID，其次按已知服务前缀重建；
    /// 群聊的 externalId 本身就是 GUID，直接原样返回。
    /// </summary>
    public static string BuildReplyGuid(string scopedChannelId, string? inboundService)
    {
        var externalId = ExtractExternalId(scopedChannelId);
        if (IsGroupChatGuid(externalId)) return externalId;
        if (TryGetChatGuid(scopedChannelId, out var cached)) return cached;
        var service = string.IsNullOrWhiteSpace(inboundService) || inboundService == "unknown"
            ? "iMessage"
            : inboundService;
        return $"{service}{IndividualSeparator}{externalId}";
    }

    // ===== chatIdentifier → chatGuid 预热缓存（imessage.md §5）=====
    // 进程内缓存：每条入站都会刷新，重启后由下一条入站重新预热，冷启动时用 BuildReplyGuid 兜底。
    // 不要用 GET /api/v1/chat/query 分页去把 handle 解析成 GUID（单页有上限，漏了就静默另起新会话）。
    // 需要跨重启持久化 GUID 时要加表/列（超出本渠道「不碰共享文件」范围，见交付说明 OPEN ISSUES）。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _chatGuidCache = new();

    /// <summary>入站时记住该作用域 id 最近一次使用的 chatGuid，回复时沿用，保证同一会话不裂开。</summary>
    public static void RememberChatGuid(string scopedChannelId, string? chatGuid)
    {
        if (string.IsNullOrWhiteSpace(scopedChannelId) || string.IsNullOrWhiteSpace(chatGuid)) return;
        _chatGuidCache[scopedChannelId] = chatGuid;
        if (_chatGuidCache.Count > ChatGuidCacheCap) _chatGuidCache.Clear();
    }

    public static bool TryGetChatGuid(string scopedChannelId, out string chatGuid)
    {
        chatGuid = string.Empty;
        if (string.IsNullOrWhiteSpace(scopedChannelId)) return false;
        if (!_chatGuidCache.TryGetValue(scopedChannelId, out var value) || string.IsNullOrWhiteSpace(value)) return false;
        chatGuid = value;
        return true;
    }

    /// <summary>缓存上限（防止无界增长，远超单租户日常活跃会话数；超限直接清空由入站重新预热）。</summary>
    internal const int ChatGuidCacheCap = 20_000;

    /// <summary>
    /// 由入站信息推导（外部 id, 是否群聊）。优先级：群 GUID &gt; chatGuid 的 identifier &gt; chatIdentifier &gt; handle。
    /// 返回的 externalId 已按 §5 归一化；回复仍应使用原始 chatGuid（持久化在线程的 From 上）。
    /// </summary>
    public static (string ExternalId, bool IsGroup) ResolveExternalId(
        string? chatGuid,
        string? chatIdentifier,
        string? handleAddress,
        bool? isGroupChat)
    {
        var group = isGroupChat == true
                    || IsGroupChatGuid(chatGuid)
                    || string.IsNullOrEmpty(chatGuid) && LooksLikeGroupIdentifier(chatIdentifier);

        if (group)
        {
            var guid = !string.IsNullOrWhiteSpace(chatGuid)
                ? chatGuid!
                : (chatIdentifier ?? string.Empty);
            return (guid.Trim(), true);
        }

        var candidate = FirstNonBlank(
            chatIdentifier,
            GetIdentifierFromGuid(chatGuid),
            handleAddress);

        return (NormalizeHandle(candidate), false);
    }

    private static bool LooksLikeGroupIdentifier(string? chatIdentifier)
        => !string.IsNullOrWhiteSpace(chatIdentifier)
           && chatIdentifier!.StartsWith("chat", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }
}
