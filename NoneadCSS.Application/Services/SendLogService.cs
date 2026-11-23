using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 邮件发送日志服务实现
/// </summary>
public class SendLogService : ISendLogService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILocalizationService? _localization;

    public SendLogService(IUnitOfWork unitOfWork, ICurrentUserAccessor currentUserAccessor, ILocalizationService? localization = null)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _localization = localization;
    }

    public async Task<PagedResponse<SendLogDto>> GetListAsync(
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
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var (items, total) = await _unitOfWork.SendLogs.GetPagedAsync(
            page, pageSize, status, mailType, mailboxId, userId, startDate, endDate, keyword);

        var dtos = items.Select(ToDto).ToList();

        return new PagedResponse<SendLogDto>
        {
            Data = dtos,
            CurrentPage = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    public async Task<SendLogDto?> GetByIdAsync(int id)
    {
        var entity = await _unitOfWork.SendLogs.GetByIdAsync(id);
        if (entity == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(entity.MailboxId))
            return null;

        return ToDto(entity);
    }

    public async Task<SendLogOverviewResponse> GetOverviewAsync(DateTime? startDate = null, DateTime? endDate = null, int? mailboxId = null)
    {
        // 默认最近 30 天
        if (!startDate.HasValue || !endDate.HasValue)
        {
            endDate ??= DateTime.UtcNow;
            startDate ??= endDate.Value.AddDays(-29);
        }

        var stats = await _unitOfWork.SendLogs.GetStatsAsync(startDate, endDate, mailboxId);
        var mailboxStats = await _unitOfWork.SendLogs.GetStatsByMailboxAsync(startDate, endDate, mailboxId);
        var dailyStats = await _unitOfWork.SendLogs.GetDailyStatsAsync(startDate.Value, endDate.Value, mailboxId);
        var recentFailures = await _unitOfWork.SendLogs.GetRecentFailuresAsync(10);

        return new SendLogOverviewResponse
        {
            Stats = new SendLogStatsDto
            {
                TotalCount = stats.TotalCount,
                SuccessCount = stats.SuccessCount,
                FailedCount = stats.FailedCount,
                PendingCount = stats.PendingCount,
                SuccessRate = Math.Round(stats.SuccessRate, 2),
            },
            MailboxStats = mailboxStats.Select(m => new MailboxSendStatsDto
            {
                MailboxId = m.MailboxId,
                MailboxName = m.MailboxName,
                Email = m.Email,
                TotalCount = m.TotalCount,
                SuccessCount = m.SuccessCount,
                FailedCount = m.FailedCount,
                SuccessRate = Math.Round(m.SuccessRate, 2),
            }).ToList(),
            DailyStats = dailyStats.Select(d => new DailySendStatsDto
            {
                Date = d.Date,
                TotalCount = d.TotalCount,
                SuccessCount = d.SuccessCount,
                FailedCount = d.FailedCount,
            }).ToList(),
            RecentFailures = recentFailures.Select(ToDto).ToList(),
        };
    }

    public async Task<List<SendLogDto>> GetRecentFailuresAsync(int count = 10)
    {
        var list = await _unitOfWork.SendLogs.GetRecentFailuresAsync(count);
        return list.Select(ToDto).ToList();
    }

    /// <summary>
    /// 语言中立的状态标签表：值为 zh-Hans 原文，仅作词条缺失时的回退源，
    /// 展示值一律按 <c>SendLogStatus_&lt;code&gt;</c> 词条取，避免把某一语言固化进目录。
    /// </summary>
    private static readonly Dictionary<int, string> NeutralStatusLabels = new()
    {
        [1] = "已接收",
        [2] = "发送错误",
        [4] = "投递成功",
        [5] = "投递失败",
        [6] = "已打开",
        [7] = "已点击",
        [8] = "已退订",
        [9] = "已投诉",
        [10] = "发送中断",
    };

    /// <summary>
    /// 语言中立的邮件类型标签表，同上（词条前缀 <c>SendLogMailType_</c>）。
    /// </summary>
    private static readonly Dictionary<int, string> NeutralMailTypeLabels = new()
    {
        [1] = "客户回复",
        [2] = "用户通知",
        [3] = "自动回复",
        [4] = "邀请",
        [5] = "密码修改",
        [6] = "错误邮件",
        [7] = "测试",
        [8] = "警报",
        [10] = "日程提醒",
    };

    private const string UnknownLabel = "未知";

    public Task<Dictionary<int, string>> GetStatusOptionsAsync()
        => Task.FromResult(LocalizeOptions(NeutralStatusLabels, "SendLogStatus_"));

    public Task<Dictionary<int, string>> GetMailTypeOptionsAsync()
        => Task.FromResult(LocalizeOptions(NeutralMailTypeLabels, "SendLogMailType_"));

    private Dictionary<int, string> LocalizeOptions(Dictionary<int, string> neutral, string keyPrefix)
    {
        var options = new Dictionary<int, string>(neutral.Count);
        foreach (var (code, zhFallback) in neutral)
        {
            options[code] = Translate($"{keyPrefix}{code}", zhFallback);
        }
        return options;
    }

    private SendLogDto ToDto(Domain.Entities.SendLog entity)
    {
        var statusCategory = GetStatusCategory(entity.Status);
        return new SendLogDto
        {
            Id = entity.Id,
            ThreadId = entity.ThreadId,
            ConversationId = entity.Thread?.ConversationId,
            ConversationSubject = entity.Thread?.Conversation?.Subject,
            MessageId = entity.MessageId,
            Email = entity.Email,
            MailType = entity.MailType,
            MailTypeName = GetMailTypeLabel(entity.MailType),
            Status = entity.Status,
            StatusName = GetStatusLabel(entity.Status),
            StatusCategory = statusCategory,
            CustomerId = entity.CustomerId,
            CustomerEmail = entity.Customer?.GetMainEmail(),
            UserId = entity.UserId,
            UserName = entity.User?.GetFullName(),
            CreatedAt = entity.CreatedAt,
        };
    }

    /// <summary>
    /// 状态显示名：走 Messages 资源（en / zh-Hans），未知码走 SendLogStatus_unknown。
    /// </summary>
    private string GetStatusLabel(int status) => NeutralStatusLabels.TryGetValue(status, out var zhFallback)
        ? Translate($"SendLogStatus_{status}", zhFallback)
        : Translate("SendLogStatus_unknown", UnknownLabel);

    /// <summary>
    /// 邮件类型显示名：走 Messages 资源（en / zh-Hans），未知码走 SendLogMailType_unknown。
    /// </summary>
    private string GetMailTypeLabel(int type) => NeutralMailTypeLabels.TryGetValue(type, out var zhFallback)
        ? Translate($"SendLogMailType_{type}", zhFallback)
        : Translate("SendLogMailType_unknown", UnknownLabel);

    /// <summary>
    /// 状态归类（success/failed/pending/warning/unknown）是机器可读值，不参与本地化。
    /// </summary>
    private static string GetStatusCategory(int status) => status switch
    {
        4 or 6 or 7 => "success",
        2 or 5 or 10 => "failed",
        1 => "pending",
        8 or 9 => "warning",
        _ => "unknown"
    };

    /// <summary>
    /// 取词条文本：本地化服务在词条缺失时会返回键名本身，此时回退给定中文原文，避免把键名吐给前端。
    /// </summary>
    private string Translate(string key, string zhFallback, params object[] args)
    {
        if (_localization is null) return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        var value = _localization[key];
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        }
        return args.Length > 0 ? string.Format(value, args) : value;
    }
}
