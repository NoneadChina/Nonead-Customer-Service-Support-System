namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 日历事件模板 - 用于快速创建常用日程（如"周会"、"每日站会"、"1v1"等）
/// </summary>
public class CalendarEventTemplate : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>模板名称（用于在模板选择器中显示）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>事件标题（应用模板时填充到事件标题）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>事件类型：due_date / task / meeting / reminder</summary>
    public string EventType { get; set; } = "task";

    /// <summary>默认时长（分钟），0 表示全天</summary>
    public int DurationMin { get; set; } = 60;

    /// <summary>是否为全天事件</summary>
    public bool IsAllDay { get; set; } = false;

    /// <summary>模板描述（可选，说明模板用途）</summary>
    public string? Description { get; set; }

    /// <summary>默认地点（可选，应用模板时填充）</summary>
    public string? Location { get; set; }

    /// <summary>排序顺序（数值越小越靠前）</summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>是否为全局共享模板（所有用户可见）</summary>
    public bool IsShared { get; set; } = true;

    /// <summary>关联邮箱 ID（null 表示不绑定邮箱）</summary>
    public int? MailboxId { get; set; }

    /// <summary>创建者用户 ID（个人模板的创建者）</summary>
    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>软删除标记</summary>
    public DateTime? DeletedAt { get; set; }

    // 导航属性
    public Mailbox? Mailbox { get; set; }
    public User? CreatedByUser { get; set; }

    public bool IsDeleted() => DeletedAt.HasValue;

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Restore()
    {
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
