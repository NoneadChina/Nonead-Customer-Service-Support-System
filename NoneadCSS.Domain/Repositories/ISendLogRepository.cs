using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// 邮件发送日志仓储接口
/// </summary>
public interface ISendLogRepository : IRepository<SendLog>
{
    /// <summary>
    /// 分页查询发送日志
    /// </summary>
    Task<(IEnumerable<SendLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        int? status = null,
        int? mailType = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null);

    /// <summary>
    /// 获取指定时间范围内的统计信息
    /// </summary>
    Task<SendLogStats> GetStatsAsync(DateTime? startDate = null, DateTime? endDate = null, int? mailboxId = null);

    /// <summary>
    /// 获取最近失败的发送记录
    /// </summary>
    Task<List<SendLog>> GetRecentFailuresAsync(int count = 10);

    /// <summary>
    /// 按邮箱统计发送情况
    /// </summary>
    /// <param name="startDate">开始时间（可选）</param>
    /// <param name="endDate">结束时间（可选）</param>
    /// <param name="mailboxId">邮箱 ID 筛选（可选，null 表示不筛选）</param>
    Task<List<MailboxSendStats>> GetStatsByMailboxAsync(DateTime? startDate = null, DateTime? endDate = null, int? mailboxId = null);

    /// <summary>
    /// 按日统计发送趋势
    /// </summary>
    Task<List<DailySendStats>> GetDailyStatsAsync(DateTime startDate, DateTime endDate, int? mailboxId = null);

    /// <summary>
    /// 通过 Message-ID 查找发送日志（用于退信处理）
    /// </summary>
    Task<SendLog?> FindByMessageIdAsync(string messageId);

    /// <summary>
    /// 通过收件人邮箱查找最近的发送记录（用于退信处理）
    /// </summary>
    Task<SendLog?> FindRecentByEmailAsync(string email, int? mailboxId = null);

    /// <summary>
    /// 获取待发送的队列记录
    /// </summary>
    Task<List<SendLog>> GetPendingAsync();
}

/// <summary>
/// 发送日志统计
/// </summary>
public class SendLogStats
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int PendingCount { get; set; }
    public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount * 100 : 0;
}

/// <summary>
/// 按邮箱的发送统计
/// </summary>
public class MailboxSendStats
{
    public int MailboxId { get; set; }
    public string MailboxName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount * 100 : 0;
}

/// <summary>
/// 按日的发送统计
/// </summary>
public class DailySendStats
{
    public DateTime Date { get; set; }
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}
