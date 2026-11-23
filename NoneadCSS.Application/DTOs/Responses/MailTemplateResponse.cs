using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Responses;

public class MailTemplateResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public MailTemplateType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string? VariablesJson { get; set; }
    public List<MailTemplateVariable> Variables { get; set; } = [];
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class MailTemplateVariable
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
    public string? Category { get; set; }
}

public class MailTemplateOverviewResponse
{
    public int Total { get; set; }
    public int ActiveCount { get; set; }
    public int SystemCount { get; set; }
    public int CustomCount { get; set; }
    public List<MailTemplateTypeCount> TypeCounts { get; set; } = [];
}

public class MailTemplateTypeCount
{
    public MailTemplateType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class MailTemplatePreviewResponse
{
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
}

/// <summary>
/// 完整渲染后的邮件模板（主题 + HTML 正文 + 纯文本正文）
/// </summary>
public class RenderedTemplateResponse
{
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
}
