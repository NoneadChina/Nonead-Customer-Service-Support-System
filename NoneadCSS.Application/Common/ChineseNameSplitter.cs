namespace NoneadCSS.Application.Common;

/// <summary>
/// 中文姓名拆分工具：将完整中文姓名拆分为姓（LastName）和名（FirstName）。
/// 规则：
///   - 复姓（欧阳、司马、诸葛等）：姓 = 前两字，名 = 其余
///   - 单姓：姓 = 第一字，名 = 其余
///   - 非中文姓名：整体放入 FirstName，LastName 留空
/// </summary>
public static class ChineseNameSplitter
{
    /// <summary>
    /// 常见中文复姓列表。
    /// </summary>
    private static readonly HashSet<string> CompoundSurnames = new(StringComparer.Ordinal)
    {
        "欧阳", "司马", "诸葛", "上官", "欧阳", "皇甫", "尉迟", "申屠",
        "司马", "东方", "独孤", "公孙", "慕容", "南宫", "西门", "司空",
        "戴妫", "叔孙", "微生", "公羊", "赫连", "澹台", "宗政", "濮阳",
        "公冶", "太叔", "仲孙", "轩辕", "令狐", "钟离", "宇文", "长孙",
        "鲜于", "闾丘", "司徒", "司寇", "仉督", "子车", "颛孙", "端木",
        "巫马", "公西", "漆雕", "乐正", "壤驷", "公良", "拓跋", "夹谷",
        "宰父", "谷梁", "段干", "百里", "东郭", "南门", "呼延", "羊舌",
        "梁丘", "左丘", "东门", "西门", "第五", "万俟"
    };

    /// <summary>
    /// 将完整姓名拆分为姓（LastName）和名（FirstName）。
    /// 对于中文姓名：LastName = 姓氏，FirstName = 名字。
    /// 对于非中文姓名：FirstName = 完整姓名，LastName = 空。
    /// </summary>
    /// <param name="fullName">完整姓名（如"张三"、"欧阳锋"、"John Doe"）</param>
    /// <returns>(lastName, firstName) 元组</returns>
    public static (string LastName, string FirstName) Split(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (string.Empty, string.Empty);

        var name = fullName.Trim();

        // 判断是否为纯中文姓名（2-4 个汉字，无空格、无拉丁字母）
        if (IsChineseName(name))
        {
            // 检查复姓
            if (name.Length >= 3 && CompoundSurnames.Contains(name[..2]))
            {
                return (name[..2], name[2..]);
            }

            // 单姓：姓 = 第一字，名 = 其余
            if (name.Length >= 2)
            {
                return (name[..1], name[1..]);
            }

            // 单字名：只有姓没有名
            return (name, string.Empty);
        }

        // 非中文姓名：整体放入 FirstName
        return (string.Empty, name);
    }

    /// <summary>
    /// 判断是否为合法的中文姓名（2-4 个汉字，无空格）。
    /// </summary>
    private static bool IsChineseName(string name)
    {
        if (name.Length < 2 || name.Length > 4)
            return false;

        foreach (var ch in name)
        {
            if (ch < '\u4e00' || ch > '\u9fff')
                return false;
        }

        return true;
    }
}
