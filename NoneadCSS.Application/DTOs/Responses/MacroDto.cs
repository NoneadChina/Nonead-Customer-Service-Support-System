namespace NoneadCSS.Application.DTOs.Responses;

public class MacroDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<MacroActionDto> Actions { get; set; } = new();

    /// <summary>ActionsJson 反序列化是否失败（true 表示动作数据可能损坏，前端应提示用户检查）</summary>
    public bool HasActionsParseError { get; set; }

    /// <summary>动作的可读化描述（如「将状态改为 已关闭 → 添加标签 已完成」）</summary>
    public string ActionsDescription { get; set; } = string.Empty;
}

public class MacroActionDto
{
    public string Type { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string? Note { get; set; }
}

public class MacroExecutionLogDto
{
    public int Id { get; set; }
    public int MacroId { get; set; }
    public string MacroName { get; set; } = string.Empty;
    public int ConversationId { get; set; }
    public string? ConversationSubject { get; set; }
    public int ExecutedByUserId { get; set; }
    public string? ExecutedByUserName { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ActionsExecuted { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAt { get; set; }
}

public class MacroOverviewResponse
{
    public int TotalMacros { get; set; }
    public int ActiveMacros { get; set; }
    public int InactiveMacros { get; set; }
    public int TotalExecutions { get; set; }
    public int TodayExecutions { get; set; }
    public Dictionary<string, string> ActionTypes { get; set; } = new();
    public List<MacroExecutionLogDto> RecentExecutions { get; set; } = new();
    public List<MacroDto> TopMacros { get; set; } = new();
}

public class CreateMacroRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public List<MacroActionDto> Actions { get; set; } = new();
}

public class UpdateMacroRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public List<MacroActionDto> Actions { get; set; } = new();
}

public class ExecuteMacroRequest
{
    public int ConversationId { get; set; }
}

public class ExecuteMacroResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ActionsExecuted { get; set; }
    public List<string> ExecutedActions { get; set; } = new();
}

public class MacroQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? MailboxId { get; set; }
    public bool? IsActive { get; set; }
    public string? Keyword { get; set; }
}

public class MacroExecutionLogQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? MacroId { get; set; }
    public int? ConversationId { get; set; }
}

/// <summary>宏执行预览响应（不实际执行）</summary>
public class MacroPreviewResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int MacroId { get; set; }
    public string MacroName { get; set; } = string.Empty;
    public int ConversationId { get; set; }
    public string? ConversationSubject { get; set; }
    public int ActionCount { get; set; }
    public List<MacroActionPreviewItem> Actions { get; set; } = new();
}

public class MacroActionPreviewItem
{
    public string Type { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>复制宏请求</summary>
public class DuplicateMacroRequest
{
    public string? NewName { get; set; }
}
