using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 报表服务实现 - 提供综合运营数据统计与分析
/// </summary>
public class ReportService : IReportService
{
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>SLA 首次响应目标时长（小时）</summary>
    private const double SlaFirstResponseTargetHours = 24.0;

    public ReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ReportResponse> GetReportAsync(DateTime startDate, DateTime endDate, int? mailboxId = null)
    {
        // 规范化日期（去除时间部分，endDate 当天 exclusive）
        var start = startDate.Date;
        var end = endDate.Date.AddDays(1);

        var summary = await BuildSummaryAsync(start, end, mailboxId);
        var trend = await BuildTrendAsync(start, end, mailboxId);
        var statusDistribution = await BuildStatusDistributionAsync(mailboxId);
        var userPerformance = await BuildUserPerformanceAsync(start, end, mailboxId);
        var mailboxLoad = await BuildMailboxLoadAsync(start, end);
        var customerDomains = await BuildCustomerDomainsAsync(start, end, mailboxId, 10);

        return new ReportResponse
        {
            StartDate = start.ToString("yyyy-MM-dd"),
            EndDate = end.AddDays(-1).ToString("yyyy-MM-dd"),
            MailboxId = mailboxId,
            Summary = summary,
            Trend = trend,
            StatusDistribution = statusDistribution,
            UserPerformance = userPerformance,
            MailboxLoad = mailboxLoad,
            CustomerDomains = customerDomains
        };
    }

    /// <summary>
    /// 分页获取客服绩效报表
    /// - 用户使用真正的 Skip/Take 分页，不会被截断
    /// - 首次响应时间计算使用 2 次 JOIN 查询代替 3 次往返，减少数据库压力（修复 Q4-5）
    /// </summary>
    public async Task<(List<ReportUserPerformanceDto> Items, int TotalCount)> GetUserPerformancePagedAsync(
        DateTime startDate, DateTime endDate, int? mailboxId, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), 100);

        var start = startDate.Date;
        var end = endDate.Date.AddDays(1);

        var userQuery = _unitOfWork.Users.GetQueryable()
            .Where(u => u.Status != UserStatus.Deleted);

        int totalCount = await userQuery.CountAsync();

        var users = await userQuery
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = await BuildUserPerformanceCoreAsync(start, end, mailboxId, users, userIds => true);
        return (items, totalCount);
    }

    private async Task<ReportSummaryDto> BuildSummaryAsync(DateTime start, DateTime end, int? mailboxId)
    {
        var newConversations = await _unitOfWork.Conversations.CountCreatedInDateRangeAsync(start, end, mailboxId);
        var closedConversations = await _unitOfWork.Conversations.CountClosedInDateRangeAsync(start, end, mailboxId);
        var totalReplies = await _unitOfWork.Conversations.CountThreadRepliesInDateRangeAsync(start, end);
        var newCustomers = await _unitOfWork.Customers.CountCreatedInDateRangeAsync(start, end);

        var (avgFirstResponseHours, slaRate) = await CalculateFirstResponseStatsAsync(start, end, mailboxId);
        var avgResolutionHours = await _unitOfWork.Conversations.AverageResolutionHoursAsync(start, end, mailboxId);

        return new ReportSummaryDto
        {
            NewConversations = newConversations,
            ClosedConversations = closedConversations,
            TotalReplies = totalReplies,
            NewCustomers = newCustomers,
            AverageFirstResponseHours = Math.Round(avgFirstResponseHours, 2),
            AverageResolutionHours = Math.Round(avgResolutionHours, 2),
            SlaAchievementRate = Math.Round(slaRate, 2)
        };
    }

    /// <summary>
    /// 计算平均首次响应时长和 SLA 达成率（数据库聚合版本，避免 OOM）
    /// 首次响应 = 会话的第一条客户消息（Type=Customer）到第一条客服回复（Type=Message）的时间差
    /// SLA 达成率 = 首次响应时长 ≤ 24小时 的会话占比
    /// </summary>
    private async Task<(double avgHours, double slaRate)> CalculateFirstResponseStatsAsync(DateTime start, DateTime end, int? mailboxId)
    {
        var convQuery = _unitOfWork.Conversations.GetQueryable()
            .AsNoTracking()
            .Where(c => c.CreatedAt >= start && c.CreatedAt < end && c.State != ConversationState.Deleted);

        if (mailboxId.HasValue)
        {
            convQuery = convQuery.Where(c => c.MailboxId == mailboxId.Value);
        }

        // TD-03：将会话过滤条件直接并入线程查询（数据库端 JOIN），
        // 避免将会话 ID 全量加载为 IN 参数列表导致 SQLite 999 参数上限异常。
        var threads = await _unitOfWork.Threads.GetQueryable()
            .AsNoTracking()
            .Join(
                convQuery,
                t => t.ConversationId,
                c => c.Id,
                (t, c) => new
                {
                    t.ConversationId,
                    t.Type,
                    t.CreatedAt
                })
            .ToListAsync();

        if (threads.Count == 0)
        {
            return (0, 0);
        }

        var responseTimes = new List<double>(threads.Count);
        foreach (var convGroup in threads.GroupBy(t => t.ConversationId))
        {
            var convThreads = convGroup.OrderBy(t => t.CreatedAt).ToList();
            var firstCustomer = convThreads.FirstOrDefault(t => t.Type == ThreadType.Customer);
            var firstReply = convThreads.FirstOrDefault(t => t.Type == ThreadType.Message);

            if (firstCustomer != null && firstReply != null)
            {
                var hours = (firstReply.CreatedAt - firstCustomer.CreatedAt).TotalHours;
                if (hours >= 0)
                {
                    responseTimes.Add(hours);
                }
            }
        }

        if (responseTimes.Count == 0)
        {
            return (0, 0);
        }

        var avg = responseTimes.Average();
        var slaCount = responseTimes.Count(h => h <= SlaFirstResponseTargetHours);
        var slaRate = (double)slaCount / responseTimes.Count * 100;
        return (avg, slaRate);
    }

    private async Task<List<ReportTrendPointDto>> BuildTrendAsync(DateTime start, DateTime end, int? mailboxId)
    {
        var trendDict = await _unitOfWork.Conversations.GetTrendAsync(start, end, mailboxId);

        // 按日聚合回复数（使用数据库聚合，避免加载全部线程实体）
        var replyQuery = _unitOfWork.Threads.GetQueryable()
            .AsNoTracking()
            .Where(t => t.Type == ThreadType.Message &&
                        t.CreatedAt >= start &&
                        t.CreatedAt < end);

        var replyByDate = await replyQuery
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count);

        var result = new List<ReportTrendPointDto>();
        for (var date = start.Date; date < end.Date; date = date.AddDays(1))
        {
            var newCount = trendDict.TryGetValue(date, out var v) ? v.NewCount : 0;
            var closedCount = trendDict.TryGetValue(date, out var v2) ? v2.ClosedCount : 0;
            var replyCount = replyByDate.TryGetValue(date, out var r) ? r : 0;
            result.Add(new ReportTrendPointDto
            {
                Date = date.ToString("yyyy-MM-dd"),
                NewCount = newCount,
                ClosedCount = closedCount,
                ReplyCount = replyCount
            });
        }
        return result;
    }

    private async Task<List<ReportStatusDistributionDto>> BuildStatusDistributionAsync(int? mailboxId)
    {
        var statusCounts = await _unitOfWork.Conversations.GetStatusDistributionAsync(mailboxId);
        var total = statusCounts.Values.Sum();

        var groups = statusCounts
            .Select(kv => new ReportStatusDistributionDto
            {
                Status = (int)kv.Key,
                StatusName = GetStatusName(kv.Key),
                Count = kv.Value,
                Percentage = total > 0 ? Math.Round((double)kv.Value / total * 100, 2) : 0
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        return groups;
    }

    /// <summary>
    /// 综合报表中嵌入的客服绩效（概览模式）：
    /// - 前 100 名按 Id 排序的客服；超过 100 时前端可切换调用分页端点 GetUserPerformancePagedAsync 查看完整列表
    /// - 邮箱负载 BuildMailboxLoadAsync 同样保持 100 上限（邮箱数量一般远小于客服数）
    /// </summary>
    private async Task<List<ReportUserPerformanceDto>> BuildUserPerformanceAsync(DateTime start, DateTime end, int? mailboxId)
    {
        const int overviewLimit = 100;

        var userQuery = _unitOfWork.Users.GetQueryable()
            .Where(u => u.Status != UserStatus.Deleted);

        var users = await userQuery
            .OrderBy(u => u.Id)
            .Take(overviewLimit)
            .ToListAsync();

        return await BuildUserPerformanceCoreAsync(start, end, mailboxId, users, userIds => true);
    }

    /// <summary>
    /// 客服绩效核心计算逻辑（共享给综合报表概览 & 分页端点）
    /// 包含 Q4-5 性能优化：首次响应时间由 3 次 DB 往返合并为 2 次 JOIN 查询
    /// </summary>
    /// <param name="userFilterForThreads">当分页计算时，限定 threads JOIN 仅扫描与当前分页用户相关的会话，避免加载全量线程</param>
    private async Task<List<ReportUserPerformanceDto>> BuildUserPerformanceCoreAsync(
        DateTime start,
        DateTime end,
        int? mailboxId,
        List<User> users,
        Func<IEnumerable<int>, bool> userFilterForThreads)
    {
        _ = userFilterForThreads; // 保留扩展点：未来大用户基数时启用 threads 侧用户过滤
        var userIds = users.Select(u => u.Id).ToList();
        var userPerf = await _unitOfWork.Conversations.GetUserPerformanceAsync(start, end, mailboxId);

        // ---- Q4-5 优化：3 次 DB 往返 → 2 次 JOIN ----
        // 原: (1) activeConvIds → (2) threads BY convId → (3) activeConvsWithUser {convId, userId}
        // 新: threads JOIN Conversation(UserId + mailboxId)，一次查出 convId/type/createdAt/userId
        // TD-03/H5 修复：JOIN 侧会话增加报表时间窗口过滤（会话在周期内创建），
        // 避免全量加载历史所有活跃会话的线程到内存（数据量随运行时间线性增长导致 OOM/超时）。
        var rightConvQuery = _unitOfWork.Conversations.GetQueryable()
            .AsNoTracking()
            .Where(c => c.Status == ConversationStatus.Active
                        && c.UserId.HasValue
                        && c.State != ConversationState.Deleted
                        && c.CreatedAt >= start
                        && c.CreatedAt < end);

        if (mailboxId.HasValue)
        {
            rightConvQuery = rightConvQuery.Where(c => c.MailboxId == mailboxId.Value);
        }

        var activeConvWithThreadsQuery = _unitOfWork.Threads.GetQueryable()
            .AsNoTracking()
            .Join(
                rightConvQuery,
                t => t.ConversationId,
                c => c.Id,
                (t, c) => new
                {
                    t.ConversationId,
                    t.Type,
                    t.CreatedAt,
                    UserId = c.UserId!.Value
                });

        // 只取当前 users 列表相关用户的 threads（若分页，这是最重要的优化裁剪点）
        var convThreadRows = await activeConvWithThreadsQuery
            .Where(r => userIds.Contains(r.UserId))
            .Select(r => new
            {
                r.ConversationId,
                r.Type,
                r.CreatedAt,
                r.UserId
            })
            .ToListAsync();

        var responseHoursByUser = new Dictionary<int, List<double>>();
        if (convThreadRows.Count > 0)
        {
            // 按 convId 分组并按时间排序（Q4-5：已通过 JOIN 拿到了 userId，无需再查 Conversations）
            var byConv = convThreadRows
                .GroupBy(r => r.ConversationId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.CreatedAt).ToList());

            foreach (var group in byConv)
            {
                var rows = group.Value;
                if (rows.Count == 0) continue;

                // 同一会话中用户的 userId 应该一致（JOIN Conversation 时已保证 UserId 有值）
                var userId = rows[0].UserId;

                var firstCustomer = rows.FirstOrDefault(r => r.Type == ThreadType.Customer);
                var firstReply = rows.FirstOrDefault(r => r.Type == ThreadType.Message);
                if (firstCustomer != null && firstReply != null)
                {
                    var hours = (firstReply.CreatedAt - firstCustomer.CreatedAt).TotalHours;
                    if (hours >= 0)
                    {
                        if (!responseHoursByUser.ContainsKey(userId))
                        {
                            responseHoursByUser[userId] = new List<double>();
                        }
                        responseHoursByUser[userId].Add(hours);
                    }
                }
            }
        }
        // ---- Q4-5 优化结束 ----

        var result = new List<ReportUserPerformanceDto>();
        foreach (var user in users)
        {
            var perf = userPerf.TryGetValue(user.Id, out var p) ? p : (0, 0, 0, 0.0);
            var avgHours = responseHoursByUser.TryGetValue(user.Id, out var hours) && hours.Count > 0
                ? Math.Round(hours.Average(), 2)
                : 0;

            result.Add(new ReportUserPerformanceDto
            {
                UserId = user.Id,
                UserName = user.GetFullName(),
                Email = user.Email,
                AssignedCount = perf.Item1,
                ReplyCount = perf.Item2,
                ClosedCount = perf.Item3,
                AverageFirstResponseHours = avgHours
            });
        }

        return result.OrderByDescending(x => x.ReplyCount).ThenByDescending(x => x.ClosedCount).ToList();
    }

    private async Task<List<ReportMailboxLoadDto>> BuildMailboxLoadAsync(DateTime start, DateTime end)
    {
        // 邮箱数量通常远小于客服数（一般 < 20），无需分页截断；
        // 若部署超过 50 个邮箱，可参考 UserPerformance 分页模式补充独立端点
        var mailboxes = await _unitOfWork.Mailboxes.GetQueryable()
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.Id)
            .ToListAsync();
        var mailboxLoad = await _unitOfWork.Conversations.GetMailboxLoadAsync(start, end);

        var totalNew = mailboxLoad.Values.Sum(x => x.Item1);
        if (totalNew == 0) totalNew = 1;

        var result = new List<ReportMailboxLoadDto>();
        foreach (var mb in mailboxes)
        {
            var load = mailboxLoad.TryGetValue(mb.Id, out var l) ? l : (0, 0, 0);
            result.Add(new ReportMailboxLoadDto
            {
                MailboxId = mb.Id,
                MailboxName = mb.Name,
                Email = mb.Email,
                NewCount = load.Item1,
                ClosedCount = load.Item2,
                ActiveCount = load.Item3,
                Percentage = Math.Round((double)load.Item1 / totalNew * 100, 2)
            });
        }
        return result.OrderByDescending(x => x.NewCount).ToList();
    }

    private async Task<List<ReportCustomerDomainDto>> BuildCustomerDomainsAsync(DateTime start, DateTime end, int? mailboxId, int top)
    {
        // 使用数据库投影，仅加载邮箱和客户 ID 字段，避免加载完整 Conversation 实体
        var convQuery = _unitOfWork.Conversations.GetQueryable()
            .AsNoTracking()
            .Where(c => c.CreatedAt >= start &&
                        c.CreatedAt < end &&
                        c.State != ConversationState.Deleted &&
                        c.CustomerEmail != null &&
                        c.CustomerEmail.Contains("@"));

        if (mailboxId.HasValue)
        {
            convQuery = convQuery.Where(c => c.MailboxId == mailboxId.Value);
        }

        var convData = await convQuery
            .Select(c => new
            {
                c.CustomerEmail,
                c.CustomerId
            })
            .ToListAsync();

        var domainGroups = convData
            .GroupBy(c => ExtractDomain(c.CustomerEmail!))
            .Select(g => new ReportCustomerDomainDto
            {
                Domain = g.Key,
                ConversationCount = g.Count(),
                UniqueCustomerCount = g.Select(x => x.CustomerId ?? 0).Distinct().Count(id => id != 0)
            })
            .OrderByDescending(x => x.ConversationCount)
            .Take(top)
            .ToList();

        var totalConv = domainGroups.Sum(x => x.ConversationCount);
        if (totalConv == 0) totalConv = 1;
        foreach (var d in domainGroups)
        {
            d.Percentage = Math.Round((double)d.ConversationCount / totalConv * 100, 2);
        }
        return domainGroups;
    }

    private static string ExtractDomain(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return "(未知)";
        var domain = email.Substring(at + 1).ToLowerInvariant();
        return string.IsNullOrEmpty(domain) ? "(未知)" : domain;
    }

    private static string GetStatusName(ConversationStatus status) => status switch
    {
        ConversationStatus.Active => "活跃",
        ConversationStatus.Pending => "待处理",
        ConversationStatus.Closed => "已关闭",
        ConversationStatus.Spam => "垃圾邮件",
        _ => status.ToString()
    };
}
