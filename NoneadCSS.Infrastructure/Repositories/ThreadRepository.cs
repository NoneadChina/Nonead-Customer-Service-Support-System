using System.Linq.Expressions;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

/// <summary>
/// 线程仓储实现
/// </summary>
public class ThreadRepository : Repository<ConversationThread>, IThreadRepository
{
    public ThreadRepository(NoneadCSSDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 根据Message-ID查找线程
    /// </summary>
    public async Task<ConversationThread?> FindByMessageIdAsync(string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return null;
        }
        
        return await _dbSet
            .AsNoTracking()
            .Include(t => t.Conversation)
            .FirstOrDefaultAsync(t => t.MessageId == messageId);
    }

    /// <summary>
    /// 获取会话的所有线程（按创建时间排序，不包含草稿）
    /// </summary>
    public async Task<List<ConversationThread>> GetByConversationIdAsync(int conversationId, bool includeAttachments = false)
    {
        var query = _dbSet
            .AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.ConversationId == conversationId && t.State != Domain.Entities.ThreadState.Draft)
            .OrderBy(t => t.CreatedAt);

        if (includeAttachments)
        {
            return await query.Include(t => t.Attachments).ToListAsync();
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// 获取会话的聊天模式线程（排除已删除，含 User 信息，按创建时间排序）
    /// </summary>
    public async Task<List<ConversationThread>> GetChatModeThreadsAsync(int conversationId)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.ConversationId == conversationId && t.State != Domain.Entities.ThreadState.Deleted)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// 获取会话的首条线程
    /// </summary>
    public async Task<ConversationThread?> GetFirstThreadAsync(int conversationId)
    {
        return await _dbSet.AsNoTracking()
            .Where(t => t.ConversationId == conversationId && t.IsFirst)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 获取会话的最新线程
    /// </summary>
    public async Task<ConversationThread?> GetLatestThreadAsync(int conversationId)
    {
        return await _dbSet.AsNoTracking()
            .Where(t => t.ConversationId == conversationId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 统计会话的有效线程数（不含LineItem类型）
    /// </summary>
    public async Task<int> CountValidThreadsAsync(int conversationId)
    {
        return await _dbSet.AsNoTracking()
            .Where(t => t.ConversationId == conversationId && t.Type != ThreadType.LineItem)
            .CountAsync();
    }

    /// <summary>
    /// 获取会话的草稿线程
    /// </summary>
    public async Task<ConversationThread?> GetDraftThreadAsync(int conversationId)
    {
        return await _dbSet.AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.ConversationId == conversationId && t.State == Domain.Entities.ThreadState.Draft)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 获取会话的草稿线程（tracking，适用于后续修改 + SaveChanges 的场景）
    /// </summary>
    public async Task<ConversationThread?> GetDraftThreadTrackedAsync(int conversationId)
    {
        return await _dbSet
            .Include(t => t.User)
            .Where(t => t.ConversationId == conversationId && t.State == Domain.Entities.ThreadState.Draft)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 获取用户的所有草稿线程
    /// </summary>
    public async Task<List<ConversationThread>> GetDraftsByUserIdAsync(int userId)
    {
        return await _dbSet.AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.UserId == userId && t.State == Domain.Entities.ThreadState.Draft)
            .Include(t => t.Conversation)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync();
    }
    
    /// <summary>
    /// 获取会话中客户发送的最近一条消息（用于设置 In-Reply-To）
    /// </summary>
    public async Task<ConversationThread?> GetLatestCustomerThreadAsync(int conversationId)
    {
        return await _dbSet.AsNoTracking()
            .Where(t => t.ConversationId == conversationId 
                && t.Type == ThreadType.Customer 
                && !string.IsNullOrEmpty(t.MessageId))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<double> CalculateAverageFirstResponseTimeMinutesAsync()
    {
        var firstReplies = await _dbSet.AsNoTracking()
            .Where(t => t.Type == ThreadType.Message && t.UserId != null)
            .GroupBy(t => t.ConversationId)
            .Select(g => new
            {
                ConversationId = g.Key,
                FirstReplyAt = g.Min(t => t.CreatedAt)
            })
            .ToListAsync();

        if (!firstReplies.Any())
            return 0;

        var conversationIds = firstReplies.Select(r => r.ConversationId).ToList();
        var conversations = await _context.Conversations.AsNoTracking()
            .Where(c => conversationIds.Contains(c.Id) && c.State != ConversationState.Deleted)
            .ToDictionaryAsync(c => c.Id, c => c.CreatedAt);

        var responseTimes = firstReplies
            .Where(r => conversations.TryGetValue(r.ConversationId, out var createdAt))
            .Select(r => (r.FirstReplyAt - conversations[r.ConversationId]).TotalMinutes)
            .Where(m => m >= 0)
            .ToList();

        return responseTimes.Count > 0 ? responseTimes.Average() : 0;
    }

    public async Task<double> CalculateAverageResolutionTimeMinutesAsync()
    {
        var conversationTimes = await _context.Conversations.AsNoTracking()
            .Where(c => c.Status == ConversationStatus.Closed && c.ClosedAt.HasValue && c.State != ConversationState.Deleted)
            .Select(c => new { c.ClosedAt, c.CreatedAt })
            .ToListAsync();

        var resolutionTimes = conversationTimes
            .Where(c => c.ClosedAt.HasValue)
            .Select(c => (c.ClosedAt!.Value - c.CreatedAt).TotalMinutes)
            .Where(m => m >= 0)
            .ToList();

        return resolutionTimes.Count > 0 ? resolutionTimes.Average() : 0;
    }

    public async Task<Dictionary<int, List<ConversationThread>>> GetByConversationIdsAsync(IEnumerable<int> conversationIds)
    {
        var idList = conversationIds.ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<int, List<ConversationThread>>();
        }

        return await _dbSet.AsNoTracking()
            .Where(t => idList.Contains(t.ConversationId))
            .GroupBy(t => t.ConversationId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.OrderBy(t => t.CreatedAt).ToList());
    }
}