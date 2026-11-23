using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

public static class SatisfactionRatingConst
{
    public const int Excellent = 5;
    public const int Good = 4;
    public const int Average = 3;
    public const int Poor = 2;
    public const int Bad = 1;

    public static string GetLabel(int rating) => rating switch
    {
        Excellent => "远超期望",
        Good => "物有所值",
        Average => "中规中矩",
        Poor => "略有失望",
        Bad => "无法接受",
        _ => "未知"
    };

    public static string GetCategory(int rating) => rating switch
    {
        Excellent => "excellent",
        Good => "good",
        Average => "average",
        Poor => "poor",
        Bad => "bad",
        _ => "unknown"
    };
}

public class SatisfactionRatingService : ISatisfactionRatingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkflowService _workflowService;
    private readonly ILogger<SatisfactionRatingService> _logger;
    private readonly ISatisfactionTokenGenerator _tokenGenerator;
    private readonly INotificationHubService _notificationHub;

    public SatisfactionRatingService(
        IUnitOfWork unitOfWork,
        IWorkflowService workflowService,
        ILogger<SatisfactionRatingService> logger,
        ISatisfactionTokenGenerator tokenGenerator,
        INotificationHubService notificationHub)
    {
        _unitOfWork = unitOfWork;
        _workflowService = workflowService;
        _logger = logger;
        _tokenGenerator = tokenGenerator;
        _notificationHub = notificationHub;
    }

    /// <summary>
    /// 合并两个 SatisfactionRatingStats：字段相加，重算 AverageScore/GoodRate。
    /// </summary>
    private static SatisfactionRatingStats MergeStats(SatisfactionRatingStats a, SatisfactionRatingStats b)
    {
        var total = a.TotalCount + b.TotalCount;
        var sumScore = a.AverageScore * a.TotalCount + b.AverageScore * b.TotalCount;
        return new SatisfactionRatingStats
        {
            TotalCount = total,
            ExcellentCount = a.ExcellentCount + b.ExcellentCount,
            GoodCount = a.GoodCount + b.GoodCount,
            AverageCount = a.AverageCount + b.AverageCount,
            PoorCount = a.PoorCount + b.PoorCount,
            BadCount = a.BadCount + b.BadCount,
            AverageScore = total > 0 ? sumScore / total : 0,
            GoodRate = total > 0 ? (double)(a.ExcellentCount + a.GoodCount + b.ExcellentCount + b.GoodCount) / total * 100 : 0
        };
    }

    /// <summary>
    /// 按日期合并 DailySatisfactionStats（按日期分组聚合，字段相加后重算平均分）。
    /// </summary>
    private static List<DailySatisfactionStats> MergeDailyStats(List<DailySatisfactionStats> a, List<DailySatisfactionStats> b)
    {
        var merged = new Dictionary<DateTime, DailySatisfactionStats>();
        void AddRange(List<DailySatisfactionStats> list)
        {
            foreach (var d in list)
            {
                if (merged.TryGetValue(d.Date, out var cur))
                {
                    cur.TotalCount += d.TotalCount;
                    cur.ExcellentCount += d.ExcellentCount;
                    cur.GoodCount += d.GoodCount;
                    cur.AverageCount += d.AverageCount;
                    cur.PoorCount += d.PoorCount;
                    cur.BadCount += d.BadCount;
                }
                else
                {
                    merged[d.Date] = new DailySatisfactionStats
                    {
                        Date = d.Date,
                        TotalCount = d.TotalCount,
                        ExcellentCount = d.ExcellentCount,
                        GoodCount = d.GoodCount,
                        AverageCount = d.AverageCount,
                        PoorCount = d.PoorCount,
                        BadCount = d.BadCount
                    };
                }
            }
        }
        AddRange(a);
        AddRange(b);
        foreach (var kv in merged)
        {
            var item = kv.Value;
            int total = item.TotalCount;
            double sumScore =
                item.ExcellentCount * SatisfactionRatingConst.Excellent +
                item.GoodCount * SatisfactionRatingConst.Good +
                item.AverageCount * SatisfactionRatingConst.Average +
                item.PoorCount * SatisfactionRatingConst.Poor +
                item.BadCount * SatisfactionRatingConst.Bad;
            item.AverageScore = total > 0 ? sumScore / total : 0;
        }
        return merged.Values.OrderBy(s => s.Date).ToList();
    }

    /// <summary>
    /// 按 MailboxId 合并 MailboxSatisfactionStats（同 mailbox 聚合后重算平均分/好评率）。
    /// </summary>
    private static List<MailboxSatisfactionStats> MergeMailboxStats(List<MailboxSatisfactionStats> a, List<MailboxSatisfactionStats> b)
    {
        var merged = new Dictionary<int, MailboxSatisfactionStats>();
        void AddRange(List<MailboxSatisfactionStats> list)
        {
            foreach (var m in list)
            {
                if (merged.TryGetValue(m.MailboxId, out var cur))
                {
                    cur.TotalCount += m.TotalCount;
                    cur.ExcellentCount += m.ExcellentCount;
                    cur.GoodCount += m.GoodCount;
                    cur.AverageCount += m.AverageCount;
                    cur.PoorCount += m.PoorCount;
                    cur.BadCount += m.BadCount;
                }
                else
                {
                    merged[m.MailboxId] = new MailboxSatisfactionStats
                    {
                        MailboxId = m.MailboxId,
                        MailboxName = m.MailboxName,
                        Email = m.Email,
                        TotalCount = m.TotalCount,
                        ExcellentCount = m.ExcellentCount,
                        GoodCount = m.GoodCount,
                        AverageCount = m.AverageCount,
                        PoorCount = m.PoorCount,
                        BadCount = m.BadCount
                    };
                }
            }
        }
        AddRange(a);
        AddRange(b);
        foreach (var kv in merged)
        {
            var item = kv.Value;
            int total = item.TotalCount;
            double sumScore =
                item.ExcellentCount * SatisfactionRatingConst.Excellent +
                item.GoodCount * SatisfactionRatingConst.Good +
                item.AverageCount * SatisfactionRatingConst.Average +
                item.PoorCount * SatisfactionRatingConst.Poor +
                item.BadCount * SatisfactionRatingConst.Bad;
            item.AverageScore = total > 0 ? sumScore / total : 0;
            item.GoodRate = total > 0 ? (double)(item.ExcellentCount + item.GoodCount) / total * 100 : 0;
        }
        return merged.Values
            .OrderByDescending(s => s.TotalCount > 0 ? s.AverageScore : 0)
            .ToList();
    }

    /// <summary>
    /// 按 UserId 合并 UserSatisfactionStats。
    /// </summary>
    private static List<UserSatisfactionStats> MergeUserStats(List<UserSatisfactionStats> a, List<UserSatisfactionStats> b)
    {
        var merged = new Dictionary<int, UserSatisfactionStats>();
        void AddRange(List<UserSatisfactionStats> list)
        {
            foreach (var u in list)
            {
                if (merged.TryGetValue(u.UserId, out var cur))
                {
                    cur.TotalCount += u.TotalCount;
                    cur.ExcellentCount += u.ExcellentCount;
                    cur.GoodCount += u.GoodCount;
                    cur.AverageCount += u.AverageCount;
                    cur.PoorCount += u.PoorCount;
                    cur.BadCount += u.BadCount;
                }
                else
                {
                    merged[u.UserId] = new UserSatisfactionStats
                    {
                        UserId = u.UserId,
                        UserName = u.UserName,
                        Email = u.Email,
                        TotalCount = u.TotalCount,
                        ExcellentCount = u.ExcellentCount,
                        GoodCount = u.GoodCount,
                        AverageCount = u.AverageCount,
                        PoorCount = u.PoorCount,
                        BadCount = u.BadCount
                    };
                }
            }
        }
        AddRange(a);
        AddRange(b);
        foreach (var kv in merged)
        {
            var item = kv.Value;
            int total = item.TotalCount;
            double sumScore =
                item.ExcellentCount * SatisfactionRatingConst.Excellent +
                item.GoodCount * SatisfactionRatingConst.Good +
                item.AverageCount * SatisfactionRatingConst.Average +
                item.PoorCount * SatisfactionRatingConst.Poor +
                item.BadCount * SatisfactionRatingConst.Bad;
            item.AverageScore = total > 0 ? sumScore / total : 0;
            item.GoodRate = total > 0 ? (double)(item.ExcellentCount + item.GoodCount) / total * 100 : 0;
        }
        return merged.Values
            .OrderByDescending(s => s.TotalCount > 0 ? s.AverageScore : 0)
            .ToList();
    }

    /// <summary>
    /// 合并两类数据源进行分页展示：
    /// 1) SatisfactionRating（邮件/IM 渠道链接评价）
    /// 2) ChatSession 已评分会话（在线聊天渠道）
    /// 合并后按 CreatedAt/ClosedAt 倒序统一排序后再分页。
    /// 性能说明：两表分别按条件过滤后按时间排序再合并，用 Skip/Take 截取目标页，
    /// 适合当前客户量（单页 20，总体 < 10000），如后续数据量巨大可改为 UNION 视图/SQL。
    /// </summary>
    public async Task<(IEnumerable<SatisfactionRatingDto> Items, int Total)> GetListAsync(
        int page,
        int pageSize,
        int? rating = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null)
    {
        // 内存分页硬性上限保护（M-F1）：避免恶意或误传的大 pageSize 导致全表加载到内存
        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        // 并行拉取两类数据（不过 page/pageSize 过滤，先按相同条件全排序后客户端侧合并切页）
        var linkTask = _unitOfWork.SatisfactionRatings.GetPagedAsync(
            1, int.MaxValue, rating, mailboxId, userId, startDate, endDate, keyword);
        var chatTask = _unitOfWork.ChatSessions.GetRatedPagedAsync(
            1, int.MaxValue, rating, mailboxId, userId, startDate, endDate, keyword);
        await Task.WhenAll(linkTask, chatTask);

        var linkItems = (await linkTask).Items.Select(ToDto).Cast<SatisfactionRatingDto>();
        var chatItems = (await chatTask).Items.Select(ChatSessionToDto).Cast<SatisfactionRatingDto>();

        var all = linkItems.Concat(chatItems)
            .OrderByDescending(d => d.CreatedAt)
            .ToList();

        var total = all.Count;
        var paged = all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (paged, total);
    }

    public async Task<SatisfactionRatingDto?> GetByIdAsync(int id)
    {
        var entity = await _unitOfWork.SatisfactionRatings.GetByIdAsync(id);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<SatisfactionRatingDto?> GetByConversationAsync(int conversationId)
    {
        var entity = await _unitOfWork.SatisfactionRatings.GetByConversationAsync(conversationId);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<SatisfactionOverviewResponse> GetOverviewAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? mailboxId = null)
    {
        // 并行查询两类数据源：SatisfactionRating（链接评价）+ ChatSession（在线聊天评分）
        var linkStatsTask = _unitOfWork.SatisfactionRatings.GetStatsAsync(startDate, endDate, mailboxId);
        var chatStatsTask = _unitOfWork.ChatSessions.GetRatedStatsAsync(startDate, endDate, mailboxId);

        var effectiveStart = startDate ?? DateTime.UtcNow.AddDays(-30);
        var effectiveEnd = endDate ?? DateTime.UtcNow;
        var linkDailyTask = _unitOfWork.SatisfactionRatings.GetDailyStatsAsync(effectiveStart, effectiveEnd, mailboxId);
        var chatDailyTask = _unitOfWork.ChatSessions.GetRatedDailyStatsAsync(effectiveStart, effectiveEnd, mailboxId);

        var linkMailboxTask = _unitOfWork.SatisfactionRatings.GetStatsByMailboxAsync(startDate, endDate);
        var chatMailboxTask = _unitOfWork.ChatSessions.GetRatedByMailboxAsync(startDate, endDate);

        var linkUserTask = _unitOfWork.SatisfactionRatings.GetStatsByUserAsync(startDate, endDate);
        var chatUserTask = _unitOfWork.ChatSessions.GetRatedByUserAsync(startDate, endDate);

        var linkRecentTask = _unitOfWork.SatisfactionRatings.GetRecentAsync(10);
        var chatRecentTask = _unitOfWork.ChatSessions.GetRecentRatedAsync(10);

        await Task.WhenAll(
            linkStatsTask, chatStatsTask,
            linkDailyTask, chatDailyTask,
            linkMailboxTask, chatMailboxTask,
            linkUserTask, chatUserTask,
            linkRecentTask, chatRecentTask);

        var mergedStats = MergeStats(await linkStatsTask, await chatStatsTask);
        var mergedDaily = MergeDailyStats(await linkDailyTask, await chatDailyTask);
        var mergedMailbox = MergeMailboxStats(await linkMailboxTask, await chatMailboxTask);
        var mergedUser = MergeUserStats(await linkUserTask, await chatUserTask);

        // Recent ratings：两表各取 10 条合并后按时间倒序取前 10
        var recentAll = (await linkRecentTask).Select(ToDto)
            .Concat<SatisfactionRatingDto>((await chatRecentTask).Select(ChatSessionToDto))
            .OrderByDescending(d => d.CreatedAt)
            .Take(10)
            .ToList();

        return new SatisfactionOverviewResponse
        {
            Stats = new SatisfactionRatingStatsDto
            {
                TotalCount = mergedStats.TotalCount,
                ExcellentCount = mergedStats.ExcellentCount,
                GoodCount = mergedStats.GoodCount,
                AverageCount = mergedStats.AverageCount,
                PoorCount = mergedStats.PoorCount,
                BadCount = mergedStats.BadCount,
                AverageScore = Math.Round(mergedStats.AverageScore, 2),
                GoodRate = Math.Round(mergedStats.GoodRate, 1)
            },
            DailyStats = mergedDaily.Select(d => new DailySatisfactionStatsDto
            {
                Date = d.Date.ToString("yyyy-MM-dd"),
                TotalCount = d.TotalCount,
                ExcellentCount = d.ExcellentCount,
                GoodCount = d.GoodCount,
                AverageCount = d.AverageCount,
                PoorCount = d.PoorCount,
                BadCount = d.BadCount,
                AverageScore = Math.Round(d.AverageScore, 2)
            }).ToList(),
            MailboxStats = mergedMailbox.Select(m => new MailboxSatisfactionStatsDto
            {
                MailboxId = m.MailboxId,
                MailboxName = m.MailboxName,
                Email = m.Email,
                TotalCount = m.TotalCount,
                ExcellentCount = m.ExcellentCount,
                GoodCount = m.GoodCount,
                AverageCount = m.AverageCount,
                PoorCount = m.PoorCount,
                BadCount = m.BadCount,
                AverageScore = Math.Round(m.AverageScore, 2),
                GoodRate = Math.Round(m.GoodRate, 1)
            }).ToList(),
            UserStats = mergedUser.Select(u => new UserSatisfactionStatsDto
            {
                UserId = u.UserId,
                UserName = u.UserName,
                Email = u.Email,
                TotalCount = u.TotalCount,
                ExcellentCount = u.ExcellentCount,
                GoodCount = u.GoodCount,
                AverageCount = u.AverageCount,
                PoorCount = u.PoorCount,
                BadCount = u.BadCount,
                AverageScore = Math.Round(u.AverageScore, 2),
                GoodRate = Math.Round(u.GoodRate, 1)
            }).ToList(),
            RecentRatings = recentAll
        };
    }

    public async Task<List<SatisfactionRatingDto>> GetRecentAsync(int count = 10, int? rating = null)
    {
        var linkTask = _unitOfWork.SatisfactionRatings.GetRecentAsync(count, rating);
        var chatTask = _unitOfWork.ChatSessions.GetRecentRatedAsync(count, rating);
        await Task.WhenAll(linkTask, chatTask);

        return (await linkTask).Select(ToDto)
            .Concat<SatisfactionRatingDto>((await chatTask).Select(ChatSessionToDto))
            .OrderByDescending(d => d.CreatedAt)
            .Take(count)
            .ToList();
    }

    public async Task<SatisfactionRatingDto> CreateAsync(CreateSatisfactionRatingRequest request, string? ipAddress = null, string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw new ArgumentException("无效的评价链接");
        }

        // 对用户提交邮箱先做规范化（去控制字符/显示名解析/统一小写），
        // 避免 IM 渠道 enrich 写入的脏邮箱导致合法用户无法评价。
        // 规范化失败返回 null（视为未提供邮箱，不阻塞评价流程）
        string? normalizedRequestEmail = NormalizeEmail(request.CustomerEmail);

        var validationResult = await ValidateTokenAsync(request.Token);
        if (validationResult == null)
        {
            throw new ArgumentException("无效的评价链接");
        }

        if (validationResult.ConversationId != request.ConversationId)
        {
            throw new ArgumentException("评价链接与会话不匹配");
        }

        if (validationResult.AlreadyRated)
        {
            throw new InvalidOperationException("您已评价过本次服务");
        }

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(request.ConversationId);
        if (conversation == null || conversation.DeletedAt != null)
        {
            throw new KeyNotFoundException($"会话 {request.ConversationId} 不存在或已被删除");
        }

        var entity = new SatisfactionRating
        {
            ConversationId = request.ConversationId,
            CustomerId = conversation.CustomerId ?? 0,
            UserId = conversation.UserId,
            Rating = request.Rating,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            CustomerName = string.IsNullOrWhiteSpace(request.CustomerName) ? null : request.CustomerName.Trim(),
            CustomerEmail = normalizedRequestEmail,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        // 使用事务 + 数据库唯一索引（ConversationId）防范并发评价绕过 AlreadyRated 检查
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.SatisfactionRatings.AddAsync(entity);
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("您已评价过本次服务");
        }

        var created = await _unitOfWork.SatisfactionRatings.GetByIdAsync(entity.Id);
        // GetByIdAsync 理论上应返回刚插入的实体；若因缓存/竞态返回 null，回退使用本地 entity
        // 避免 null-forgiving 操作符掩盖真实问题并触发 NullReferenceException
        var dto = ToDto(created ?? entity);

        // 触发工作流：满意度评价提交
        try
        {
            await _workflowService.ExecuteForSatisfactionRatingAsync(entity.Id, WorkflowConst.TriggerSatisfactionSubmitted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发工作流失败（满意度评价提交）");
        }

        // 实时推送满意度评价更新通知给所有服务台在线客服
        try
        {
            await _notificationHub.SendToAll("SatisfactionUpdated", dto);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推送满意度评价实时通知失败（不影响主流程）");
        }

        return dto;
    }

    /// <summary>
    /// 判断 DbUpdateException 是否由唯一约束冲突引起（SQLite 和 SQL Server 兼容）。
    /// 参照 CustomerRepository 中的同名实现。
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var msg = (ex.InnerException?.Message ?? ex.Message)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(msg)) return false;
        // SQLite: "UNIQUE constraint failed"
        // SQL Server: "Cannot insert duplicate key row" / "Violation of UNIQUE KEY constraint"
        return msg.Contains("unique constraint")
               || msg.Contains("duplicate key")
               || msg.Contains("violation of unique");
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _unitOfWork.SatisfactionRatings.GetByIdAsync(id);
        if (entity == null) return false;

        await _unitOfWork.SatisfactionRatings.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<Dictionary<int, string>> GetRatingOptionsAsync()
    {
        await Task.CompletedTask;
        return new Dictionary<int, string>
        {
            { SatisfactionRatingConst.Excellent, SatisfactionRatingConst.GetLabel(SatisfactionRatingConst.Excellent) },
            { SatisfactionRatingConst.Good, SatisfactionRatingConst.GetLabel(SatisfactionRatingConst.Good) },
            { SatisfactionRatingConst.Average, SatisfactionRatingConst.GetLabel(SatisfactionRatingConst.Average) },
            { SatisfactionRatingConst.Poor, SatisfactionRatingConst.GetLabel(SatisfactionRatingConst.Poor) },
            { SatisfactionRatingConst.Bad, SatisfactionRatingConst.GetLabel(SatisfactionRatingConst.Bad) }
        };
    }

    public async Task<SatisfactionTokenValidationResult?> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split('-', 2);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var conversationId))
        {
            return null;
        }

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null || conversation.DeletedAt != null)
        {
            return null;
        }

        // 双路径验证：
        // - 邮件渠道：CustomerEmail 非空，用邮箱验证
        // - 聊天渠道（微信/飞书/WhatsApp/钉钉）：CustomerEmail 为空，回退到 Customer.ChannelId 验证
        string customerEmail = conversation.CustomerEmail ?? string.Empty;
        bool validated = false;

        if (!string.IsNullOrEmpty(customerEmail))
        {
            validated = _tokenGenerator.ValidateToken(token, conversationId, customerEmail);
        }
        else
        {
            // 聊天渠道回退：加载 Customer 获取 ChannelId 进行验证
            if (conversation.CustomerId.HasValue)
            {
                var customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
                if (customer != null && !string.IsNullOrWhiteSpace(customer.ChannelId))
                {
                    validated = _tokenGenerator.ValidateTokenByChannelId(token, conversationId, customer.ChannelId);
                    if (validated)
                    {
                        _logger.LogInformation("满意度评价令牌通过渠道 ID 验证：会话 {ConversationId}", conversationId);
                    }
                }
            }
        }

        if (!validated)
        {
            _logger.LogWarning("满意度评价令牌验证失败：会话 {ConversationId}，令牌已过期或无效", conversationId);
            return null;
        }

        bool alreadyRated = false;
        try
        {
            var existing = await _unitOfWork.SatisfactionRatings.GetByConversationAsync(conversationId);
            alreadyRated = existing != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查会话 {ConversationId} 是否已评价时出错，默认按未评价处理", conversationId);
        }

        return new SatisfactionTokenValidationResult
        {
            ConversationId = conversationId,
            // 返回前规范化邮箱：去控制字符/解析显示名/统一小写，
            // 确保前端接收到的是干净邮箱，避免页面展示和回传校验异常
            CustomerEmail = NormalizeEmail(customerEmail) ?? string.Empty,
            Subject = conversation.Subject,
            AlreadyRated = alreadyRated
        };
    }

    /// <summary>
    /// 规范化邮箱（逻辑与 CustomerService.ValidateAndNormalizeEmail 保持一致）。
    /// 重复实现说明（L-F2）：此处未复用 CustomerService 的方法，是为了避免 SatisfactionRatingService
    /// 对 CustomerService 产生额外依赖（评价服务仅做展示/统计，不需要完整客户校验链路）。
    /// 两处逻辑需保持同步，修改时请注意同步更新。
    /// 处理场景：
    ///   - 前后空白 / 全角空格 / BOM / 不可见控制字符
    ///   - 显示名格式："张三" &lt;zhangsan@example.com&gt; → zhangsan@example.com
    ///   - 统一转小写，消除域名字符欺骗风险
    /// 返回值：
    ///   - 有效邮箱 → 返回规范化后字符串（小写，无显示名）
    ///   - 空值/空白 → 返回 null（视为未提供邮箱，不阻塞业务流程）
    ///   - 解析失败（非 RFC 5322 兼容的脏数据）→ 返回 null，
    ///     对 IM 渠道评价来说 CustomerEmail 仅用于展示和统计，缺少不影响提交。
    /// </summary>
    private static string? NormalizeEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed.Length > 254)
            return null;

        var sanitized = new string(trimmed.Where(c => !char.IsControl(c)).ToArray());

        try
        {
            var addr = new System.Net.Mail.MailAddress(sanitized);
            return $"{addr.User}@{addr.Host}".ToLowerInvariant();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private SatisfactionRatingDto ToDto(SatisfactionRating entity)
    {
        return new SatisfactionRatingDto
        {
            Id = entity.Id,
            Source = "satisfaction_link",
            ChatSessionId = null,
            ConversationId = entity.ConversationId,
            ConversationSubject = entity.Conversation?.Subject,
            ThreadId = entity.ThreadId,
            CustomerId = entity.CustomerId,
            CustomerName = entity.CustomerName ?? entity.Customer?.GetFullName() ?? "未知客户",
            CustomerEmail = entity.CustomerEmail ?? entity.Customer?.GetMainEmail(),
            UserId = entity.UserId,
            UserName = entity.User != null ? $"{entity.User.FirstName} {entity.User.LastName}".Trim() : null,
            Rating = entity.Rating,
            RatingLabel = SatisfactionRatingConst.GetLabel(entity.Rating),
            RatingCategory = SatisfactionRatingConst.GetCategory(entity.Rating),
            Comment = entity.Comment,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    /// <summary>
    /// 将已评分的 ChatSession 映射为 SatisfactionRatingDto（统一展示格式），
    /// Source = "chat_session"，ChatSessionId 保留以区分来源。
    /// 注意：ChatSession.CustomerId 可为 null（未登录访客），此时赋值 -1 避免
    /// 打破 SatisfactionRatingDto.CustomerId (int) 的非空约束。
    /// </summary>
    private SatisfactionRatingDto ChatSessionToDto(ChatSession s)
    {
        var rating = s.Rating!.Value;
        // 映射评分日期：优先 ClosedAt（评分后关闭）否则 UpdatedAt
        var createdAt = s.ClosedAt ?? s.UpdatedAt;
        var customerName = string.IsNullOrWhiteSpace(s.CustomerName)
            ? (s.Customer != null ? s.Customer.GetFullName() : null)
            : s.CustomerName;
        var customerEmail = string.IsNullOrWhiteSpace(s.CustomerEmail)
            ? s.Customer?.GetMainEmail()
            : s.CustomerEmail;
        string? userName = null;
        if (s.User != null)
            userName = $"{s.User.FirstName} {s.User.LastName}".Trim();

        return new SatisfactionRatingDto
        {
            Id = -s.Id, // 负值避免与 SatisfactionRating.Id 冲突
            Source = "chat_session",
            ChatSessionId = s.Id,
            ConversationId = s.ConversationId ?? 0,
            ConversationSubject = s.Subject ?? s.Conversation?.Subject,
            ThreadId = null,
            CustomerId = s.CustomerId ?? -1,
            CustomerName = customerName ?? "在线访客",
            CustomerEmail = string.IsNullOrEmpty(customerEmail) ? null : customerEmail,
            UserId = s.UserId,
            UserName = userName,
            Rating = rating,
            RatingLabel = SatisfactionRatingConst.GetLabel(rating),
            RatingCategory = SatisfactionRatingConst.GetCategory(rating),
            Comment = s.RatingComment,
            CreatedAt = createdAt,
            UpdatedAt = s.UpdatedAt
        };
    }
}
