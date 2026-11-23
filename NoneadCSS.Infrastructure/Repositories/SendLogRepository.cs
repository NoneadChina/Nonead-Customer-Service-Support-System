using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

/// <summary>
/// 发送状态常量
/// </summary>
public static class SendLogStatus
{
    public const int Accepted = 1;              // 已接收（待发送）
    public const int SendError = 2;             // 发送错误
    public const int DeliverySuccess = 4;       // 投递成功
    public const int DeliveryError = 5;         // 投递失败
    public const int Opened = 6;                // 已打开
    public const int Clicked = 7;               // 已点击
    public const int Unsubscribed = 8;          // 已退订
    public const int Complained = 9;            // 已投诉
    public const int SendIntermediateError = 10; // 发送中间错误

    /// <summary>
    /// BUG-07 修复：邮件发送失败后的最大重试次数。
    /// 超过此次数后状态固化为 SendError，不再被队列拾起，避免无限重试占用资源。
    /// </summary>
    public const int MaxRetryCount = 3;

    public static bool IsSuccess(int status) => status == DeliverySuccess || status == Opened || status == Clicked;
    public static bool IsFailed(int status) => status == SendError || status == DeliveryError || status == SendIntermediateError;
    public static bool IsPending(int status) => status == Accepted;

    public static string GetLabel(int status) => status switch
    {
        Accepted => "已接收",
        SendError => "发送错误",
        DeliverySuccess => "投递成功",
        DeliveryError => "投递失败",
        Opened => "已打开",
        Clicked => "已点击",
        Unsubscribed => "已退订",
        Complained => "已投诉",
        SendIntermediateError => "发送中断",
        _ => "未知"
    };
}

/// <summary>
/// 邮件类型常量
/// </summary>
public static class SendLogMailType
{
    public const int EmailToCustomer = 1;        // 发给客户
    public const int UserNotification = 2;       // 用户通知
    public const int AutoReply = 3;              // 自动回复
    public const int Invite = 4;                 // 邀请
    public const int PasswordChanged = 5;        // 密码修改
    public const int WrongUserEmailMessage = 6;  // 错误邮件
    public const int Test = 7;                   // 测试
    public const int Alert = 8;                  // 警报
    public const int CalendarReminder = 10;      // 日程提醒

    public static string GetLabel(int type) => type switch
    {
        EmailToCustomer => "客户回复",
        UserNotification => "用户通知",
        AutoReply => "自动回复",
        Invite => "邀请",
        PasswordChanged => "密码修改",
        WrongUserEmailMessage => "错误邮件",
        Test => "测试",
        Alert => "警报",
        CalendarReminder => "日程提醒",
        _ => "未知"
    };
}

public class SendLogRepository : Repository<SendLog>, ISendLogRepository
{
    public SendLogRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    public async Task<(IEnumerable<SendLog> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        int? status = null,
        int? mailType = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null)
    {
        var query = _dbSet.AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        if (mailType.HasValue)
        {
            query = query.Where(s => s.MailType == mailType.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(s => s.UserId == userId.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(s => s.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            // 结束日期包含当天
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(s => s.CreatedAt < endExclusive);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(s => s.Email.Contains(kw) || s.MessageId.Contains(kw));
        }

        // 邮箱筛选需要通过 Thread -> Conversation 关联
        if (mailboxId.HasValue)
        {
            query = query
                .Include(s => s.Thread!)
                .ThenInclude(t => t.Conversation)
                .Where(s => s.Thread != null && s.Thread.Conversation.MailboxId == mailboxId.Value);
        }

        var total = await query.CountAsync();

        var items = await query
            .Include(s => s.Thread!)
                .ThenInclude(t => t.Conversation)
            .Include(s => s.User)
            .Include(s => s.Customer)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<SendLogStats> GetStatsAsync(DateTime? startDate = null, DateTime? endDate = null, int? mailboxId = null)
    {
        var query = _dbSet.AsQueryable();

        if (startDate.HasValue)
        {
            query = query.Where(s => s.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(s => s.CreatedAt < endExclusive);
        }

        if (mailboxId.HasValue)
        {
            query = query
                .Include(s => s.Thread!)
                .ThenInclude(t => t.Conversation)
                .Where(s => s.Thread != null && s.Thread.Conversation.MailboxId == mailboxId.Value);
        }

        var total = await query.CountAsync();
        var success = await query.CountAsync(s => s.Status == SendLogStatus.DeliverySuccess || s.Status == SendLogStatus.Opened || s.Status == SendLogStatus.Clicked);
        var failed = await query.CountAsync(s => s.Status == SendLogStatus.SendError || s.Status == SendLogStatus.DeliveryError || s.Status == SendLogStatus.SendIntermediateError);
        var pending = await query.CountAsync(s => s.Status == SendLogStatus.Accepted);

        return new SendLogStats
        {
            TotalCount = total,
            SuccessCount = success,
            FailedCount = failed,
            PendingCount = pending,
        };
    }

    public async Task<List<SendLog>> GetRecentFailuresAsync(int count = 10)
    {
        // 失败状态：SendError(2), DeliveryError(5), SendIntermediateError(10)
        var failedStatuses = new[] { SendLogStatus.SendError, SendLogStatus.DeliveryError, SendLogStatus.SendIntermediateError };
        return await _dbSet
            .Where(s => failedStatuses.Contains(s.Status))
            .Include(s => s.Thread!)
                .ThenInclude(t => t.Conversation)
            .Include(s => s.User)
            .OrderByDescending(s => s.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<MailboxSendStats>> GetStatsByMailboxAsync(DateTime? startDate = null, DateTime? endDate = null, int? mailboxId = null)
    {
        // 优先使用 SendLog.MailboxId 字段进行分组（覆盖系统邮件、密码重置等无会话场景）；
        // 仅在该字段为 null 时，才回退到 Thread -> Conversation -> MailboxId 关联查询
        var query = from s in _dbSet
                    join t in _context.Threads on s.ThreadId equals t.Id into tj
                    from t in tj.DefaultIfEmpty()
                    join c in _context.Conversations on (t != null ? t.ConversationId : 0) equals c.Id into cj
                    from c in cj.DefaultIfEmpty()
                    select new
                    {
                        SendLog = s,
                        MailboxId = s.MailboxId ?? (c != null ? c.MailboxId : 0)
                    };

        if (startDate.HasValue)
        {
            query = query.Where(x => x.SendLog.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(x => x.SendLog.CreatedAt < endExclusive);
        }

        if (mailboxId.HasValue)
        {
            query = query.Where(x => x.MailboxId == mailboxId.Value);
        }

        var groupedStats = await query
            .GroupBy(x => x.MailboxId)
            .Select(g => new
            {
                MailboxId = g.Key,
                TotalCount = g.Count(),
                SuccessCount = g.Count(x => x.SendLog.Status == SendLogStatus.DeliverySuccess || x.SendLog.Status == SendLogStatus.Opened || x.SendLog.Status == SendLogStatus.Clicked),
                FailedCount = g.Count(x => x.SendLog.Status == SendLogStatus.SendError || x.SendLog.Status == SendLogStatus.DeliveryError || x.SendLog.Status == SendLogStatus.SendIntermediateError),
            })
            .OrderByDescending(g => g.TotalCount)
            .ToListAsync();

        var mailboxIds = groupedStats.Where(g => g.MailboxId != 0).Select(g => g.MailboxId).Distinct().ToList();
        // 使用 IgnoreQueryFilters 以便显示已软删除邮箱的真实名称（统计历史数据）
        var mailboxes = await _context.Mailboxes
            .IgnoreQueryFilters()
            .Where(m => mailboxIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m);

        var stats = groupedStats.Select(g =>
        {
            var mailbox = g.MailboxId != 0 && mailboxes.TryGetValue(g.MailboxId, out var m) ? m : null;
            return new MailboxSendStats
            {
                MailboxId = g.MailboxId,
                MailboxName = mailbox?.Name ?? "无关联邮箱",
                Email = mailbox?.Email ?? "-",
                TotalCount = g.TotalCount,
                SuccessCount = g.SuccessCount,
                FailedCount = g.FailedCount,
            };
        }).ToList();

        return stats;
    }

    public async Task<List<DailySendStats>> GetDailyStatsAsync(DateTime startDate, DateTime endDate, int? mailboxId = null)
    {
        var query = _dbSet.AsQueryable();

        query = query.Where(s => s.CreatedAt >= startDate && s.CreatedAt < endDate.AddDays(1));

        if (mailboxId.HasValue)
        {
            query = query
                .Include(s => s.Thread!)
                .ThenInclude(t => t.Conversation)
                .Where(s => s.Thread != null && s.Thread.Conversation.MailboxId == mailboxId.Value);
        }

        var stats = await query
            .GroupBy(s => new { s.CreatedAt.Date })
            .Select(g => new DailySendStats
            {
                Date = g.Key.Date,
                TotalCount = g.Count(),
                SuccessCount = g.Count(s => s.Status == SendLogStatus.DeliverySuccess || s.Status == SendLogStatus.Opened || s.Status == SendLogStatus.Clicked),
                FailedCount = g.Count(s => s.Status == SendLogStatus.SendError || s.Status == SendLogStatus.DeliveryError || s.Status == SendLogStatus.SendIntermediateError),
            })
            .OrderBy(s => s.Date)
            .ToListAsync();

        return stats;
    }

    public async Task<SendLog?> FindByMessageIdAsync(string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return null;
        }

        return await _dbSet
            .Include(s => s.Thread!)
                .ThenInclude(t => t.Conversation)
            .FirstOrDefaultAsync(s => s.MessageId == messageId);
    }

    public async Task<SendLog?> FindRecentByEmailAsync(string email, int? mailboxId = null)
    {
        if (string.IsNullOrEmpty(email))
        {
            return null;
        }

        IQueryable<SendLog> query = _dbSet.Where(s => s.Email.Equals(email, StringComparison.OrdinalIgnoreCase));

        if (mailboxId.HasValue)
        {
            query = query.Where(s => s.MailboxId == mailboxId.Value);
        }

        return await query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
    }

    public async Task<List<SendLog>> GetPendingAsync()
    {
        // BUG-07 修复：
        // 1. 不仅拾起 Accepted(1)，还要拾起 SendIntermediateError(10) —— 防止进程崩溃时
        //    事务回滚前已被标记为中间错误状态的邮件被永远遗弃（孤立中间状态）。
        // 2. 限制 AttemptCount < MaxRetryCount，超过最大重试次数的邮件不再被队列拾起，
        //    状态会固化为 SendError(2)（由 ProcessQueueItemAsync 在失败时判定并写入），
        //    避免无限重试占用资源。
        var pendingStatuses = new[] { SendLogStatus.Accepted, SendLogStatus.SendIntermediateError };
        return await _dbSet
            .Where(s => pendingStatuses.Contains(s.Status)
                && s.AttemptCount < SendLogStatus.MaxRetryCount)
            .OrderBy(s => s.CreatedAt)
            .Take(100)
            .ToListAsync();
    }
}
