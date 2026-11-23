namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 宏动作（领域模型）。
/// </summary>
public class MacroAction
{
    /// <summary>
    /// 动作类型：set_status / assign_user / set_priority / add_tag / remove_tag / add_note / send_reply
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 动作值。根据类型不同可能是状态值、用户ID、标签名、回复内容等。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 附加说明（如备注是否对客户可见等）。
    /// </summary>
    public string? Note { get; set; }
}
