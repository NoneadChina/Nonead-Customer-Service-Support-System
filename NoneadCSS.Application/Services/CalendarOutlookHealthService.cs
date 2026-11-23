using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Application.Services;

/// <summary>
/// P3-D: Outlook 同步健康仪表盘服务实现
/// 基于 OutlookSyncLog 审计表 + Mailbox.OutlookDeltaToken 聚合统计
/// （所有依赖均为接口，放在 Application 层符合分层架构）
/// </summary>
public class CalendarOutlookHealthService : ICalendarOutlookHealthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOutlookSyncLogRepository _syncLogRepository;
    private readonly IMailboxRepository _mailboxRepository;
    private readonly ILogger<CalendarOutlookHealthService> _logger;
    private readonly IUserRoleResolver _userRoleResolver;

    public CalendarOutlookHealthService(
        IUnitOfWork unitOfWork,
        IOutlookSyncLogRepository syncLogRepository,
        IMailboxRepository mailboxRepository,
        ILogger<CalendarOutlookHealthService> logger,
        IUserRoleResolver userRoleResolver)
    {
        _unitOfWork = unitOfWork;
        _syncLogRepository = syncLogRepository;
        _mailboxRepository = mailboxRepository;
        _logger = logger;
        _userRoleResolver = userRoleResolver;
    }

    /// <inheritdoc />
    public async Task<CalendarOutlookHealthDashboardDto?> GetMailboxHealthAsync(int mailboxId, int recentCount = 10, int historyDays = 7)
    {
        var mailbox = await _mailboxRepository.GetByIdAsync(mailboxId);
        if (mailbox == null) return null;

        var repo = _unitOfWork.Repository<OutlookSyncLog>();

        // NOTE: EF Core SQLite provider does NOT translate DateTimeOffset members
        // (direct comparison nor ToUnixTimeMilliseconds()) into SQL.
        // 因此在 SQL 端仅按主键倒序 Take 上限条数（Id 与 StartedAt 同向单调），
        // 时间窗口过滤放到内存端执行，避免无界加载整张日志表。
        const int maxLoad = 2000;
        const int windowDays = 30;
        var loadedLogs = await repo.GetQueryable(false)
            .AsNoTracking()
            .Where(x => x.MailboxId == mailboxId)
            .OrderByDescending(x => x.Id)
            .Take(maxLoad)
            .ToListAsync();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-windowDays);
        var allLogs = loadedLogs
            .Where(x => x.StartedAt >= cutoff)
            .ToList();

        var recent = allLogs
            .OrderByDescending(x => x.StartedAt)
            .Take(Math.Max(recentCount, 1))
            .ToList();

        var historyCutoff = DateTimeOffset.UtcNow.AddDays(-Math.Abs(historyDays));
        var last7Days = allLogs
            .Where(x => x.StartedAt >= historyCutoff)
            .Select(x => new { x.CreatedCount, x.UpdatedCount, x.DeletedCount, x.ErrorCount, x.Status, x.ErrorMessage })
            .ToList();

        var lastSuccess = recent.FirstOrDefault(x => string.Equals(x.Status, "success", StringComparison.OrdinalIgnoreCase));
        var lastAttempt = recent.FirstOrDefault();

        var consecutiveFailures = 0;
        foreach (var log in recent)
        {
            if (string.Equals(log.Status, "success", StringComparison.OrdinalIgnoreCase))
                break;
            consecutiveFailures++;
        }

        if (consecutiveFailures == 0 && lastSuccess == null)
        {
            var settingRepo = _unitOfWork.Repository<OutlookSyncSetting>();
            var muRepo = _unitOfWork.Repository<MailboxUser>();
            var userId = await muRepo.GetQueryable(false)
                .AsNoTracking()
                .Where(x => x.MailboxId == mailboxId)
                .Select(x => x.UserId)
                .FirstOrDefaultAsync();
            if (userId > 0)
            {
                var setting = await settingRepo.GetQueryable(false)
                    .AsNoTracking()
                    .Where(s => s.UserId == userId && s.DeletedAt == null)
                    .FirstOrDefaultAsync();
                if (setting != null)
                {
                    consecutiveFailures = setting.ConsecutiveFailures;
                }
            }
        }

        var lastFailedLog = recent.FirstOrDefault(x => !string.Equals(x.Status, "success", StringComparison.OrdinalIgnoreCase));
        var (lastErrorCode, lastErrorMessage) = ExtractErrorCodeAndMessage(lastFailedLog?.ErrorMessage);

        var recordsProcessed = last7Days.Sum(x => (long)x.CreatedCount + x.UpdatedCount + x.DeletedCount);

        var topErrors = last7Days
            .Where(x => !string.IsNullOrEmpty(x.ErrorMessage))
            .Select(x => ExtractErrorCodeAndMessage(x.ErrorMessage).ErrorCode ?? "UNKNOWN")
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => (ErrorCode: g.Key, Count: g.Count()))
            .ToList();

        var history = recent
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new CalendarOutlookSyncHistoryItem
            {
                Id = x.Id,
                StartedAt = x.StartedAt,
                FinishedAt = x.FinishedAt,
                Status = x.Status,
                CreatedCount = x.CreatedCount,
                UpdatedCount = x.UpdatedCount,
                DeletedCount = x.DeletedCount,
                ErrorCount = x.ErrorCount,
                DurationMs = (long)Math.Max(0, (x.FinishedAt - x.StartedAt).TotalMilliseconds),
                ErrorMessage = x.ErrorMessage
            })
            .ToList();

        return new CalendarOutlookHealthDashboardDto
        {
            MailboxId = mailboxId,
            Email = mailbox.Email,
            LastSuccessAt = lastSuccess?.FinishedAt ?? (lastSuccess != null ? lastSuccess.StartedAt : null),
            LastAttemptAt = lastAttempt?.StartedAt,
            ConsecutiveFailures = consecutiveFailures,
            LastErrorCode = lastErrorCode,
            LastErrorMessage = lastErrorMessage ?? lastFailedLog?.ErrorMessage,
            RecordsProcessedLast7Days = recordsProcessed,
            DeltaTokenValid = !string.IsNullOrEmpty(mailbox.OutlookDeltaToken),
            DeltaTokenUpdatedAt = mailbox.UpdatedAt,
            RecentHistory = history,
            TopErrorCodes = topErrors
        };
    }

    /// <inheritdoc />
    public async Task<List<CalendarOutlookHealthDashboardDto>> GetUserMailboxesHealthAsync(int userId, int recentCount = 10, int historyDays = 7)
    {
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(userId);
        var muRepo = _unitOfWork.Repository<MailboxUser>();

        IEnumerable<int> mailboxIds;
        if (isSuperAdmin)
        {
            var allMailboxes = await _mailboxRepository.GetAllAsync();
            mailboxIds = allMailboxes.Select(m => m.Id).ToList();
        }
        else
        {
            mailboxIds = await muRepo.GetQueryable(false)
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => x.MailboxId)
                .Distinct()
                .ToListAsync();
        }

        var result = new List<CalendarOutlookHealthDashboardDto>();
        foreach (var mid in mailboxIds)
        {
            var item = await GetMailboxHealthAsync(mid, recentCount, historyDays);
            if (item != null)
                result.Add(item);
        }
        return result;
    }

    /// <summary>
    /// 从错误消息中提取简短错误码和摘要
    /// </summary>
    private static (string? ErrorCode, string? Summary) ExtractErrorCodeAndMessage(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return (null, null);

        string code;
        var msg = errorMessage.Trim();

        if (msg.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            code = "AUTH_TOKEN_EXPIRED";
        }
        else if (msg.Contains("403", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("permission", StringComparison.OrdinalIgnoreCase))
        {
            code = "AUTH_FORBIDDEN";
        }
        else if (msg.Contains("404", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("NotFound", StringComparison.OrdinalIgnoreCase))
        {
            code = "NOT_FOUND";
        }
        else if (msg.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("throttle", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("rate", StringComparison.OrdinalIgnoreCase))
        {
            code = "RATE_LIMITED";
        }
        else if (msg.Contains("500", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("InternalServerError", StringComparison.OrdinalIgnoreCase))
        {
            code = "GRAPH_500";
        }
        else if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            code = "TIMEOUT";
        }
        else if (msg.Contains("Delta", StringComparison.OrdinalIgnoreCase) &&
                 (msg.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                  msg.Contains("invalid", StringComparison.OrdinalIgnoreCase)))
        {
            code = "DELTA_TOKEN_EXPIRED";
        }
        else if (msg.Contains("网络", StringComparison.Ordinal) ||
                 msg.Contains("Network", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("Socket", StringComparison.OrdinalIgnoreCase) ||
                 msg.Contains("DNS", StringComparison.OrdinalIgnoreCase))
        {
            code = "NETWORK_ERROR";
        }
        else
        {
            code = "UNKNOWN";
        }

        var summary = msg.Length > 200 ? msg.Substring(0, 200) + "..." : msg;
        return (code, summary);
    }
}
