namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// P3-D: Outlook 同步健康仪表盘 DTO（按 Mailbox 维度）
/// </summary>
public class CalendarOutlookHealthDashboardDto
{
    /// <summary>
    /// 邮箱 ID
    /// </summary>
    public int MailboxId { get; set; }

    /// <summary>
    /// 邮箱地址
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 最近一次成功同步时间（UTC），null 表示从未成功
    /// </summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>
    /// 最近一次尝试同步时间（UTC），无论成功失败
    /// </summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// 连续失败次数（成功时归零）
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// 最近一次错误码（从 ErrorMessage 中提取或统一归类），null 表示无错误
    /// </summary>
    public string? LastErrorCode { get; set; }

    /// <summary>
    /// 最近一次错误信息摘要
    /// </summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// 最近 7 天内处理的总记录数（CreatedCount + UpdatedCount + DeletedCount）
    /// </summary>
    public long RecordsProcessedLast7Days { get; set; }

    /// <summary>
    /// 当前 DeltaToken 是否存在且有效（true=可增量同步，false=将触发全量同步）
    /// </summary>
    public bool DeltaTokenValid { get; set; }

    /// <summary>
    /// DeltaToken 上次刷新时间（UTC）
    /// </summary>
    public DateTimeOffset? DeltaTokenUpdatedAt { get; set; }

    /// <summary>
    /// 最近 10 次同步历史摘要（只读聚合）
    /// </summary>
    public List<CalendarOutlookSyncHistoryItem> RecentHistory { get; set; } = [];

    /// <summary>
    /// 最近同步中出现频率最高的 Top3 错误码
    /// </summary>
    public List<(string ErrorCode, int Count)> TopErrorCodes { get; set; } = [];
}

/// <summary>
/// 历史同步摘要项
/// </summary>
public class CalendarOutlookSyncHistoryItem
{
    public int Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int DeletedCount { get; set; }
    public int ErrorCount { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
