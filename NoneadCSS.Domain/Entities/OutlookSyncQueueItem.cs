using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// Outlook 同步重试队列：单项同步失败时入队，由 BackgroundService 按指数退避重试。
/// 超过 MaxRetry 次后标记为 DeadLettered（死信），需人工介入。
/// 符合任务 D#3 规范。
/// </summary>
public class OutlookSyncQueueItem
{
    public int Id { get; set; }

    /// <summary>
    /// 关联邮箱 ID。
    /// </summary>
    public int MailboxId { get; set; }

    /// <summary>
    /// 关联的本地事件 ID（可空，例如 Outlook 端拉取到的新事件尚无映射）。
    /// </summary>
    public int? EventId { get; set; }

    /// <summary>
    /// 方向：Inbound（Outlook→本地） / Outbound（本地→Outlook）。
    /// </summary>
    [MaxLength(20)]
    public string Direction { get; set; } = null!;

    /// <summary>
    /// 操作类型：Create / Update / Delete。
    /// </summary>
    [MaxLength(20)]
    public string Operation { get; set; } = null!;

    /// <summary>
    /// 同步载荷 JSON（序列化 OutlookEventDto 或 CalendarEvent 相关字段），供重试时还原上下文。
    /// 注意：不指定 ColumnTypeName，由 EF Core 根据数据库提供程序自动映射：
    /// - SQL Server → nvarchar(max)
    /// - SQLite   → TEXT（SQLite 不支持 nvarchar(max)，DDL 会报 near "max": syntax error）
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// 已重试次数（默认 0）。
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// 最大重试次数（默认 5）。
    /// </summary>
    public int MaxRetry { get; set; } = 5;

    /// <summary>
    /// 下次允许重试时间（可空；null 表示立即可重试）。使用指数退避。
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; set; }

    /// <summary>
    /// 状态：Pending / Processing / Retrying / DeadLettered / Failed。
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    /// <summary>
    /// 最近一次错误摘要（失败时写入）。
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// 创建时间（UTC）。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 最后更新时间（UTC）。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [ForeignKey(nameof(MailboxId))]
    public Mailbox? Mailbox { get; set; }
}
