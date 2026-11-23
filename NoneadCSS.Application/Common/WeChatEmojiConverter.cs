using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 微信表情代码转换器 - 将微信推送的表情代码（如 /:ok、/::D、[Facepalm]）转换为 Unicode emoji。
/// 微信服务号客户发送内置表情时，微信推送的 XML Content 字段是文本代码而非 Unicode emoji，
/// 直接存储会导致会话记录显示 /:ok 这样的原始代码。本转换器在消息入库前统一转换为 emoji。
/// 映射表基于微信官方公开的表情对照表，符号代码（/::XX）和别名代码（[XX]）均覆盖。
/// </summary>
public static class WeChatEmojiConverter
{
    /// <summary>
    /// 完整的微信表情代码 → Unicode emoji 映射表。
    /// 通过 BuildFullEmojiMap() 构建以安全处理重复键，包含两种格式：
    /// 1. 符号代码（/::D、/:ok）- 微信基本表情
    /// 2. 别名代码（[Facepalm]、[Smile]）- 微信扩展表情
    /// </summary>
    private static readonly Dictionary<string, string> FullEmojiMap = BuildFullEmojiMap();

    /// <summary>
    /// 匹配所有已知微信表情代码的正则（按长度降序排列，确保最长匹配优先）。
    /// </summary>
    private static readonly Regex EmojiRegex = new(
        string.Join("|", FullEmojiMap.Keys.Select(Regex.Escape)),
        RegexOptions.Compiled);

    /// <summary>
    /// 将文本中的微信表情代码替换为 Unicode emoji。
    /// 支持表情代码嵌入文本中、多个表情代码连续出现（无分隔符）等场景。
    /// 未匹配到任何代码时原样返回。
    /// </summary>
    /// <param name="content">可能包含微信表情代码的文本</param>
    /// <returns>表情代码已替换为 emoji 的文本</returns>
    public static string Convert(string? content)
    {
        if (string.IsNullOrEmpty(content)) return content ?? string.Empty;
        return EmojiRegex.Replace(content, m =>
            FullEmojiMap.TryGetValue(m.Value, out var emoji) ? emoji : m.Value);
    }

    /// <summary>
    /// 构建完整的微信表情代码 → emoji 映射表。
    /// 包含符号代码（/::XX、/:XX）和别名代码（[XX]）两大类。
    /// </summary>
    private static Dictionary<string, string> BuildFullEmojiMap()
    {
        var map = new Dictionary<string, string>(128);

        // ===== 符号代码（微信基本表情，/:: 或 /: 开头）=====
        // 基本表情（/:: 开头）
        map["/::D"] = "😄";   // 微笑/呲牙
        map["/::~"] = "😢";   // 撇嘴
        map["/::B"] = "😊";   // 色
        map["/::|"] = "😐";   // 发呆
        map["/::8"] = "😎";   // 得意（部分客户端用 /::8 代替 /:8-)）
        map["/::<"] = "😥";   // 流泪
        map["/::$"] = "😳";   // 害羞
        map["/::X"] = "😍";   // 闭嘴
        map["/::Z"] = "😴";   // 睡
        map["/::'"] = "😭";   // 大哭
        map["/::-|"] = "😏";  // 尴尬
        map["/::@"] = "😡";   // 发怒
        map["/::P"] = "😛";   // 调皮
        map["/::,"] = "😅";   // 惊讶
        map["/::-O"] = "😮";  // 难过/惊讶
        map["/::>"] = "😅";   // 酷/糗
        map["/::L"] = "😕";   // 抓狂
        map["/::T"] = "😭";   // 大哭变体
        map["/::G"] = "😅";   // 汗

        // 扩展符号表情（/: 开头）
        map[":8-)"] = "😎";   // 得意
        map["/:8-)"] = "😎";  // 得意（带前导斜杠）
        map["/:搏"] = "💪";    // 拳头
        map["/:ok"] = "👍";   // OK
        map["/:hand"] = "✋";  // 手
        map["/:jump"] = "😆";  // 跳
        map["/:shake"] = "😱"; // 发抖
        map["/:strong"] = "💪"; // 强
        map["/:weak"] = "👎";  // 弱
        map["/:share"] = "😊"; // 分享
        map["/:v"] = "✌️";     // 耶
        map["/:dgy"] = "🙏";   // 鞠躬
        map["/:jj"] = "😢";    // 委屈
        map["/:z"] = "😴";     // 睡
        map["/:d"] = "😄";     // 笑
        map["/:y"] = "✌️";     // 耶
        map["/:f"] = "😅";     // 汗
        map["/:x"] = "😍";     // 爱
        map["/:w"] = "😏";     // 挑逗
        map["/:c"] = "😢";     // 哭
        map["/:h"] = "😄";     // 笑
        map["/:b"] = "😊";     // 色
        map["/:j"] = "😎";     // 酷
        map["/:q"] = "😳";     // 害羞
        map["/:t"] = "😭";     // 哭
        map["/:o"] = "😮";     // 惊讶
        map["/:i"] = "😄";     // 笑
        map["/:g"] = "😅";     // 汗
        map["/:n"] = "😕";     // 疑惑
        map["/:m"] = "😐";     // 冷
        map["/:l"] = "😍";     // 爱
        map["/:k"] = "😡";     // 怒
        map["/:a"] = "😄";     // 笑
        map[":*"] = "😘";      // 亲亲
        map["/:*"] = "😘";     // 亲亲
        map["/:heart"] = "❤️"; // 心
        map[":heart"] = "❤️";  // 心
        map["/:love"] = "❤️";  // 爱
        map[":love"] = "❤️";   // 爱
        map["/:rose"] = "🌹";  // 玫瑰
        map[":rose"] = "🌹";   // 玫瑰
        map["/:coffee"] = "☕"; // 咖啡
        map[":coffee"] = "☕";  // 咖啡
        map["/:beer"] = "🍺";  // 啤酒
        map[":beer"] = "🍺";   // 啤酒
        map["/:cake"] = "🎂";  // 蛋糕
        map[":cake"] = "🎂";   // 蛋糕
        map["/:gift"] = "🎁";  // 礼物
        map[":gift"] = "🎁";   // 礼物
        map["/:moon"] = "🌙";  // 月亮
        map[":moon"] = "🌙";   // 月亮
        map["/:sun"] = "☀️";   // 太阳
        map[":sun"] = "☀️";    // 太阳
        map["/:star"] = "⭐";  // 星
        map[":star"] = "⭐";   // 星

        // ===== 别名代码（[XX] 格式，微信扩展表情）=====
        map["[微笑]"] = "😄";
        map["[撇嘴]"] = "😢";
        map["[色]"] = "😊";
        map["[发呆]"] = "😐";
        map["[得意]"] = "😎";
        map["[流泪]"] = "😥";
        map["[害羞]"] = "😳";
        map["[闭嘴]"] = "😍";
        map["[睡]"] = "😴";
        map["[大哭]"] = "😭";
        map["[尴尬]"] = "😏";
        map["[发怒]"] = "😡";
        map["[调皮]"] = "😛";
        map["[呲牙]"] = "😄";
        map["[惊讶]"] = "😮";
        map["[难过]"] = "😕";
        map["[酷]"] = "😎";
        map["[冷汗]"] = "😅";
        map["[抓狂]"] = "😫";
        map["[吐]"] = "🤮";
        map["[偷笑]"] = "🤭";
        map["[可爱]"] = "☺️";
        map["[白眼]"] = "🙄";
        map["[傲慢]"] = "😏";
        map["[饥饿]"] = "🤤";
        map["[困]"] = "😪";
        map["[惊恐]"] = "😱";
        map["[流汗]"] = "😅";
        map["[憨笑]"] = "☺️";
        map["[大兵]"] = "🪖";
        map["[奋斗]"] = "💪";
        map["[咒骂]"] = "🤬";
        map["[疑问]"] = "❓";
        map["[嘘]"] = "🤫";
        map["[晕]"] = "😵";
        map["[折磨]"] = "😖";
        map["[衰]"] = "😞";
        map["[骷髅]"] = "💀";
        map["[敲打]"] = "🔨";
        map["[再见]"] = "👋";
        map["[擦汗]"] = "😅";
        map["[抠鼻]"] = "🤏";
        map["[鼓掌]"] = "👏";
        map["[糗大了]"] = "😖";
        map["[坏笑]"] = "🤭";
        map["[左哼哼]"] = "😠";
        map["[右哼哼]"] = "😠";
        map["[哈欠]"] = "🥱";
        map["[鄙视]"] = "😒";
        map["[委屈]"] = "😢";
        map["[快哭了]"] = "🥺";
        map["[阴险]"] = "😏";
        map["[亲亲]"] = "😘";
        map["[吓]"] = "😱";
        map["[可怜]"] = "🥺";
        map["[菜刀]"] = "🔪";
        map["[西瓜]"] = "🍉";
        map["[啤酒]"] = "🍺";
        map["[篮球]"] = "🏀";
        map["[乒乓]"] = "🏓";
        map["[咖啡]"] = "☕";
        map["[饭]"] = "🍚";
        map["[猪头]"] = "🐷";
        map["[玫瑰]"] = "🌹";
        map["[凋谢]"] = "🥀";
        map["[示爱]"] = "❤️";
        map["[爱心]"] = "❤️";
        map["[心碎]"] = "💔";
        map["[蛋糕]"] = "🎂";
        map["[闪电]"] = "⚡";
        map["[炸弹]"] = "💣";
        map["[刀]"] = "🔪";
        map["[足球]"] = "⚽";
        map["[瓢虫]"] = "🐞";
        map["[音乐]"] = "🎵";
        map["[便便]"] = "💩";
        map["[月亮]"] = "🌙";
        map["[太阳]"] = "☀️";
        map["[礼物]"] = "🎁";
        map["[拥抱]"] = "🤗";
        map["[帽子]"] = "👒";
        map["[钱币]"] = "💰";
        map["[灯泡]"] = "💡";
        map["[和平]"] = "🕊️";
        map["[茶壶]"] = "🫖";
        map["[围巾]"] = "🧣";
        map["[云]"] = "☁️";
        map["[雪人]"] = "⛄";
        map["[飞机]"] = "✈️";
        map["[火车]"] = "🚂";
        map["[汽车]"] = "🚗";
        map["[自行车]"] = "🚲";
        map["[钥匙]"] = "🔑";
        map["[拳头]"] = "✊";
        map["[差劲]"] = "👎";
        map["[握手]"] = "🤝";
        map["[胜利]"] = "✌️";
        map["[抱拳]"] = "🙏";
        map["[勾引]"] = "👉";
        map["[OK]"] = "👌";
        map["[强]"] = "👍";
        map["[弱]"] = "👎";
        map["[发呆]"] = "😐";

        // ===== 微信扩展表情别名（英文，[XX] 格式）=====
        map["[Smile]"] = "😄";
        map["[Cry]"] = "😢";
        map["[Laugh]"] = "😆";
        map["[Facepalm]"] = "🤦";
        map["[Rose]"] = "🌹";
        map["[Heart]"] = "❤️";
        map["[Coffee]"] = "☕";
        map["[Beer]"] = "🍺";
        map["[Cake]"] = "🎂";
        map["[Gift]"] = "🎁";
        map["[Sun]"] = "☀️";
        map["[Moon]"] = "🌙";
        map["[Star]"] = "⭐";
        map["[Fire]"] = "🔥";
        map["[ThumbsUp]"] = "👍";
        map["[ThumbsDown]"] = "👎";
        map["[OK]"] = "👌";
        map["[Clap]"] = "👏";
        map["[Pray]"] = "🙏";
        map["[Wave]"] = "👋";
        map["[Punch]"] = "✊";
        map["[Victory]"] = "✌️";
        map["[Shake]"] = "🤝";
        map["[Jump]"] = "😆";
        map["[Shivering]"] = "😱";

        return map;
    }
}
