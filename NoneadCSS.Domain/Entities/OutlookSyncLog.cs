using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// Outlook 同步审计日志：记录每次同步的整体结果（成功/部分/失败）及数量统计。
/// 与任务 D#2 规范一致：以 Mailbox 为维度（非 UserId），记录 Direction / Status / 计数 / 错误信息。
/// </summary>
public class OutlookSyncLog
{
    public int Id { get; set; }

    /// <summary>
    /// 关联邮箱 ID。
    /// </summary>
    public int MailboxId { get; set; }

    /// <summary>
    /// 同步方向：Inbound / Outbound / Both。
    /// </summary>
    [MaxLength(20)]
    public string Direction { get; set; } = null!;

    /// <summary>
    /// 同步开始时间（UTC）。
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// 同步结束时间（UTC）。
    /// </summary>
    public DateTimeOffset FinishedAt { get; set; }

    /// <summary>
    /// 整体状态：Success / Partial / Failed。
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    /// <summary>
    /// 同步期间新建事件数量。
    /// </summary>
    public int CreatedCount { get; set; }

    /// <summary>
    /// 同步期间更新事件数量。
    /// </summary>
    public int UpdatedCount { get; set; }

    /// <summary>
    /// 同步期间删除事件数量。
    /// </summary>
    public int DeletedCount { get; set; }

    /// <summary>
    /// 同步期间错误数量。
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// 同步期间检测到的冲突数量（双方都修改过的重复事件）。
    /// </summary>
    public int ConflictCount { get; set; }

    /// <summary>
    /// 冲突详情 JSON 数组：[{eventId, outlookId, strategy, winner, localModified, outlookModified, timestamp}]。
    /// 便于排查"哪个事件冲突、用了什么策略、谁胜出"。null 表示无冲突。
    /// </summary>
    public string? ConflictDetailsJson { get; set; }

    /// <summary>
    /// 错误消息摘要（Status != Success 时写入）。
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 原始载荷（可选，JSON / XML 字符串），便于排障。
    /// 注意：不指定 ColumnTypeName，由 EF Core 根据数据库提供程序自动映射：
    /// - SQL Server → nvarchar(max)
    /// - SQLite   → TEXT（SQLite 不支持 nvarchar(max)，DDL 会报 near "max": syntax error）
    /// </summary>
    public string? RawPayload { get; set; }

    /// <summary>
    /// 记录创建时间（UTC）。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [ForeignKey(nameof(MailboxId))]
    public Mailbox? Mailbox { get; set; }
}
