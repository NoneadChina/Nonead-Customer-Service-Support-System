using System.Linq.Expressions;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// 线程仓储接口
/// </summary>
public interface IThreadRepository : IRepository<ConversationThread>
{
    /// <summary>
    /// 根据Message-ID查找线程
    /// </summary>
    Task<ConversationThread?> FindByMessageIdAsync(string messageId);
    
    /// <summary>
    /// 获取会话的所有线程（按创建时间排序）
    /// </summary>
    Task<List<ConversationThread>> GetByConversationIdAsync(int conversationId, bool includeAttachments = false);

    /// <summary>
    /// 获取会话的聊天模式线程（排除已删除，含 User 信息，按创建时间排序）
    /// </summary>
    Task<List<ConversationThread>> GetChatModeThreadsAsync(int conversationId);
    
    /// <summary>
    /// 获取会话的首条线程
    /// </summary>
    Task<ConversationThread?> GetFirstThreadAsync(int conversationId);
    
    /// <summary>
    /// 获取会话的最新线程
    /// </summary>
    Task<ConversationThread?> GetLatestThreadAsync(int conversationId);
    
    /// <summary>
    /// 统计会话的有效线程数（不含LineItem类型）
    /// </summary>
    Task<int> CountValidThreadsAsync(int conversationId);
    
    /// <summary>
    /// 获取会话的草稿线程
    /// </summary>
    Task<ConversationThread?> GetDraftThreadAsync(int conversationId);

    /// <summary>
    /// 获取会话的草稿线程（tracking，适用于后续修改 + SaveChanges 的场景）
    /// </summary>
    Task<ConversationThread?> GetDraftThreadTrackedAsync(int conversationId);
    
    /// <summary>
    /// 获取用户的所有草稿线程
    /// </summary>
    Task<List<ConversationThread>> GetDraftsByUserIdAsync(int userId);
    
    /// <summary>
    /// 获取会话中客户发送的最近一条消息（用于设置 In-Reply-To）
    /// </summary>
    Task<ConversationThread?> GetLatestCustomerThreadAsync(int conversationId);
    
    /// <summary>
    /// 计算所有会话的平均首次响应时间（分钟）
    /// </summary>
    Task<double> CalculateAverageFirstResponseTimeMinutesAsync();
    
    /// <summary>
    /// 计算已关闭会话的平均解决时间（分钟）
    /// </summary>
    Task<double> CalculateAverageResolutionTimeMinutesAsync();

    /// <summary>
    /// 批量获取多个会话的线程（按会话ID分组）
    /// </summary>
    Task<Dictionary<int, List<ConversationThread>>> GetByConversationIdsAsync(IEnumerable<int> conversationIds);
}