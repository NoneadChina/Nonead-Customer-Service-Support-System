using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IChatSessionRepository
{
    Task<ChatSession?> GetByIdAsync(int id);
    Task<ChatSession?> GetByIdTrackedAsync(int id);
    Task<ChatSession?> GetByTokenAsync(string sessionToken);
    /// <summary>
    /// 按关联的工单 ConversationId 查询聊天会话（聊天转工单后，在工单详情页反查聊天评分）
    /// </summary>
    Task<ChatSession?> GetByConversationIdAsync(int conversationId);
    Task<IReadOnlyList<ChatSession>> GetListAsync(
        string? status = null,
        int? mailboxId = null,
        int? userId = null,
        int? customerId = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        int pageSize = 20,
        int[]? mailboxIds = null);
    Task<int> CountAsync(
        string? status = null,
        int? mailboxId = null,
        int? userId = null,
        int? customerId = null,
        string? keyword = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int[]? mailboxIds = null);
    Task<IReadOnlyList<ChatSession>> GetAllAsync();
    Task AddAsync(ChatSession session);
    [Obsolete("推荐改用 GetByIdTrackedAsync + 直接修改属性（避免 AsNoTracking + Update 反模式）。该方法后续版本将移除。")]
    void Update(ChatSession session);
    void Remove(ChatSession session);
    Task<int> CountByStatusAsync(string status, int[]? mailboxIds = null);
    Task<int> CountActiveAsync();
    Task<int> SumUnreadCountAsync(int[]? mailboxIds = null);
    /// <summary>
    /// 取所有 Status=awaiting_rating 且 RatingTimeoutAt &lt;= utcNow 的在线聊天会话（超时等待评分的），
    /// 按超时时间升序取前 limit 条，用于后台 worker 定时扫描关闭。
    /// 返回的实体是 tracked，便于 worker 修改字段后 SaveChanges。
    /// </summary>
    Task<List<ChatSession>> GetAwaitingRatingExpiredAsync(DateTime utcNow, int limit = 100);

    /// <summary>
    /// 聚合所有已评分（Rating.HasValue）的在线聊天会话，生成总览统计。
    /// 字段口径与 SatisfactionRatingStats 对齐，用于在 SatisfactionRatingService 中合并两类数据。
    /// </summary>
    Task<SatisfactionRatingStats> GetRatedStatsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? mailboxId = null,
        int? userId = null);

    /// <summary>
    /// 按日期分组聚合已评分的在线聊天会话。
    /// 评分日期取 ClosedAt（会话评分后关闭），无 ClosedAt 则回退 UpdatedAt。
    /// </summary>
    Task<List<Domain.Repositories.DailySatisfactionStats>> GetRatedDailyStatsAsync(
        DateTime startDate,
        DateTime endDate,
        int? mailboxId = null);

    /// <summary>
    /// 按邮箱（MailboxId）聚合已评分的在线聊天会话。
    /// </summary>
    Task<List<Domain.Repositories.MailboxSatisfactionStats>> GetRatedByMailboxAsync(
        DateTime? startDate = null,
        DateTime? endDate = null);

    /// <summary>
    /// 按客服用户（UserId）聚合已评分的在线聊天会话。
    /// </summary>
    Task<List<Domain.Repositories.UserSatisfactionStats>> GetRatedByUserAsync(
        DateTime? startDate = null,
        DateTime? endDate = null);

    /// <summary>
    /// 分页查询已评分的在线聊天会话，用于 SatisfactionRatingService.GetListAsync 合并数据源。
    /// </summary>
    Task<(IEnumerable<ChatSession> Items, int Total)> GetRatedPagedAsync(
        int page,
        int pageSize,
        int? rating = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null);

    /// <summary>
    /// 获取最近 N 条已评分的在线聊天会话。
    /// </summary>
    Task<List<ChatSession>> GetRecentRatedAsync(int count = 10, int? rating = null);

    // ====== 软删除 / 垃圾箱 / 硬删除 ======

    /// <summary>
    /// 获取垃圾箱会话列表（DeletedAt != null），支持关键词搜索 CustomerName/CustomerEmail/Subject。
    /// </summary>
    Task<IReadOnlyList<ChatSession>> GetDeletedListAsync(int page, int pageSize, string? keyword = null);

    /// <summary>
    /// 统计垃圾箱会话数量（DeletedAt != null）。
    /// </summary>
    Task<int> CountDeletedAsync(string? keyword = null);

    /// <summary>
    /// 软删除单个会话（设置 DeletedAt = UtcNow）。
    /// </summary>
    Task<bool> SoftDeleteAsync(int id);

    /// <summary>
    /// 批量软删除会话。
    /// </summary>
    Task<bool> SoftDeleteAsync(List<int> ids);

    /// <summary>
    /// 恢复软删除的会话（设置 DeletedAt = null）。
    /// </summary>
    Task<bool> RestoreAsync(int id);

    /// <summary>
    /// 彻底删除单个会话（先删关联消息，再物理删除会话本身）。
    /// </summary>
    Task<bool> HardDeleteAsync(int id);

    /// <summary>
    /// 清空垃圾箱：物理删除所有 DeletedAt != null 的会话及其关联消息。返回删除的会话数。
    /// </summary>
    Task<int> HardDeleteAllDeletedAsync();
}

public interface IChatMessageRepository
{
    Task<ChatMessage?> GetByIdAsync(int id);

    /// <summary>
    /// 按主键获取已跟踪的实体（用于修改场景）。避免 AsNoTracking + Update 反模式。
    /// </summary>
    Task<ChatMessage?> GetByIdTrackedAsync(int id);
    Task<IReadOnlyList<ChatMessage>> GetBySessionIdAsync(int sessionId, int limit = 100);
    Task<IReadOnlyList<ChatMessage>> GetListAsync(
        int? sessionId = null,
        string? senderType = null,
        bool? isRead = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        int pageSize = 50);
    Task<int> CountAsync(int? sessionId = null, string? senderType = null, bool? isRead = null);
    Task<Dictionary<int, int>> GetCountBySessionIdsAsync(IEnumerable<int> sessionIds);
    Task<int> CountUnreadAsync(int sessionId, string senderType);
    Task AddAsync(ChatMessage message);
    void Update(ChatMessage message);
    void Remove(ChatMessage message);
    Task MarkAsReadAsync(int sessionId, string senderType);
}
