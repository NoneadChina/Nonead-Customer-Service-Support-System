using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 时间追踪记录 - 记录客服处理工单/客户/独立任务花费的时间
/// </summary>
public class TimeEntry : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>
    /// 记录类型：timer（计时器）/ manual（手动录入）
    /// </summary>
    public string EntryType { get; set; } = "manual";

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>关联工单ID（可选）</summary>
    public int? ConversationId { get; set; }

    /// <summary>关联客户ID（可选）</summary>
    public int? CustomerId { get; set; }

    /// <summary>所属用户ID（必填）</summary>
    public int UserId { get; set; }

    /// <summary>关联邮箱ID（可选）</summary>
    public int? MailboxId { get; set; }

    /// <summary>开始时间（UTC）</summary>
    public DateTime StartTime { get; set; }

    /// <summary>结束时间（UTC），null 表示计时器运行中</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 时长（分钟）。手动录入直接填，计时器停止时计算。
    /// 运行中的计时器此字段为 0。
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>是否可计费</summary>
    public bool Billable { get; set; } = false;

    /// <summary>计时器是否运行中（EndTime == null）</summary>
    public bool IsRunning => EndTime == null;

    // 审计字段
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // 乐观锁
    public byte[]? RowVersion { get; set; }

    // 导航属性
    public Conversation? Conversation { get; set; }
    public Customer? Customer { get; set; }
    public User? User { get; set; }
    public Mailbox? Mailbox { get; set; }
}
