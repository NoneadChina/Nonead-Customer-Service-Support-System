namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 工作流条件组。
/// </summary>
public class WorkflowConditionGroup
{
    /// <summary>
    /// 匹配类型：all（全部满足）/ any（任意满足）
    /// </summary>
    public string MatchType { get; set; } = "all";

    public List<WorkflowCondition> Conditions { get; set; } = new();
}

/// <summary>
/// 工作流条件。
/// </summary>
public class WorkflowCondition
{
    /// <summary>
    /// 条件字段：subject / from_email / body / to_email / customer_name 等
    /// </summary>
    public string Field { get; set; } = "subject";

    /// <summary>
    /// 操作符：contains / not_contains / equals / starts_with / ends_with / regex
    /// </summary>
    public string Operator { get; set; } = "contains";

    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 工作流动作。
/// </summary>
public class WorkflowAction
{
    /// <summary>
    /// 动作类型：set_status / assign_user / add_tag / remove_tag / send_reply / set_priority / forward
    /// </summary>
    public string Type { get; set; } = "set_status";

    public string? Value { get; set; }

    /// <summary>
    /// 附加参数（如回复正文、收件人等）
    /// </summary>
    public string? Extra { get; set; }
}
