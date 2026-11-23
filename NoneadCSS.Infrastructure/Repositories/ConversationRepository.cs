using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class ConversationRepository : Repository<Conversation>, IConversationRepository
{
    public ConversationRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 应用标准的 Includes（用于列表查询）
    /// </summary>
    private IQueryable<Conversation> ApplyStandardIncludes(IQueryable<Conversation> query)
    {
        return query
            .Include(c => c.Customer!)
                .ThenInclude(cu => cu.Emails)
            .Include(c => c.User)
            .Include(c => c.ConversationTags!)
                .ThenInclude(ct => ct.Tag);
    }

    /// <summary>
    /// 应用完整的 Includes（用于详情查询，包含 Threads 和 Followers，不包含草稿）
    /// 使用 AsSingleQuery 避免在 SQLite 上触发 Error 5（active statements）。
    /// 注意：Include 内不可使用 OrderBy/Take，否则 EF Core 翻译为 SQL APPLY 操作，SQLite 不支持。
    /// </summary>
    private IQueryable<Conversation> ApplyFullIncludes(IQueryable<Conversation> query)
    {
        return ApplyStandardIncludes(query)
            .Include(c => c.Followers)
            .Include(c => c.Threads!.Where(t => t.State != Domain.Entities.ThreadState.Draft && t.State != Domain.Entities.ThreadState.Deleted))
                .ThenInclude(t => t.Attachments)
            .Include(c => c.Threads!.Where(t => t.State != Domain.Entities.ThreadState.Draft && t.State != Domain.Entities.ThreadState.Deleted))
                .ThenInclude(t => t.User)
            .AsSingleQuery();
    }

    public override async Task<Conversation?> GetByIdAsync(int id)
    {
        // 过滤 State == Deleted（垃圾箱）的会话，与 GetByMailboxIdAsync 等方法保持一致
        // 已删除会话的查询/恢复应通过专门的 IgnoreQueryFilters 方法
        return await ApplyFullIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(c => c.Id == id && c.State != ConversationState.Deleted);
    }

    /// <summary>
    /// 按 ID 查询会话（tracking，含完整 Includes），适用于后续修改 + SaveChanges 的场景。
    /// 与 GetByIdAsync 保持一致的 Includes（Customer、User、ConversationTags、Threads、Followers），
    /// 但不使用 AsNoTracking，返回的实体被跟踪，修改属性后只需 SaveChangesAsync 即可持久化。
    /// </summary>
    public override async Task<Conversation?> GetByIdTrackedAsync(int id)
    {
        return await ApplyFullIncludes(_dbSet)
            .FirstOrDefaultAsync(c => c.Id == id && c.State != ConversationState.Deleted);
    }

    public async Task<Conversation?> GetByIdForUpdateAsync(int id)
    {
        return await _dbSet.FirstOrDefaultAsync(c => c.Id == id && c.State != ConversationState.Deleted);
    }

    public async Task<List<Conversation>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<Conversation>();
        }

        return await ApplyFullIncludes(_dbSet.AsNoTracking())
            .Where(c => idList.Contains(c.Id) && c.State != ConversationState.Deleted)
            .ToListAsync();
    }

    public async Task<List<Conversation>> GetByIdsForUpdateAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<Conversation>();
        }

        return await _dbSet
            .Where(c => idList.Contains(c.Id) && c.State != ConversationState.Deleted)
            .ToListAsync();
    }

    public async Task<List<Conversation>> GetByIdsWithFoldersForUpdateAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<Conversation>();
        }

        return await _dbSet
            .Include(c => c.ConversationFolders)
            .Where(c => idList.Contains(c.Id) && c.State != ConversationState.Deleted)
            .ToListAsync();
    }

    public async Task<IEnumerable<Conversation>> GetByMailboxIdAsync(int mailboxId, int page, int pageSize)
    {
        return await ApplyStandardIncludes(_dbSet.AsNoTracking())
            .Where(c => c.MailboxId == mailboxId && c.State != ConversationState.Deleted)
            .OrderByDescending(c => c.LastReplyAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<IEnumerable<Conversation>> GetByFolderIdAsync(int folderId, int page, int pageSize)
    {
        return await ApplyStandardIncludes(_dbSet.AsNoTracking())
            .Where(c => c.FolderId == folderId && c.State != ConversationState.Deleted)
            .OrderByDescending(c => c.LastReplyAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    // M-7：返回 (Items, Total) 元组，先在未 Include 的查询上计数避免性能下降
    public async Task<(IEnumerable<Conversation> Items, int Total)> GetByUserIdAsync(int userId, int page, int pageSize)
    {
        var baseQuery = _dbSet.AsNoTracking()
            .Where(c => c.UserId == userId && c.State != ConversationState.Deleted);
        var total = await baseQuery.CountAsync();
        var items = await ApplyStandardIncludes(baseQuery)
            .OrderByDescending(c => c.LastReplyAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (items, total);
    }

    // M-7：返回 (Items, Total) 元组，先在未 Include 的查询上计数避免性能下降
    public async Task<(IEnumerable<Conversation> Items, int Total)> GetByCustomerIdAsync(int customerId, int page, int pageSize)
    {
        var baseQuery = _dbSet.AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.State != ConversationState.Deleted);
        var total = await baseQuery.CountAsync();
        var items = await ApplyStandardIncludes(baseQuery)
            .OrderByDescending(c => c.LastReplyAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (items, total);
    }

    /// <summary>
    /// 获取所有未逻辑删除的会话（用于"全部邮箱"视图），按最后回复时间倒序。
    /// </summary>
    public async Task<(IEnumerable<Conversation> Items, int Total)> GetAllActiveAsync(int page, int pageSize)
    {
        var baseQuery = _dbSet.AsNoTracking()
            .Where(c => c.State != ConversationState.Deleted);

        // 先在未 Include 的查询上计数，避免 Include 后 Count 性能下降
        var total = await baseQuery.CountAsync();

        var items = await ApplyStandardIncludes(baseQuery)
            .Include(c => c.Mailbox)
            .OrderByDescending(c => c.LastReplyAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    /// <summary>
    /// 获取已软删除的会话（用于垃圾箱视图），按更新时间倒序。
    /// mailboxId > 0 时按邮箱过滤，mailboxId <= 0 时返回所有邮箱的已删除会话。
    /// </summary>
    public async Task<(IEnumerable<Conversation> Items, int Total)> GetDeletedByMailboxIdAsync(int mailboxId, int page, int pageSize)
    {
        var baseQuery = _dbSet
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.DeletedAt != null);

        if (mailboxId > 0)
        {
            baseQuery = baseQuery.Where(c => c.MailboxId == mailboxId);
        }

        // 先在未 Include 的查询上计数，避免 Include 后 Count 性能下降
        var total = await baseQuery.CountAsync();

        var items = await baseQuery
            .Include(c => c.Customer!)
                .ThenInclude(cu => cu.Emails)
            .Include(c => c.User)
            .Include(c => c.Mailbox)
            .Include(c => c.ConversationTags!)
                .ThenInclude(ct => ct.Tag)
            .OrderByDescending(c => c.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Conversation?> GetDeletedByIdAsync(int id)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == id && c.DeletedAt != null)
            .Include(c => c.Customer!)
                .ThenInclude(cu => cu.Emails)
            .Include(c => c.User)
            .Include(c => c.ConversationTags!)
                .ThenInclude(ct => ct.Tag)
            .FirstOrDefaultAsync();
    }

    public async Task<Conversation?> GetDeletedTrackedByIdAsync(int id)
    {
        return await _dbSet
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt != null);
    }

    /// <summary>
    /// 彻底删除已软删除的会话（清空垃圾箱）。
    /// mailboxId > 0 时只删除该邮箱的，mailboxId <= 0 时删除所有邮箱的。
    /// 返回被删除的会话数量。
    /// </summary>
    public async Task<int> HardDeleteAllDeletedByMailboxIdAsync(int mailboxId)
    {
        var query = _dbSet
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null);

        if (mailboxId > 0)
        {
            query = query.Where(c => c.MailboxId == mailboxId);
        }

        var conversationIds = await query.Select(c => c.Id).ToListAsync();

        if (conversationIds.Count == 0) return 0;

        // 获取关联的线程 ID
        var threadIds = await _context.Threads
            .IgnoreQueryFilters()
            .Where(t => conversationIds.Contains(t.ConversationId))
            .Select(t => t.Id)
            .ToListAsync();

        // 事务由服务层（ClearTrashByMailboxAsync）管理，仓储层不再启动嵌套事务
        // 按外键依赖顺序使用 ExecuteDeleteAsync 删除关联数据
        if (threadIds.Count > 0)
        {
            await _context.Attachments.IgnoreQueryFilters()
                .Where(a => a.ThreadId != null && threadIds.Contains(a.ThreadId.Value))
                .ExecuteDeleteAsync();
            await _context.SendLogs.IgnoreQueryFilters()
                .Where(s => s.ThreadId != null && threadIds.Contains(s.ThreadId.Value))
                .ExecuteDeleteAsync();
        }

        // 清理 Restrict 关联的审计数据
        await _context.WorkflowExecutionLogs.IgnoreQueryFilters()
            .Where(l => l.ConversationId.HasValue && conversationIds.Contains(l.ConversationId.Value))
            .ExecuteDeleteAsync();
        await _context.SlaViolations.IgnoreQueryFilters()
            .Where(v => conversationIds.Contains(v.ConversationId))
            .ExecuteDeleteAsync();
        await _context.MacroExecutionLogs.IgnoreQueryFilters()
            .Where(l => conversationIds.Contains(l.ConversationId))
            .ExecuteDeleteAsync();

        await _context.SatisfactionRatings.IgnoreQueryFilters()
            .Where(r => conversationIds.Contains(r.ConversationId))
            .ExecuteDeleteAsync();
        await _context.ConversationTags.IgnoreQueryFilters()
            .Where(ct => conversationIds.Contains(ct.ConversationId))
            .ExecuteDeleteAsync();
        await _context.ConversationFolders.IgnoreQueryFilters()
            .Where(cf => conversationIds.Contains(cf.ConversationId))
            .ExecuteDeleteAsync();
        await _context.Followers.IgnoreQueryFilters()
            .Where(f => conversationIds.Contains(f.ConversationId))
            .ExecuteDeleteAsync();
        await _context.Stars.IgnoreQueryFilters()
            .Where(s => conversationIds.Contains(s.ConversationId))
            .ExecuteDeleteAsync();
        await _context.Threads.IgnoreQueryFilters()
            .Where(t => conversationIds.Contains(t.ConversationId))
            .ExecuteDeleteAsync();
        await query.ExecuteDeleteAsync();

        return conversationIds.Count;
    }

    public async Task HardDeleteAsync(int id)
    {
        var conversationIds = new List<int> { id };

        var threadIds = await _context.Threads
            .IgnoreQueryFilters()
            .Where(t => t.ConversationId == id)
            .Select(t => t.Id)
            .ToListAsync();

        // 事务由服务层（HardDeleteConversationAsync）管理，仓储层不再启动嵌套事务
        if (threadIds.Count > 0)
        {
            await _context.Attachments.IgnoreQueryFilters()
                .Where(a => a.ThreadId != null && threadIds.Contains(a.ThreadId.Value))
                .ExecuteDeleteAsync();
            await _context.SendLogs.IgnoreQueryFilters()
                .Where(s => s.ThreadId != null && threadIds.Contains(s.ThreadId.Value))
                .ExecuteDeleteAsync();
        }

        // 清理 Restrict 关联的审计数据
        await _context.WorkflowExecutionLogs.IgnoreQueryFilters()
            .Where(l => l.ConversationId == id)
            .ExecuteDeleteAsync();
        await _context.SlaViolations.IgnoreQueryFilters()
            .Where(v => v.ConversationId == id)
            .ExecuteDeleteAsync();
        await _context.MacroExecutionLogs.IgnoreQueryFilters()
            .Where(l => l.ConversationId == id)
            .ExecuteDeleteAsync();

        await _context.SatisfactionRatings.IgnoreQueryFilters()
            .Where(r => r.ConversationId == id)
            .ExecuteDeleteAsync();
        await _context.ConversationTags.IgnoreQueryFilters()
            .Where(ct => ct.ConversationId == id)
            .ExecuteDeleteAsync();
        await _context.ConversationFolders.IgnoreQueryFilters()
            .Where(cf => cf.ConversationId == id)
            .ExecuteDeleteAsync();
        await _context.Followers.IgnoreQueryFilters()
            .Where(f => f.ConversationId == id)
            .ExecuteDeleteAsync();
        await _context.Stars.IgnoreQueryFilters()
            .Where(s => s.ConversationId == id)
            .ExecuteDeleteAsync();
        await _context.Threads.IgnoreQueryFilters()
            .Where(t => t.ConversationId == id)
            .ExecuteDeleteAsync();
        await _dbSet.IgnoreQueryFilters().Where(c => c.Id == id).ExecuteDeleteAsync();
    }

    public async Task<IEnumerable<Conversation>> SearchAsync(string query, int page, int pageSize)
    {
        return await ApplyStandardIncludes(_dbSet.AsNoTracking())
            .Where(c => (c.Subject.Contains(query) || c.CustomerEmail.Contains(query)) && c.State != ConversationState.Deleted)
            .OrderByDescending(c => c.LastReplyAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<(IEnumerable<Conversation> Items, int Total)> AdvancedSearchAsync(
        string? keyword = null,
        ConversationStatus? status = null,
        ConversationType? type = null,
        SourceType? sourceType = null,
        int? mailboxId = null,
        List<int>? mailboxIds = null,
        int? userId = null,
        int? customerId = null,
        string? customerEmail = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        DateTime? lastReplyStartDate = null,
        DateTime? lastReplyEndDate = null,
        bool? hasAttachments = null,
        bool? isUnassigned = null,
        int? priority = null,
        int? starredByUserId = null,
        int page = 1,
        int pageSize = 20,
        int? readerUserId = null)
    {
        var query = _dbSet.AsNoTracking().AsQueryable();

        // 过滤已删除的会话
        query = query.Where(c => c.State != ConversationState.Deleted);

        if (!string.IsNullOrEmpty(keyword))
        {
            var kw = keyword.Trim();
            // SearchVector 已预处理为小写全文索引（Subject + Preview + CustomerEmail + Customer 姓名），
            // 作为唯一搜索条件避免多列 LIKE '%kw%' 无法走索引的性能问题。
            // 注意：不要在 LINQ 中调用 ToLowerInvariant()，SQLite 不可翻译会抛 InvalidOperationException。
            // SQLite LIKE 在 ASCII 场景默认大小写不敏感（pragma case_sensitive_like = 0）。
            query = query.Where(c => c.SearchVector != null && c.SearchVector.Contains(kw));
        }

        if (status.HasValue)
        {
            query = query.Where(c => c.Status == status.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(c => c.Type == type.Value);
        }

        if (sourceType.HasValue)
        {
            query = query.Where(c => c.SourceType == sourceType.Value);
        }

        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value);
        }

        if (mailboxIds != null && mailboxIds.Count > 0)
        {
            // 防止 IDOR：仅查询指定邮箱列表中的会话（用于全局搜索的邮箱访问过滤）
            query = query.Where(c => mailboxIds.Contains(c.MailboxId));
        }

        if (userId.HasValue)
        {
            query = query.Where(c => c.UserId == userId.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(c => c.CustomerId == customerId.Value);
        }

        if (!string.IsNullOrEmpty(customerEmail))
        {
            query = query.Where(c => c.CustomerEmail.Equals(customerEmail.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (startDate.HasValue)
        {
            query = query.Where(c => c.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var endExclusive = endDate.Value.AddDays(1);
            query = query.Where(c => c.CreatedAt < endExclusive);
        }

        if (lastReplyStartDate.HasValue)
        {
            query = query.Where(c => c.LastReplyAt >= lastReplyStartDate.Value);
        }

        if (lastReplyEndDate.HasValue)
        {
            var endExclusive = lastReplyEndDate.Value.AddDays(1);
            query = query.Where(c => c.LastReplyAt < endExclusive);
        }

        if (hasAttachments.HasValue)
        {
            query = query.Where(c => c.HasAttachments == hasAttachments.Value);
        }

        if (isUnassigned.HasValue && isUnassigned.Value)
        {
            query = query.Where(c => c.UserId == null);
        }

        if (priority.HasValue)
        {
            var priorityValue = (ConversationPriority)priority.Value;
            query = query.Where(c => c.Priority == priorityValue);
        }

        if (starredByUserId.HasValue)
        {
            query = query.Where(c => _context.Followers.Any(f => f.ConversationId == c.Id && f.UserId == starredByUserId.Value));
        }

        var total = await query.CountAsync();

        // 排序规则：有新消息（未读）的会话置顶，其次按最后回复时间倒序。
        // 未读判定：用户从未查看（无 ReadState 记录）或 ReadState.LastReadAt < Conversation.LastReplyAt。
        // 不论会话状态（Active/Pending/Closed）或创建时间，只要有未读消息就排在已读会话之前。
        IQueryable<Conversation> orderedQuery = ApplyStandardIncludes(query)
            .Include(c => c.Mailbox);

        if (readerUserId.HasValue)
        {
            var uid = readerUserId.Value;
            // 主排序：未读（1）排前，已读（0）排后
            // 未读 = 不存在 ReadState 记录，或 ReadState.LastReadAt < (LastReplyAt ?? CreatedAt)
            orderedQuery = orderedQuery
                .OrderByDescending(c => !_context.ConversationReadStates
                    .Any(r => r.ConversationId == c.Id && r.UserId == uid
                              && r.LastReadAt >= (c.LastReplyAt ?? c.CreatedAt)))
                .ThenByDescending(c => c.LastReplyAt ?? c.CreatedAt);
        }
        else
        {
            orderedQuery = orderedQuery
                .OrderByDescending(c => c.LastReplyAt ?? c.CreatedAt);
        }

        var items = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<Dictionary<int, DateTime>> GetLastReadAtMapAsync(IEnumerable<int> conversationIds, int userId)
    {
        var ids = conversationIds?.ToList();
        if (ids == null || ids.Count == 0) return new Dictionary<int, DateTime>();

        return await _context.ConversationReadStates
            .AsNoTracking()
            .Where(r => r.UserId == userId && ids.Contains(r.ConversationId))
            .ToDictionaryAsync(r => r.ConversationId, r => r.LastReadAt);
    }

    /// <inheritdoc />
    public async Task UpsertReadStateAsync(int conversationId, int userId)
    {
        // SQLite UPSERT：存在则更新 LastReadAt，不存在则插入
        // 仓储内不再调用 SaveChangesAsync，由调用方（ConversationService.MarkConversationAsReadAsync）统一 SaveChanges。
        var existing = await _context.ConversationReadStates
            .FirstOrDefaultAsync(r => r.ConversationId == conversationId && r.UserId == userId);

        var now = DateTime.UtcNow;
        if (existing != null)
        {
            existing.LastReadAt = now;
            existing.UpdatedAt = now;
        }
        else
        {
            _context.ConversationReadStates.Add(new ConversationReadState
            {
                ConversationId = conversationId,
                UserId = userId,
                LastReadAt = now,
                UpdatedAt = now
            });
        }
    }

    public async Task<int> CountByStatusAsync(ConversationStatus status)
    {
        return await _dbSet.AsNoTracking().CountAsync(c => c.Status == status && c.State != ConversationState.Deleted);
    }

    public async Task<int> CountByMailboxIdAndStatusAsync(int mailboxId, ConversationStatus status)
    {
        return await _dbSet.AsNoTracking().CountAsync(c => c.MailboxId == mailboxId && c.Status == status && c.State != ConversationState.Deleted);
    }

    public async Task<int> CountByFolderIdAsync(int folderId)
    {
        return await _dbSet.AsNoTracking().CountAsync(c => c.FolderId == folderId && c.State != ConversationState.Deleted);
    }

    public async Task<int> CountByUserIdAndStatusAsync(int userId, ConversationStatus status)
    {
        return await _dbSet.AsNoTracking().CountAsync(c => c.UserId == userId && c.Status == status && c.State != ConversationState.Deleted);
    }

    public async Task<int> CountCreatedInDateRangeAsync(DateTime startDate, DateTime endDate, int? mailboxId = null)
    {
        var query = _dbSet.AsNoTracking().Where(c => c.CreatedAt >= startDate && c.CreatedAt < endDate && c.State != ConversationState.Deleted);
        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value);
        }
        return await query.CountAsync();
    }

    public async Task<int> CountClosedInDateRangeAsync(DateTime startDate, DateTime endDate, int? mailboxId = null)
    {
        var query = _dbSet.AsNoTracking().Where(c => c.ClosedAt.HasValue && c.ClosedAt.Value >= startDate && c.ClosedAt.Value < endDate && c.State != ConversationState.Deleted);
        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value);
        }
        return await query.CountAsync();
    }

    public async Task<int> CountThreadRepliesByUserIdInDateRangeAsync(int userId, DateTime startDate, DateTime endDate)
    {
        return await _context.Threads
            .Where(t => t.UserId == userId
                && t.CreatedAt >= startDate
                && t.CreatedAt < endDate
                && t.Type == ThreadType.Message)
            .CountAsync();
    }

    public async Task<int> CountUnassignedActiveAsync()
    {
        return await _dbSet.AsNoTracking().CountAsync(c => c.UserId == null && c.Status == ConversationStatus.Active && c.State != ConversationState.Deleted);
    }

    public async Task<int> CountActiveByCustomerIdAsync(int customerId)
    {
        return await _dbSet.AsNoTracking().CountAsync(c => c.CustomerId == customerId && c.State != ConversationState.Deleted);
    }

    public async Task<Dictionary<int, int>> CountActiveByCustomerIdsAsync(IEnumerable<int> customerIds)
    {
        var idList = customerIds.Distinct().ToList();
        if (idList.Count == 0)
            return new Dictionary<int, int>();

        return await _dbSet
            .AsNoTracking()
            .Where(c => c.CustomerId.HasValue && idList.Contains(c.CustomerId.Value) && c.State != ConversationState.Deleted)
            .GroupBy(c => c.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count);
    }

    public async Task<int> CountDeletedByCustomerIdAsync(int customerId)
    {
        return await _dbSet.AsNoTracking().CountAsync(c => c.CustomerId == customerId && c.State == ConversationState.Deleted);
    }

    public async Task<Dictionary<int, int>> CountDeletedByCustomerIdsAsync(IEnumerable<int> customerIds)
    {
        var idList = customerIds.Distinct().ToList();
        if (idList.Count == 0)
            return new Dictionary<int, int>();

        return await _dbSet
            .AsNoTracking()
            .Where(c => c.CustomerId.HasValue && idList.Contains(c.CustomerId.Value) && c.State == ConversationState.Deleted)
            .GroupBy(c => c.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count);
    }

    public async Task<int> CountActiveByUserIdAsync(int userId)
    {
        return await _dbSet.AsNoTracking().CountAsync(c => c.UserId == userId && c.State != ConversationState.Deleted);
    }

    public async Task<Dictionary<int, int>> GetCountByCustomerIdsAsync(IEnumerable<int> customerIds)
    {
        var idList = customerIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        return await _dbSet
            .AsNoTracking()
            .Where(c => c.CustomerId.HasValue && idList.Contains(c.CustomerId.Value) && c.State != ConversationState.Deleted)
            .GroupBy(c => c.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count);
    }

    public async Task<int> SoftDeleteByCustomerIdAsync(int customerId)
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(c => c.CustomerId == customerId && c.State != ConversationState.Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.State, ConversationState.Deleted)
                .SetProperty(c => c.DeletedAt, now)
                .SetProperty(c => c.UpdatedAt, now));
    }

    public async Task<int> SoftDeleteByCustomerIdsAsync(IEnumerable<int> customerIds)
    {
        var idList = customerIds.Distinct().ToList();
        if (idList.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(c => c.CustomerId.HasValue && idList.Contains(c.CustomerId.Value) && c.State != ConversationState.Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.State, ConversationState.Deleted)
                .SetProperty(c => c.DeletedAt, now)
                .SetProperty(c => c.UpdatedAt, now));
    }

    public async Task<int> RestoreByCustomerIdAsync(int customerId)
    {
        var now = DateTime.UtcNow;
        DateTime? nullDeletedAt = null;
        return await _dbSet
            .Where(c => c.CustomerId == customerId && c.State == ConversationState.Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.State, ConversationState.Published)
                .SetProperty(c => c.DeletedAt, nullDeletedAt)
                .SetProperty(c => c.UpdatedAt, now));
    }

    public async Task<Dictionary<int, int>> GetActiveCountByUserIdAsync()
    {
        return await _dbSet
            .AsNoTracking()
            .Where(c => c.Status == ConversationStatus.Active && c.State != ConversationState.Deleted && c.UserId.HasValue)
            .GroupBy(c => c.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);
    }

    public async Task<Dictionary<int, int>> GetClosedCountByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(c => c.ClosedAt.HasValue && c.ClosedAt.Value >= startDate && c.ClosedAt.Value < endDate && c.State != ConversationState.Deleted && c.UserId.HasValue)
            .GroupBy(c => c.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);
    }

    public async Task<Dictionary<int, int>> GetReplyCountByUserIdAndDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Threads
            .Where(t => t.UserId.HasValue
                && t.CreatedAt >= startDate
                && t.CreatedAt < endDate
                && t.Type == ThreadType.Message)
            .GroupBy(t => t.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);
    }

    public async Task<Dictionary<int, (int Active, int Pending, int Closed)>> GetStatusCountsByMailboxIdAsync()
    {
        return await _dbSet
            .AsNoTracking()
            .Where(c => c.State != ConversationState.Deleted)
            .GroupBy(c => c.MailboxId)
            .Select(g => new
            {
                MailboxId = g.Key,
                Active = g.Count(c => c.Status == ConversationStatus.Active),
                Pending = g.Count(c => c.Status == ConversationStatus.Pending),
                Closed = g.Count(c => c.Status == ConversationStatus.Closed)
            })
            .ToDictionaryAsync(x => x.MailboxId, x => (x.Active, x.Pending, x.Closed));
    }

    public async Task<Dictionary<DateTime, (int NewCount, int ClosedCount)>> GetTrendAsync(DateTime startDate, DateTime endDate, int? mailboxId = null)
    {
        var newConversations = _dbSet
            .AsNoTracking()
            .Where(c => c.CreatedAt >= startDate && c.CreatedAt < endDate && c.State != ConversationState.Deleted);
        var closedConversations = _dbSet
            .AsNoTracking()
            .Where(c => c.ClosedAt.HasValue && c.ClosedAt.Value >= startDate && c.ClosedAt.Value < endDate && c.State != ConversationState.Deleted);

        if (mailboxId.HasValue)
        {
            newConversations = newConversations.Where(c => c.MailboxId == mailboxId.Value);
            closedConversations = closedConversations.Where(c => c.MailboxId == mailboxId.Value);
        }

        var newByDate = await newConversations
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count);

        var closedByDate = await closedConversations
            .GroupBy(c => c.ClosedAt!.Value.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count);

        var result = new Dictionary<DateTime, (int, int)>();
        for (var date = startDate.Date; date < endDate.Date; date = date.AddDays(1))
        {
            result[date] = (
                newByDate.TryGetValue(date, out var newCount) ? newCount : 0,
                closedByDate.TryGetValue(date, out var closedCount) ? closedCount : 0
            );
        }

        return result;
    }

    public async Task<IEnumerable<ConversationThread>> GetThreadsByConversationIdAsync(int conversationId)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .SelectMany(c => c.Threads.Where(t =>
                t.State != Domain.Entities.ThreadState.Deleted &&
                t.State != Domain.Entities.ThreadState.Draft))
            .Include(t => t.User)
            .Include(t => t.Attachments)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<Dictionary<ConversationStatus, int>> GetStatusDistributionAsync(int? mailboxId = null)
    {
        var query = _dbSet.AsNoTracking().Where(c => c.State != ConversationState.Deleted);

        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value);
        }

        return await query
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);
    }

    public async Task<int> CountThreadRepliesInDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Threads
            .Where(t => t.Type == ThreadType.Message && t.CreatedAt >= startDate && t.CreatedAt < endDate)
            .CountAsync();
    }

    public async Task<double> AverageResolutionHoursAsync(DateTime startDate, DateTime endDate, int? mailboxId = null)
    {
        var query = _dbSet.AsNoTracking()
            .Where(c => c.State != ConversationState.Deleted
                && c.ClosedAt.HasValue
                && c.ClosedAt.Value >= startDate
                && c.ClosedAt.Value < endDate);

        if (mailboxId.HasValue)
        {
            query = query.Where(c => c.MailboxId == mailboxId.Value);
        }

        // SQLite 无法翻译 DateDiffSecond/Ticks，仅投影所需列后客户端计算
        var rows = await query
            .Select(c => new { c.CreatedAt, c.ClosedAt })
            .ToListAsync();

        if (rows.Count == 0) return 0;

        return rows.Average(c => (c.ClosedAt!.Value - c.CreatedAt).TotalHours);
    }

    public async Task<Dictionary<int, (int AssignedCount, int ReplyCount, int ClosedCount, double AvgFirstResponseHours)>> GetUserPerformanceAsync(DateTime startDate, DateTime endDate, int? mailboxId = null)
    {
        var activeQuery = _dbSet
            .Where(c => c.State != ConversationState.Deleted && c.Status == ConversationStatus.Active && c.UserId.HasValue);
        var closedQuery = _dbSet
            .Where(c => c.State != ConversationState.Deleted
                && c.ClosedAt.HasValue
                && c.ClosedAt.Value >= startDate
                && c.ClosedAt.Value < endDate
                && c.UserId.HasValue);
        var replyQuery = _context.Threads
            .Where(t => t.Type == ThreadType.Message
                && t.CreatedAt >= startDate
                && t.CreatedAt < endDate
                && t.UserId.HasValue);

        if (mailboxId.HasValue)
        {
            activeQuery = activeQuery.Where(c => c.MailboxId == mailboxId.Value);
            closedQuery = closedQuery.Where(c => c.MailboxId == mailboxId.Value);
        }

        var activeByUser = await activeQuery
            .GroupBy(c => c.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var closedByUser = await closedQuery
            .GroupBy(c => c.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var replyByUser = await replyQuery
            .GroupBy(t => t.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var allUserIds = activeByUser.Keys
            .Union(closedByUser.Keys)
            .Union(replyByUser.Keys)
            .Distinct()
            .ToList();

        var result = new Dictionary<int, (int AssignedCount, int ReplyCount, int ClosedCount, double AvgFirstResponseHours)>();
        foreach (var userId in allUserIds)
        {
            result[userId] = (
                AssignedCount: activeByUser.TryGetValue(userId, out var ac) ? ac : 0,
                ReplyCount: replyByUser.TryGetValue(userId, out var rc) ? rc : 0,
                ClosedCount: closedByUser.TryGetValue(userId, out var cc) ? cc : 0,
                AvgFirstResponseHours: 0
            );
        }

        return result;
    }

    public async Task<Dictionary<int, (int NewCount, int ClosedCount, int ActiveCount)>> GetMailboxLoadAsync(DateTime startDate, DateTime endDate)
    {
        var newQuery = _dbSet
            .Where(c => c.State != ConversationState.Deleted && c.CreatedAt >= startDate && c.CreatedAt < endDate);
        var closedQuery = _dbSet
            .Where(c => c.State != ConversationState.Deleted
                && c.ClosedAt.HasValue
                && c.ClosedAt.Value >= startDate
                && c.ClosedAt.Value < endDate);
        var activeQuery = _dbSet
            .Where(c => c.State != ConversationState.Deleted && c.Status == ConversationStatus.Active);

        var newByMailbox = await newQuery
            .GroupBy(c => c.MailboxId)
            .Select(g => new { MailboxId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MailboxId, x => x.Count);

        var closedByMailbox = await closedQuery
            .GroupBy(c => c.MailboxId)
            .Select(g => new { MailboxId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MailboxId, x => x.Count);

        var activeByMailbox = await activeQuery
            .GroupBy(c => c.MailboxId)
            .Select(g => new { MailboxId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MailboxId, x => x.Count);

        var allMailboxIds = newByMailbox.Keys
            .Union(closedByMailbox.Keys)
            .Union(activeByMailbox.Keys)
            .Distinct()
            .ToList();

        var result = new Dictionary<int, (int NewCount, int ClosedCount, int ActiveCount)>();
        foreach (var mbId in allMailboxIds)
        {
            result[mbId] = (
                NewCount: newByMailbox.TryGetValue(mbId, out var nc) ? nc : 0,
                ClosedCount: closedByMailbox.TryGetValue(mbId, out var cc) ? cc : 0,
                ActiveCount: activeByMailbox.TryGetValue(mbId, out var ac) ? ac : 0
            );
        }

        return result;
    }

    public async Task<(IEnumerable<Conversation> Items, int Total)> GetPagedByStatusesAsync(
        int page,
        int pageSize,
        IEnumerable<ConversationStatus> statuses)
    {
        var statusList = statuses.ToList();
        var query = _dbSet
            .AsNoTracking()
            .Where(c => c.State != ConversationState.Deleted && statusList.Contains(c.Status));

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    /// <summary>
    /// 获取下一个可用的会话编号（Max(Number) + 1，仅统计未软删除记录）。
    /// Number 字段有唯一索引（过滤已软删除记录），新增会话时必须调用此方法避免唯一约束冲突。
    /// </summary>
    /// <remarks>
    /// ⚠️ 并发竞态风险：本方法仅读取 Max(Number)+1，与实际 INSERT（调用方 SaveChangesAsync）之间存在
    /// TOCTOU 竞态窗口——两个并发请求（如同时收到邮件 + 聊天转工单）可能读到相同 maxNumber、计算出
    /// 相同 nextNumber，导致第二个 INSERT 触发 Number 唯一索引冲突。
    /// 该竞态由 <see cref="Persistence.NoneadCSSDbContext.SaveChangesAsync(CancellationToken)"/>
    /// 统一处理：捕获会话编号唯一约束冲突后，重新查询 Max(Number)+1 并按指数退避（50/100/200ms）重试，
    /// 最多 3 次，重试耗尽仍失败则抛出原始异常。调用方无需感知此重试机制。
    /// </remarks>
    public async Task<int> GetNextNumberAsync()
    {
        var maxNumber = await _dbSet
            .AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .MaxAsync(c => (int?)c.Number) ?? 0;
        return maxNumber + 1;
    }

    /// <summary>
    /// 按会话编号（Number，面向用户引用的 [#数字] 标记）获取未软删除的会话（含完整 Includes，AsNoTracking）。
    /// 用于邮件回复时通过主题标记匹配现有会话。
    /// </summary>
    public async Task<Conversation?> GetByNumberAsync(int number)
    {
        return await ApplyFullIncludes(_dbSet.AsNoTracking())
            .FirstOrDefaultAsync(c => c.Number == number && c.State != ConversationState.Deleted);
    }

    /// <summary>
    /// 获取指定会话的所有文件夹关联（追踪查询）。
    /// 不依赖 Conversation.ConversationFolders 导航属性是否已加载，
    /// 用于文件夹同步逻辑（T000060：避免导航集合为空时旧关联不删除、新关联重复 INSERT 触发 PK 冲突）。
    /// </summary>
    public async Task<List<ConversationFolder>> GetConversationFoldersTrackedAsync(int conversationId)
    {
        return await _context.ConversationFolders
            .Where(cf => cf.ConversationId == conversationId)
            .ToListAsync();
    }

    /// <summary>
    /// 删除指定的会话-文件夹关联（追踪实体，需配合 SaveChangesAsync 持久化）。
    /// </summary>
    public void RemoveConversationFolder(ConversationFolder folder)
    {
        _context.ConversationFolders.Remove(folder);
    }

    /// <summary>
    /// 按 IM 渠道（Channel + ChannelId + SourceType）查找最近活跃会话的私有复用方法。
    /// 抽取自 WhatsApp / DingTalk 两个查询，避免重复实现。
    /// </summary>
    private async Task<Conversation?> FindActiveImConversationByChannelAsync(
        string channel, string channelId, Domain.Enums.SourceType sourceType, TimeSpan within)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        var cutoff = DateTime.UtcNow - within;

        var query = from conv in ApplyStandardIncludes(_dbSet)
                    join customer in _context.Set<Customer>() on conv.CustomerId equals customer.Id
                    where conv.DeletedAt == null
                        && conv.SourceType == sourceType
                        && conv.Status != Domain.Enums.ConversationStatus.Closed
                        && customer.DeletedAt == null
                        && customer.Channel == channel
                        && customer.ChannelId == channelId
                        && conv.UpdatedAt >= cutoff
                    orderby conv.UpdatedAt descending
                    select conv;

        return await query.FirstOrDefaultAsync();
    }

    public Task<Conversation?> FindActiveWhatsAppConversationByWaIdAsync(string waId, TimeSpan within)
        => FindActiveImConversationByChannelAsync("whatsapp", waId, Domain.Enums.SourceType.WhatsApp, within);

    public Task<Conversation?> FindActiveDingTalkConversationByUserIdAsync(string staffId, TimeSpan within)
        => FindActiveImConversationByChannelAsync("dingtalk", staffId, Domain.Enums.SourceType.DingTalk, within);

    public Task<Conversation?> FindActiveMessengerConversationByChannelIdAsync(string psid, TimeSpan within)
        => FindActiveImConversationByChannelAsync("messenger", psid, Domain.Enums.SourceType.Messenger, within);

    public Task<Conversation?> FindActiveTelegramConversationByChatIdAsync(string chatId, TimeSpan within)
        => FindActiveImConversationByChannelAsync("telegram", chatId, Domain.Enums.SourceType.Telegram, within);

    public Task<Conversation?> FindActiveLineConversationByUserIdAsync(string userId, TimeSpan within)
        => FindActiveImConversationByChannelAsync("line", userId, Domain.Enums.SourceType.Line, within);

    /// <summary>
    /// iMessage 客户标识已按 (mode, external_id) 作用域化（见 IMessageIdentity.BuildScopedChannelId），
    /// 因此这里直接按已作用域化的 ChannelId 匹配，不再二次拼接前缀。
    /// </summary>
    public Task<Conversation?> FindActiveIMessageConversationAsync(string scopedChannelId, TimeSpan within)
        => FindActiveImConversationByChannelAsync("imessage", scopedChannelId, Domain.Enums.SourceType.IMessage, within);

    public async Task<int> BulkUpdatePriorityAsync(IEnumerable<int> conversationIds, ConversationPriority priority)
    {
        var idList = conversationIds.Distinct().ToList();
        if (idList.Count == 0) return 0;

        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(c => idList.Contains(c.Id) && c.State != ConversationState.Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Priority, priority)
                .SetProperty(c => c.UpdatedAt, now));
    }

    public async Task<int> BulkSoftDeleteAsync(IEnumerable<int> conversationIds)
    {
        var idList = conversationIds.Distinct().ToList();
        if (idList.Count == 0) return 0;

        var now = DateTime.UtcNow;
        return await _dbSet
            .Where(c => idList.Contains(c.Id) && c.State != ConversationState.Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.State, ConversationState.Deleted)
                .SetProperty(c => c.DeletedAt, now)
                .SetProperty(c => c.UpdatedAt, now));
    }
}
