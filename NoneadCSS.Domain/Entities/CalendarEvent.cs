namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 日历事件 - 表示工单截止日期、计划任务、会议等日历项
/// </summary>
public class CalendarEvent : ISoftDelete
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 事件类型：due_date (截止日期) / task (计划任务) / meeting (会议) / reminder (提醒)
    /// </summary>
    public string EventType { get; set; } = "task";

    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsAllDay { get; set; } = true;

    /// <summary>
    /// 关联会话ID（可选，用于将事件绑定到工单）
    /// </summary>
    public int? ConversationId { get; set; }

    public int? MailboxId { get; set; }
    public int? UserId { get; set; }

    /// <summary>
    /// 状态：pending (待处理) / in_progress (进行中) / completed (已完成) / cancelled (已取消) / missed (已错过)
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// 颜色标记（用于前端显示）
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// 创建者用户 ID（用于权限校验，专员可编辑自己创建的事件）
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// 优先级（1-5，5最高，用于截止日期）
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// 进度（0-100，用于任务）
    /// </summary>
    public int? Progress { get; set; }

    /// <summary>
    /// 地点（线上/线下，用于会议）
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// 会议链接（用于会议）
    /// </summary>
    public string? MeetingUrl { get; set; }

    /// <summary>
    /// 参会人/参与人用户 ID 数组 JSON
    /// </summary>
    public string? AttendeesJson { get; set; }

    /// <summary>
    /// 子任务数组 JSON
    /// </summary>
    public string? SubTasksJson { get; set; }

    /// <summary>
    /// 关联事件 ID（用于提醒）
    /// </summary>
    public int? RelatedEventId { get; set; }

    /// <summary>
    /// 重复日程排除/额外日期 JSON
    /// </summary>
    public string? RecurrenceExclusionsJson { get; set; }

    // 软删除
    public DateTime? DeletedAt { get; set; }

    // 重复日程
    /// <summary>
    /// 是否重复
    /// </summary>
    public bool IsRecurring { get; set; }

    /// <summary>
    /// 重复类型：0=每日，1=每周，2=每月，3=每年
    /// </summary>
    public int? RecurrenceType { get; set; }

    /// <summary>
    /// 重复间隔（每 N 天/周/月/年）
    /// </summary>
    public int? RecurrenceInterval { get; set; }

    /// <summary>
    /// 重复结束日期
    /// </summary>
    public DateTime? RecurrenceEndDate { get; set; }

    /// <summary>
    /// 重复次数上限
    /// </summary>
    public int? RecurrenceCount { get; set; }

    /// <summary>
    /// 主持人用户 ID 数组 JSON（会议类型专属，前端多选下拉菜单）
    /// </summary>
    public string? HostUserIdsJson { get; set; }

    /// <summary>
    /// 议程 ID 数组 JSON（会议类型专属，关联外部议程条目）
    /// </summary>
    public string? AgendaIdsJson { get; set; }

    /// <summary>
    /// 任务负责人用户 ID（任务类型专属，区别于 UserId 事件归属者）
    /// </summary>
    public int? TaskOwnerId { get; set; }

    /// <summary>
    /// 任务截止时间（任务类型专属，区别于 EndAt 结束时间，用于任务到期提醒）
    /// </summary>
    public DateTime? DueAt { get; set; }

    /// <summary>
    /// 提醒方式：popup / email / sms（提醒类型专属，配合 ReminderMinutes 使用）
    /// </summary>
    public string? ReminderType { get; set; }

    /// <summary>
    /// 每周哪几天（JSON 数组，0=周日..6=周六，重复日程面板专属）
    /// </summary>
    public string? WeekdaysSelectedJson { get; set; }

    /// <summary>
    /// RRULE 原始字符串（重复日程面板生成的描述规则，区别于 RecurrenceType 枚举）
    /// </summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>
    /// 重复规则原始事件ID（用于展开后的实例关联原始事件）
    /// </summary>
    public int? RecurrenceParentId { get; set; }

    /// <summary>
    /// 重复实例的原始开始日期（用于标识是哪一次的实例）
    /// </summary>
    public DateTime? RecurrenceOriginalStart { get; set; }

    // 提醒
    /// <summary>
    /// 提前多少分钟提醒
    /// </summary>
    public int? ReminderMinutes { get; set; }

    /// <summary>
    /// 提醒是否已发送
    /// </summary>
    public bool IsReminderSent { get; set; }

    /// <summary>
    /// 额外提醒邮件地址（逗号分隔），用于发送提醒邮件给非系统用户
    /// </summary>
    public string? ReminderEmails { get; set; }

    /// <summary>
    /// 上次发送提醒的开始时间（用于周期性事件每周期重置提醒）
    /// </summary>
    public DateTime? LastReminderSentAt { get; set; }

    /// <summary>
    /// 实际发送提醒的时间（用于 fallback 判定窗口）
    /// </summary>
    public DateTimeOffset? ReminderSentAt { get; set; }

    /// <summary>
    /// 提醒确认状态：pending / confirmed / fallback_sent / failed
    /// </summary>
    public string ReminderConfirmation { get; set; } = "pending";

    /// <summary>
    /// 提醒被确认的时间
    /// </summary>
    public DateTimeOffset? ReminderConfirmedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// H-S1: 乐观锁版本号，防止并发更新覆盖
    /// </summary>
    public byte[] RowVersion { get; set; } = default!;

    // 导航属性
    public Conversation? Conversation { get; set; }
    public Mailbox? Mailbox { get; set; }
    public User? User { get; set; }
    /// <summary>创建者用户导航属性（用于 ToDto 直接映射 CreatedByName，避免额外查询）</summary>
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
