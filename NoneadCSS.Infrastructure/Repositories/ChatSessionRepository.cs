using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Infrastructure.Repositories;

public class ChatSessionRepository : IChatSessionRepository
{
    private readonly NoneadCSSDbContext _context;

    public ChatSessionRepository(NoneadCSSDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 应用标准的 Include 链，包含关联的邮箱、用户和客户信息。
    /// </summary>
    private IQueryable<ChatSession> ApplyIncludes(IQueryable<ChatSession> query)
    {
        return query
            .Include(s => s.Mailbox)
            .Include(s => s.User)
            .Include(s => s.Customer);
    }

    /// <summary>
    /// 应用完整的 Include 链，包含关联的邮箱、用户、客户和会话信息。
    /// </summary>
    private IQueryable<ChatSession> ApplyFullIncludes(IQueryable<ChatSession> query)
    {
        return ApplyIncludes(query)
            .Include(s => s.Conversation);
    }

    public async Task<ChatSession?> GetByIdAsync(int id)
    {
        return await ApplyFullIncludes(_context.ChatSessions.AsNoTracking())
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>
    /// 按 ID 查询会话（tracking，含完整 Includes），适用于后续修改 + SaveChanges 的场景。
    /// </summary>
    public async Task<ChatSession?> GetByIdTrackedAsync(int id)
    {
        return await ApplyFullIncludes(_context.ChatSessions)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<ChatSession?> GetByTokenAsync(string sessionToken)
    {
        return await ApplyIncludes(_context.ChatSessions.AsNoTracking())
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);
    }

    /// <summary>
    /// 按 ConversationId 查询聊天会话（工单详情页回查在线聊天评分，只读）
    /// </summary>
    public async Task<ChatSession?> GetByConversationIdAsync(int conversationId)
    {
        return await ApplyFullIncludes(_context.ChatSessions.AsNoTracking())
            .FirstOrDefaultAsync(s => s.ConversationId == conversationId);
    }

    public async Task<IReadOnlyList<ChatSession>> GetListAsync(
        string? status = null,
        int? mailboxId = null,
        int? userId = null,
        int? customerId = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        int pageSize = 20,
        int[]? mailboxIds = null)
    {
        var query = BuildQuery(status, mailboxId, mailboxIds, userId, customerId, keyword, startDate, endDate);
        var items = await query
            .OrderByDescending(s => s.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return items;
    }

    public async Task<int> CountAsync(
        string? status = null,
        int? mailboxId = null,
        int? userId = null,
        int? customerId = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int[]? mailboxIds = null)
    {
        var query = BuildQuery(status, mailboxId, mailboxIds, userId, customerId, keyword, startDate, endDate);
        return await query.CountAsync();
    }

    public async Task<IReadOnlyList<ChatSession>> GetAllAsync()
    {
        return await ApplyIncludes(_context.ChatSessions.AsNoTracking())
            .OrderByDescending(s => s.UpdatedAt)
            .Take(100)
            .ToListAsync();
    }

    public async Task AddAsync(ChatSession session)
    {
        await _context.ChatSessions.AddAsync(session);
    }

    [Obsolete("推荐改用 GetByIdTrackedAsync + 直接修改属性（避免 AsNoTracking + Update 反模式）。该方法后续版本将移除。")]
    public void Update(ChatSession session)
    {
        _context.ChatSessions.Update(session);
    }

    public void Remove(ChatSession session)
    {
        _context.ChatSessions.Remove(session);
    }

    public async Task<int> CountByStatusAsync(string status, int[]? mailboxIds = null)
    {
        var query = _context.ChatSessions.Where(s => s.Status == status);
        if (mailboxIds is { Length: > 0 })
        {
            query = query.Where(s => s.MailboxId.HasValue && mailboxIds.Contains(s.MailboxId.Value));
        }
        return await query.CountAsync();
    }

    public async Task<int> CountActiveAsync()
    {
        return await _context.ChatSessions.CountAsync(s => s.Status == "waiting" || s.Status == "active");
    }

    public async Task<int> SumUnreadCountAsync(int[]? mailboxIds = null)
    {
        var query = _context.ChatSessions.AsQueryable();
        if (mailboxIds is { Length: > 0 })
        {
            query = query.Where(s => s.MailboxId.HasValue && mailboxIds.Contains(s.MailboxId.Value));
        }
        return await query.SumAsync(s => s.AgentUnreadCount);
    }

    public async Task<List<ChatSession>> GetAwaitingRatingExpiredAsync(DateTime utcNow, int limit = 100)
    {
        return await _context.ChatSessions
            .AsTracking()
            .Where(s => s.Status == "awaiting_rating" &&
                        s.RatingTimeoutAt.HasValue &&
                        s.RatingTimeoutAt.Value <= utcNow)
            .OrderBy(s => s.RatingTimeoutAt!.Value)
            .Take(limit)
            .ToListAsync();
    }

    // ===================== 已评分会话聚合统计（供 SatisfactionRatingService 合并用） =====================

    /// <summary>
    /// 仅包含已评分会话的基础查询：Rating.HasValue 且应用时间/邮箱/用户过滤。
    /// 评分日期优先级：ClosedAt（评分完成后关闭）＞ UpdatedAt。
    /// </summary>
    private IQueryable<ChatSession> BuildRatedQuery(
        int? rating = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = _context.ChatSessions.AsNoTracking()
            .Where(s => s.Rating.HasValue);

        if (rating.HasValue)
            query = query.Where(s => s.Rating == rating.Value);

        if (mailboxId.HasValue)
            query = query.Where(s => s.MailboxId == mailboxId.Value);

        if (userId.HasValue)
            query = query.Where(s => s.UserId == userId.Value);

        // 时间过滤按 ClosedAt ?? UpdatedAt
        if (startDate.HasValue)
            query = query.Where(s => (s.ClosedAt ?? s.UpdatedAt) >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(s => (s.ClosedAt ?? s.UpdatedAt) < endExclusive);
        }

        return query;
    }

    public async Task<SatisfactionRatingStats> GetRatedStatsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? mailboxId = null,
        int? userId = null)
    {
        var query = BuildRatedQuery(null, mailboxId, userId, startDate, endDate);

        // 单次 GroupBy 查询后内存聚合，避免多次 CountAsync 触发多次数据库往返
        var groups = await query
            .GroupBy(s => s.Rating!.Value)
            .Select(g => new
            {
                Rating = g.Key,
                Count = g.Count(),
                ScoreSum = g.Sum(s => s.Rating!.Value)
            })
            .ToListAsync();

        int CountOf(int v) => groups.FirstOrDefault(g => g.Rating == v)?.Count ?? 0;

        var total = groups.Sum(g => g.Count);
        var totalScore = groups.Sum(g => g.ScoreSum);
        var excellent = CountOf(SatisfactionRatingValue.Excellent);
        var good = CountOf(SatisfactionRatingValue.Good);
        var average = CountOf(SatisfactionRatingValue.Average);
        var poor = CountOf(SatisfactionRatingValue.Poor);
        var bad = CountOf(SatisfactionRatingValue.Bad);

        return new SatisfactionRatingStats
        {
            TotalCount = total,
            ExcellentCount = excellent,
            GoodCount = good,
            AverageCount = average,
            PoorCount = poor,
            BadCount = bad,
            AverageScore = total > 0 ? (double)totalScore / total : 0,
            GoodRate = total > 0 ? (double)(excellent + good) / total * 100 : 0
        };
    }

    public async Task<List<Domain.Repositories.DailySatisfactionStats>> GetRatedDailyStatsAsync(
        DateTime startDate,
        DateTime endDate,
        int? mailboxId = null)
    {
        var query = BuildRatedQuery(null, mailboxId, null, startDate, endDate);

        // 使用 ClosedAt ?? UpdatedAt 作为评分日期
        var stats = await query
            .GroupBy(s => new { Date = (s.ClosedAt ?? s.UpdatedAt).Date })
            .Select(g => new Domain.Repositories.DailySatisfactionStats
            {
                Date = g.Key.Date,
                TotalCount = g.Count(),
                ExcellentCount = g.Count(s => s.Rating == SatisfactionRatingValue.Excellent),
                GoodCount = g.Count(s => s.Rating == SatisfactionRatingValue.Good),
                AverageCount = g.Count(s => s.Rating == SatisfactionRatingValue.Average),
                PoorCount = g.Count(s => s.Rating == SatisfactionRatingValue.Poor),
                BadCount = g.Count(s => s.Rating == SatisfactionRatingValue.Bad),
                AverageScore = (double)g.Sum(s => s.Rating!.Value) / g.Count()
            })
            .OrderBy(s => s.Date)
            .ToListAsync();

        return stats;
    }

    public async Task<List<Domain.Repositories.MailboxSatisfactionStats>> GetRatedByMailboxAsync(
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = BuildRatedQuery(null, null, null, startDate, endDate)
            .Where(s => s.MailboxId != null);

        var groupedStats = await query
            .GroupBy(s => s.MailboxId!.Value)
            .Select(g => new
            {
                MailboxId = g.Key,
                TotalCount = g.Count(),
                ExcellentCount = g.Count(s => s.Rating == SatisfactionRatingValue.Excellent),
                GoodCount = g.Count(s => s.Rating == SatisfactionRatingValue.Good),
                AverageCount = g.Count(s => s.Rating == SatisfactionRatingValue.Average),
                PoorCount = g.Count(s => s.Rating == SatisfactionRatingValue.Poor),
                BadCount = g.Count(s => s.Rating == SatisfactionRatingValue.Bad),
                TotalScore = g.Sum(s => s.Rating!.Value),
            })
            .OrderByDescending(g => g.TotalCount > 0 ? (double)g.TotalScore / g.TotalCount : 0)
            .ToListAsync();

        var mailboxIds = groupedStats.Select(g => g.MailboxId).Distinct().ToList();
        var mailboxes = await _context.Mailboxes
            .Where(m => mailboxIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m);

        return groupedStats.Select(g =>
        {
            var mailbox = mailboxes.TryGetValue(g.MailboxId, out var m) ? m : null;
            return new Domain.Repositories.MailboxSatisfactionStats
            {
                MailboxId = g.MailboxId,
                MailboxName = mailbox?.Name ?? "未知",
                Email = mailbox?.Email ?? "-",
                TotalCount = g.TotalCount,
                ExcellentCount = g.ExcellentCount,
                GoodCount = g.GoodCount,
                AverageCount = g.AverageCount,
                PoorCount = g.PoorCount,
                BadCount = g.BadCount,
                AverageScore = g.TotalCount > 0 ? (double)g.TotalScore / g.TotalCount : 0,
                GoodRate = g.TotalCount > 0 ? (double)(g.ExcellentCount + g.GoodCount) / g.TotalCount * 100 : 0
            };
        }).ToList();
    }

    public async Task<List<Domain.Repositories.UserSatisfactionStats>> GetRatedByUserAsync(
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = BuildRatedQuery(null, null, null, startDate, endDate)
            .Where(s => s.UserId != null);

        var groupedStats = await query
            .GroupBy(s => s.UserId!.Value)
            .Select(g => new
            {
                UserId = g.Key,
                TotalCount = g.Count(),
                ExcellentCount = g.Count(s => s.Rating == SatisfactionRatingValue.Excellent),
                GoodCount = g.Count(s => s.Rating == SatisfactionRatingValue.Good),
                AverageCount = g.Count(s => s.Rating == SatisfactionRatingValue.Average),
                PoorCount = g.Count(s => s.Rating == SatisfactionRatingValue.Poor),
                BadCount = g.Count(s => s.Rating == SatisfactionRatingValue.Bad),
                TotalScore = g.Sum(s => s.Rating!.Value),
            })
            .OrderByDescending(g => g.TotalCount > 0 ? (double)g.TotalScore / g.TotalCount : 0)
            .ToListAsync();

        var userIds = groupedStats.Select(g => g.UserId).Distinct().ToList();
        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        return groupedStats.Select(g =>
        {
            var user = users.TryGetValue(g.UserId, out var u) ? u : null;
            return new Domain.Repositories.UserSatisfactionStats
            {
                UserId = g.UserId,
                UserName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "未知",
                Email = user?.Email ?? "-",
                TotalCount = g.TotalCount,
                ExcellentCount = g.ExcellentCount,
                GoodCount = g.GoodCount,
                AverageCount = g.AverageCount,
                PoorCount = g.PoorCount,
                BadCount = g.BadCount,
                AverageScore = g.TotalCount > 0 ? (double)g.TotalScore / g.TotalCount : 0,
                GoodRate = g.TotalCount > 0 ? (double)(g.ExcellentCount + g.GoodCount) / g.TotalCount * 100 : 0
            };
        }).ToList();
    }

    public async Task<(IEnumerable<ChatSession> Items, int Total)> GetRatedPagedAsync(
        int page,
        int pageSize,
        int? rating = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null)
    {
        var query = BuildRatedQuery(rating, mailboxId, userId, startDate, endDate);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(s =>
                s.CustomerName.ToLower().Contains(kw) ||
                s.CustomerEmail.ToLower().Contains(kw) ||
                (s.RatingComment != null && s.RatingComment.ToLower().Contains(kw)) ||
                (s.Subject != null && s.Subject.ToLower().Contains(kw)));
        }

        var total = await query.CountAsync();

        var items = await ApplyFullIncludes(query)
            .OrderByDescending(s => s.ClosedAt ?? s.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<ChatSession>> GetRecentRatedAsync(int count = 10, int? rating = null)
    {
        var query = BuildRatedQuery(rating);

        return await ApplyFullIncludes(query)
            .OrderByDescending(s => s.ClosedAt ?? s.UpdatedAt)
            .Take(count)
            .ToListAsync();
    }

    private IQueryable<ChatSession> BuildQuery(
        string? status, int? mailboxId, int[]? mailboxIds, int? userId, int? customerId,
        string? keyword, DateTime? startDate, DateTime? endDate)
    {
        var query = ApplyIncludes(_context.ChatSessions.AsNoTracking()).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            // 支持用英文逗号分隔的多状态过滤(例如 "active,awaiting_rating"),
            // 便于前端把"进行中"和"等待评分"两个阶段视为同一个"活跃"bucket
            var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (statuses.Length == 1)
                query = query.Where(s => s.Status == statuses[0]);
            else
                query = query.Where(s => statuses.Contains(s.Status));
        }
        if (mailboxId.HasValue)
            query = query.Where(s => s.MailboxId == mailboxId.Value);
        else if (mailboxIds is { Length: > 0 })
            query = query.Where(s => s.MailboxId.HasValue && mailboxIds.Contains(s.MailboxId.Value));
        if (userId.HasValue)
            query = query.Where(s => s.UserId == userId.Value);
        if (customerId.HasValue)
            query = query.Where(s => s.CustomerId == customerId.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(s =>
                s.CustomerName.ToLower().Contains(kw) ||
                s.CustomerEmail.ToLower().Contains(kw) ||
                (s.Subject != null && s.Subject.ToLower().Contains(kw)));
        }
        if (startDate.HasValue)
            query = query.Where(s => s.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(s => s.CreatedAt < endExclusive);
        }

        return query;
    }

    // ===================== 软删除 / 垃圾箱 / 硬删除 =====================

    /// <summary>
    /// 垃圾箱列表分页查询（DeletedAt != null），支持关键词搜索 CustomerName/CustomerEmail/Subject。
    /// 使用 IgnoreQueryFilters 绕过全局软删除过滤器以访问已删除的记录。
    /// </summary>
    public async Task<IReadOnlyList<ChatSession>> GetDeletedListAsync(int page, int pageSize, string? keyword = null)
    {
        var query = ApplyIncludes(_context.ChatSessions.IgnoreQueryFilters().AsNoTracking())
            .Where(s => s.DeletedAt != null);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(s =>
                s.CustomerName.ToLower().Contains(kw) ||
                s.CustomerEmail.ToLower().Contains(kw) ||
                (s.Subject != null && s.Subject.ToLower().Contains(kw)));
        }

        return await query
            .OrderByDescending(s => s.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// 统计垃圾箱会话数量（DeletedAt != null）。
    /// </summary>
    public async Task<int> CountDeletedAsync(string? keyword = null)
    {
        var query = _context.ChatSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.DeletedAt != null);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(s =>
                s.CustomerName.ToLower().Contains(kw) ||
                s.CustomerEmail.ToLower().Contains(kw) ||
                (s.Subject != null && s.Subject.ToLower().Contains(kw)));
        }

        return await query.CountAsync();
    }

    /// <summary>
    /// 软删除单个会话：设置 DeletedAt = UtcNow。
    /// 使用 IgnoreQueryFilters 找到记录（含已删除的，实现幂等）。
    /// </summary>
    public async Task<bool> SoftDeleteAsync(int id)
    {
        var session = await _context.ChatSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (session == null)
            return false;

        if (session.DeletedAt != null)
            return true; // 已软删除，幂等返回 true

        session.DeletedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// 批量软删除：使用 ExecuteUpdateAsync 直接在数据库层更新，性能优于逐条加载。
    /// 仅更新未删除的记录（全局查询过滤器自动排除已删除记录）。
    /// </summary>
    public async Task<bool> SoftDeleteAsync(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return false;

        var affected = await _context.ChatSessions
            .Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.DeletedAt, DateTime.UtcNow));

        return affected > 0;
    }

    /// <summary>
    /// 恢复软删除的会话：设置 DeletedAt = null。
    /// 使用 IgnoreQueryFilters 找到已删除的记录。
    /// </summary>
    public async Task<bool> RestoreAsync(int id)
    {
        var session = await _context.ChatSessions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (session == null)
            return false;

        if (session.DeletedAt == null)
            return true; // 未被删除，幂等返回 true

        session.DeletedAt = null;
        return true;
    }

    /// <summary>
    /// 彻底删除单个会话：先删除关联的 ChatMessages，再物理删除 ChatSession 本身。
    /// 使用 IgnoreQueryFilters 以便能删除已软删除（在垃圾箱中）的会话。
    /// </summary>
    public async Task<bool> HardDeleteAsync(int id)
    {
        // 先删除关联消息（ChatMessage 无软删除过滤器，直接物理删除）
        await _context.ChatMessages
            .Where(m => m.SessionId == id)
            .ExecuteDeleteAsync();

        // 再物理删除会话本身（需 IgnoreQueryFilters 以匹配已软删除的记录）
        var affected = await _context.ChatSessions.IgnoreQueryFilters()
            .Where(s => s.Id == id)
            .ExecuteDeleteAsync();

        return affected > 0;
    }

    /// <summary>
    /// 清空垃圾箱：批量物理删除所有 DeletedAt != null 的会话及其关联消息。返回删除的会话数。
    /// </summary>
    public async Task<int> HardDeleteAllDeletedAsync()
    {
        // 收集所有已软删除的会话 ID
        var deletedIds = await _context.ChatSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.DeletedAt != null)
            .Select(s => s.Id)
            .ToListAsync();

        if (deletedIds.Count == 0)
            return 0;

        // 批量删除关联消息
        await _context.ChatMessages
            .Where(m => deletedIds.Contains(m.SessionId))
            .ExecuteDeleteAsync();

        // 批量物理删除会话
        var affected = await _context.ChatSessions.IgnoreQueryFilters()
            .Where(s => deletedIds.Contains(s.Id))
            .ExecuteDeleteAsync();

        return affected;
    }
}

public class ChatMessageRepository : IChatMessageRepository
{
    private readonly NoneadCSSDbContext _context;

    public ChatMessageRepository(NoneadCSSDbContext context)
    {
        _context = context;
    }

    public async Task<ChatMessage?> GetByIdAsync(int id)
    {
        return await _context.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    /// <summary>
    /// 返回被 ChangeTracker 跟踪的实体，修改属性后调用 SaveChangesAsync 即可持久化，
    /// 无需调用 Update()，避免全字段 UPDATE 与对象图附加问题。
    /// </summary>
    public async Task<ChatMessage?> GetByIdTrackedAsync(int id)
    {
        return await _context.ChatMessages
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetBySessionIdAsync(int sessionId, int limit = 100)
    {
        return await _context.ChatMessages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetListAsync(
        int? sessionId = null,
        string? senderType = null,
        bool? isRead = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        int pageSize = 50)
    {
        var query = BuildQuery(sessionId, senderType, isRead, startDate, endDate);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return items;
    }

    public async Task<int> CountAsync(int? sessionId = null, string? senderType = null, bool? isRead = null)
    {
        var query = BuildQuery(sessionId, senderType, isRead, null, null);
        return await query.CountAsync();
    }

    /// <summary>
    /// 按 SessionId 列表批量获取消息数（GROUP BY 聚合，避免 N+1 查询）
    /// </summary>
    public async Task<Dictionary<int, int>> GetCountBySessionIdsAsync(IEnumerable<int> sessionIds)
    {
        var idList = sessionIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        return await _context.ChatMessages.AsNoTracking()
            .Where(m => idList.Contains(m.SessionId))
            .GroupBy(m => m.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count);
    }

    public async Task<int> CountUnreadAsync(int sessionId, string senderType)
    {
        // senderType 表示接收方：customer 接收则查 agent 发送的未读，agent 接收则查 customer 发送的未读
        var oppositeSender = senderType == "customer" ? "agent" : "customer";
        return await _context.ChatMessages.AsNoTracking()
            .CountAsync(m => m.SessionId == sessionId && m.SenderType == oppositeSender && !m.IsRead);
    }

    public async Task AddAsync(ChatMessage message)
    {
        await _context.ChatMessages.AddAsync(message);
    }

    /// <summary>
    /// ⚠️ 反模式：推荐改用 GetByIdTrackedAsync 查询（tracking），直接修改属性后 SaveChangesAsync，
    /// 避免全字段 UPDATE 以及对象图附加导致的跟踪冲突。
    /// </summary>
    [Obsolete("推荐改用 GetByIdTrackedAsync + 直接修改属性")]
    public void Update(ChatMessage message)
    {
        _context.ChatMessages.Update(message);
    }

    public void Remove(ChatMessage message)
    {
        _context.ChatMessages.Remove(message);
    }

    public async Task MarkAsReadAsync(int sessionId, string senderType)
    {
        // 将对方发的消息全部标记为已读
        var oppositeSender = senderType == "customer" ? "agent" : "customer";
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ChatMessages SET IsRead = 1 WHERE SessionId = {sessionId} AND SenderType = {oppositeSender} AND IsRead = 0");
    }

    private IQueryable<ChatMessage> BuildQuery(
        int? sessionId, string? senderType, bool? isRead,
        DateTime? startDate, DateTime? endDate)
    {
        var query = _context.ChatMessages.AsNoTracking().AsQueryable();
        if (sessionId.HasValue)
            query = query.Where(m => m.SessionId == sessionId.Value);
        if (!string.IsNullOrWhiteSpace(senderType))
            query = query.Where(m => m.SenderType == senderType);
        if (isRead.HasValue)
            query = query.Where(m => m.IsRead == isRead.Value);
        if (startDate.HasValue)
            query = query.Where(m => m.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(m => m.CreatedAt < endExclusive);
        }
        return query;
    }
}
