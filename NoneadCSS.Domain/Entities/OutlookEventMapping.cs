namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 日历事件与 Outlook 事件的映射关系
/// </summary>
public class OutlookEventMapping
{
    public int Id { get; set; }

    /// <summary>
    /// 本地日历事件 ID
    /// </summary>
    public int CalendarEventId { get; set; }

    /// <summary>
    /// Outlook 事件 ID（Graph API 的 id 字段）
    /// </summary>
    public string OutlookEventId { get; set; } = string.Empty;

    /// <summary>
    /// 关联用户 ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 事件来源：local（本地创建）/ outlook（Outlook 创建）
    /// </summary>
    public string Source { get; set; } = "local";

    /// <summary>
    /// Outlook 事件的最后修改时间（用于冲突检测）
    /// </summary>
    public DateTime? OutlookLastModified { get; set; }

    /// <summary>
    /// 本地事件的最后修改时间（用于冲突检测）
    /// </summary>
    public DateTime? LocalLastModified { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
