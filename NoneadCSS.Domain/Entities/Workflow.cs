using System.Text.Json;
using System.Text.RegularExpressions;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 工作流自动化规则。当满足触发条件时自动执行预设动作。
/// </summary>
public class Workflow : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 所属邮箱 ID。null 表示对所有邮箱生效。
    /// </summary>
    public int? MailboxId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 是否系统内置工作流。系统内置不可删除，但可停用。
    /// </summary>
    public bool IsSystem { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// 触发类型：new_conversation / new_reply / status_changed
    /// </summary>
    public string TriggerType { get; set; } = "new_conversation";

    /// <summary>
    /// 条件配置 JSON。结构：{ matchType: "all|any", conditions: [{ field, operator, value }] }
    /// </summary>
    public string? ConditionsJson { get; set; }

    /// <summary>
    /// 动作配置 JSON。结构：[{ type, value }]
    /// </summary>
    public string? ActionsJson { get; set; }

    /// <summary>
    /// 累计匹配次数
    /// </summary>
    public int MatchCount { get; set; }

    public DateTime? LastMatchedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public Mailbox? Mailbox { get; set; }

    /// <summary>
    /// 是否已被删除（软删除）。
    /// </summary>
    public bool IsDeleted() => DeletedAt.HasValue;

    /// <summary>
    /// 是否匹配指定触发类型。
    /// </summary>
    public bool MatchesTrigger(string triggerType)
        => IsActive && !IsDeleted() && TriggerType == triggerType;

    /// <summary>
    /// 是否可应用于指定邮箱。
    /// </summary>
    public bool AppliesToMailbox(int mailboxId)
        => !MailboxId.HasValue || MailboxId.Value == mailboxId;

    /// <summary>
    /// 是否可针对指定触发类型和邮箱执行。
    /// </summary>
    public bool CanExecuteFor(string triggerType, int mailboxId)
        => MatchesTrigger(triggerType) && AppliesToMailbox(mailboxId);

    /// <summary>
    /// 是否存在条件配置。
    /// </summary>
    public bool HasConditions()
        => !string.IsNullOrWhiteSpace(ConditionsJson);

    /// <summary>
    /// 是否存在动作配置。
    /// </summary>
    public bool HasActions()
        => !string.IsNullOrWhiteSpace(ActionsJson);

    /// <summary>
    /// JSON 反序列化选项（大小写不敏感，兼容 camelCase 与 PascalCase）。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 反序列化条件配置。
    /// </summary>
    public WorkflowConditionGroup DeserializeConditions()
    {
        if (string.IsNullOrWhiteSpace(ConditionsJson))
            return new WorkflowConditionGroup();
        try
        {
            return JsonSerializer.Deserialize<WorkflowConditionGroup>(ConditionsJson, JsonOptions) ?? new WorkflowConditionGroup();
        }
        catch
        {
            return new WorkflowConditionGroup();
        }
    }

    /// <summary>
    /// 反序列化动作配置。
    /// </summary>
    public List<WorkflowAction> DeserializeActions()
    {
        if (string.IsNullOrWhiteSpace(ActionsJson))
            return new List<WorkflowAction>();
        try
        {
            return JsonSerializer.Deserialize<List<WorkflowAction>>(ActionsJson, JsonOptions) ?? new List<WorkflowAction>();
        }
        catch
        {
            return new List<WorkflowAction>();
        }
    }

    /// <summary>
    /// 评估条件是否匹配。
    /// </summary>
    /// <param name="context">评估上下文</param>
    /// <param name="matchedConditions">匹配明细描述</param>
    /// <returns>是否匹配</returns>
    public bool EvaluateConditions(WorkflowEvaluationContext context, out List<string> matchedConditions)
    {
        matchedConditions = new List<string>();
        var group = DeserializeConditions();

        if (group == null || group.Conditions.Count == 0)
            return true;

        var results = new List<bool>();
        foreach (var cond in group.Conditions)
        {
            bool matched;
            string description;

            if (cond.Field == "tag")
            {
                matched = cond.Operator switch
                {
                    "contains" => context.Tags.Any(t => t.Contains(cond.Value, StringComparison.OrdinalIgnoreCase)),
                    "not_contains" => !context.Tags.Any(t => t.Contains(cond.Value, StringComparison.OrdinalIgnoreCase)),
                    "equals" => context.Tags.Any(t => string.Equals(t, cond.Value, StringComparison.OrdinalIgnoreCase)),
                    "not_equals" => !context.Tags.Any(t => string.Equals(t, cond.Value, StringComparison.OrdinalIgnoreCase)),
                    "starts_with" => context.Tags.Any(t => t.StartsWith(cond.Value, StringComparison.OrdinalIgnoreCase)),
                    "ends_with" => context.Tags.Any(t => t.EndsWith(cond.Value, StringComparison.OrdinalIgnoreCase)),
                    "regex" => context.Tags.Any(t => TryRegexMatch(t, cond.Value)),
                    _ => false
                };
                description = $"标签 {cond.Operator} \"{cond.Value}\" => {(matched ? "✓" : "✗")}";
            }
            else
            {
                var value = cond.Field switch
                {
                    "subject" => context.Subject,
                    "from_email" => context.FromEmail,
                    "to_email" => context.ToEmail,
                    "body" => context.Body,
                    "customer_name" => context.CustomerName,
                    "status" => context.Status,
                    "priority" => context.Priority,
                    "mailbox" => context.Mailbox,
                    "assignee" => context.Assignee,
                    "satisfaction_rating" => context.SatisfactionRating,
                    "satisfaction_comment" => context.SatisfactionComment,
                    "source_type" => context.SourceType,
                    _ => string.Empty
                };

                matched = MatchSingle(value, cond.Operator, cond.Value);
                description = $"{cond.Field} {cond.Operator} \"{cond.Value}\" => {(matched ? "✓" : "✗")}";
            }

            results.Add(matched);
            matchedConditions.Add(description);
        }

        return group.MatchType == "any" ? results.Any(r => r) : results.All(r => r);
    }

    /// <summary>
    /// 验证动作配置是否有效。
    /// </summary>
    /// <param name="invalidActions">无效动作列表</param>
    /// <returns>是否全部有效</returns>
    public bool ValidateActions(out List<string> invalidActions)
    {
        invalidActions = new List<string>();
        var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "set_status", "assign_user", "add_tag", "remove_tag",
            "send_reply", "set_priority", "forward", "send_email", "send_im_message", "conversation_process",
            "trigger_webhook", "execute_macro"
        };

        var actions = DeserializeActions();
        foreach (var action in actions)
        {
            if (!validTypes.Contains(action.Type))
            {
                invalidActions.Add(action.Type);
            }
        }

        return invalidActions.Count == 0;
    }

    /// <summary>
    /// 记录一次匹配。
    /// </summary>
    public void RecordMatch()
    {
        MatchCount++;
        LastMatchedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 软删除。
    /// </summary>
    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 恢复软删除。
    /// </summary>
    public void Restore()
    {
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    private static bool MatchSingle(string value, string op, string target)
    {
        if (string.IsNullOrEmpty(target) && op != "regex") return false;

        return op switch
        {
            "contains" => value.Contains(target, StringComparison.OrdinalIgnoreCase),
            "not_contains" => !value.Contains(target, StringComparison.OrdinalIgnoreCase),
            "equals" => string.Equals(value, target, StringComparison.OrdinalIgnoreCase),
            "not_equals" => !string.Equals(value, target, StringComparison.OrdinalIgnoreCase),
            "starts_with" => value.StartsWith(target, StringComparison.OrdinalIgnoreCase),
            "ends_with" => value.EndsWith(target, StringComparison.OrdinalIgnoreCase),
            "regex" => TryRegexMatch(value, target),
            _ => false
        };
    }

    private static bool TryRegexMatch(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 工作流执行日志。记录每次工作流触发的执行结果。
/// </summary>
public class WorkflowExecutionLog
{
    public int Id { get; set; }
    public int WorkflowId { get; set; }

    /// <summary>
    /// 关联会话 ID（可空）。非会话场景（如 ChatSession 级 DummyConversation）必须为 null，
    /// 禁止写入 0 以避免违反外键约束（Conversations 表不存在 Id=0 的记录）。
    /// </summary>
    public int? ConversationId { get; set; }

    /// <summary>
    /// 执行状态：success / failed / partial
    /// </summary>
    public string Status { get; set; } = "success";

    /// <summary>
    /// 执行的动作数量
    /// </summary>
    public int ActionsExecuted { get; set; }

    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    public Workflow? Workflow { get; set; }
    public Conversation? Conversation { get; set; }
}
