using System.Text.Json;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 宏（Macro）- 预定义的批量操作集合，可一键执行多个动作
/// </summary>
public class Macro : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>宏名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>宏描述</summary>
    public string? Description { get; set; }

    /// <summary>所属邮箱 ID。null 表示全局可用。</summary>
    public int? MailboxId { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>排序顺序</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 动作配置 JSON。结构：
    /// [
    ///   { type: "reply", value: "回复内容", note: false },
    ///   { type: "status", value: 3 },
    ///   { type: "assign", value: 2 },
    ///   { type: "add_tag", value: "已处理" },
    ///   { type: "remove_tag", value: "待办" },
    ///   { type: "add_note", value: "备注内容" },
    ///   { type: "priority", value: 2 }
    /// ]
    /// </summary>
    public string ActionsJson { get; set; } = "[]";

    /// <summary>使用次数统计</summary>
    public int UsageCount { get; set; }

    /// <summary>最后使用时间</summary>
    public DateTime? LastUsedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>软删除时间。null=未删除，非空=已删除</summary>
    public DateTime? DeletedAt { get; set; }

    public Mailbox? Mailbox { get; set; }

    /// <summary>
    /// 是否已被删除（软删除）。
    /// </summary>
    public bool IsDeleted() => DeletedAt.HasValue;

    /// <summary>
    /// 是否可应用于指定邮箱。
    /// </summary>
    public bool AppliesToMailbox(int mailboxId)
        => !MailboxId.HasValue || MailboxId.Value == mailboxId;

    /// <summary>
    /// 是否可在指定邮箱上执行（已启用且未删除）。
    /// </summary>
    public bool CanExecuteFor(int mailboxId)
        => IsActive && !IsDeleted() && AppliesToMailbox(mailboxId);

    /// <summary>
    /// 反序列化动作配置，自动过滤掉无效的动作（Type 为空的项）。
    /// </summary>
    public List<MacroAction> DeserializeActions(JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(ActionsJson))
            return new List<MacroAction>();
        try
        {
            var actions = JsonSerializer.Deserialize<List<MacroAction>>(ActionsJson, options) ?? new List<MacroAction>();
            return actions.Where(a => !string.IsNullOrWhiteSpace(a.Type)).ToList();
        }
        catch
        {
            return new List<MacroAction>();
        }
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
            // 新命名（与 Workflow 保持一致）
            "set_status", "assign_user", "add_tag", "remove_tag",
            "send_reply", "set_priority", "add_note",
            // 旧命名（向后兼容数据库中已存储的旧命名宏）
            "status", "assign", "priority", "reply"
        };

        var actions = DeserializeActions();
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (string.IsNullOrWhiteSpace(action.Type))
            {
                invalidActions.Add($"#{i + 1} (空类型)");
            }
            else if (!validTypes.Contains(action.Type))
            {
                invalidActions.Add($"#{i + 1} ({action.Type})");
            }
        }

        return invalidActions.Count == 0;
    }

    /// <summary>
    /// 记录一次使用。
    /// </summary>
    public void RecordUsage()
    {
        UsageCount++;
        LastUsedAt = DateTime.UtcNow;
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
}

/// <summary>
/// 宏执行日志
/// </summary>
public class MacroExecutionLog
{
    public int Id { get; set; }
    public int MacroId { get; set; }
    public int ConversationId { get; set; }
    public int ExecutedByUserId { get; set; }

    /// <summary>执行状态：success / failed / partial</summary>
    public string Status { get; set; } = "success";

    /// <summary>执行的动作数量</summary>
    public int ActionsExecuted { get; set; }

    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    public Macro? Macro { get; set; }
    public Conversation? Conversation { get; set; }
    public User? ExecutedByUser { get; set; }
}
