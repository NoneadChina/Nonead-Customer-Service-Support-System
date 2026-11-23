using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Domain.Repositories;

public interface IConversationRepository : IRepository<Conversation>
{
    /// <summary>
    /// 按 ID 列表批量获取会话（含完整 Includes，用于批量操作避免 N+1 查询）
    /// </summary>
    Task<List<Conversation>> GetByIdsAsync(IEnumerable<int> ids);

    /// <summary>
    /// 按 ID 列表批量获取会话（追踪查询，仅加载 Conversation 实体本身，用于批量更新）
    /// </summary>
    Task<List<Conversation>> GetByIdsForUpdateAsync(IEnumerable<int> ids);

    /// <summary>
    /// 按 ID 列表批量获取会话（追踪查询，含 ConversationFolders，用于批量分配/状态更新）
    /// </summary>
    Task<List<Conversation>> GetByIdsWithFoldersForUpdateAsync(IEnumerable<int> ids);

    /// <summary>
    /// 按 ID 获取会话（追踪查询，仅加载 Conversation 实体本身，用于更新）
    /// </summary>
    Task<Conversation?> GetByIdForUpdateAsync(int id);

    Task<IEnumerable<Conversation>> GetByMailboxIdAsync(int mailboxId, int page, int pageSize);
    Task<IEnumerable<Conversation>> GetByFolderIdAsync(int folderId, int page, int pageSize);
    /// <summary>
    /// M-7：按用户 ID 分页获取会话，返回 (Items, Total) 元组以支持前端分页总数显示。
    /// </summary>
    Task<(IEnumerable<Conversation> Items, int Total)> GetByUserIdAsync(int userId, int page, int pageSize);
    /// <summary>
    /// M-7：按客户 ID 分页获取会话，返回 (Items, Total) 元组以支持前端分页总数显示。
    /// </summary>
    Task<(IEnumerable<Conversation> Items, int Total)> GetByCustomerIdAsync(int customerId, int page, int pageSize);
    /// <summary>
    /// 获取所有未逻辑删除的会话（用于"全部邮箱"视图），按最后回复时间倒序。
    /// </summary>
    Task<(IEnumerable<Conversation> Items, int Total)> GetAllActiveAsync(int page, int pageSize);
    /// <summary>
    /// 获取已软删除的会话（用于垃圾箱视图），按更新时间倒序。
    /// mailboxId > 0 时按邮箱过滤，mailboxId <= 0 时返回所有邮箱的已删除会话。
    /// </summary>
    Task<(IEnumerable<Conversation> Items, int Total)> GetDeletedByMailboxIdAsync(int mailboxId, int page, int pageSize);
    /// <summary>
    /// 按 ID 获取已软删除的会话（含完整 Includes，AsNoTracking）。
    /// </summary>
    Task<Conversation?> GetDeletedByIdAsync(int id);

    /// <summary>
    /// 按 ID 获取已软删除的会话（追踪查询，仅加载 Conversation 实体本身，用于恢复更新）。
    /// </summary>
    Task<Conversation?> GetDeletedTrackedByIdAsync(int id);
    /// <summary>
    /// 彻底删除已软删除的会话（清空垃圾箱）。
    /// mailboxId > 0 时只删除该邮箱的，mailboxId <= 0 时删除所有邮箱的。
    /// 返回被删除的会话数量。
    /// </summary>
    Task<int> HardDeleteAllDeletedByMailboxIdAsync(int mailboxId);
    /// <summary>
    /// 彻底删除单个已软删除的会话。
    /// </summary>
    Task HardDeleteAsync(int id);
    Task<IEnumerable<Conversation>> SearchAsync(string query, int page, int pageSize);
    Task<(IEnumerable<Conversation> Items, int Total)> AdvancedSearchAsync(
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
        int? readerUserId = null);

    /// <summary>
    /// 批量获取指定用户对一组会话的最后阅读时间。
    /// 用于列表页计算每个会话是否有未读消息（LastReplyAt > LastReadAt 或无记录视为未读）。
    /// </summary>
    Task<Dictionary<int, DateTime>> GetLastReadAtMapAsync(IEnumerable<int> conversationIds, int userId);

    /// <summary>
    /// 更新（或插入）指定用户对某会话的最后阅读时间为当前 UTC 时间。
    /// 进入会话详情时调用，使列表页未读徽章消失。
    /// </summary>
    Task UpsertReadStateAsync(int conversationId, int userId);
    Task<int> CountByStatusAsync(ConversationStatus status);
    Task<int> CountByMailboxIdAndStatusAsync(int mailboxId, ConversationStatus status);
    Task<int> CountByFolderIdAsync(int folderId);
    Task<int> CountByUserIdAndStatusAsync(int userId, ConversationStatus status);
    Task<int> CountCreatedInDateRangeAsync(DateTime startDate, DateTime endDate, int? mailboxId = null);
    Task<int> CountClosedInDateRangeAsync(DateTime startDate, DateTime endDate, int? mailboxId = null);
    Task<int> CountThreadRepliesByUserIdInDateRangeAsync(int userId, DateTime startDate, DateTime endDate);
    Task<int> CountUnassignedActiveAsync();
    Task<int> CountActiveByCustomerIdAsync(int customerId);
    /// <summary>
    /// 批量统计多个客户的活跃会话数量（仅统计未软删除会话），用于批量删除前置校验，避免 N+1。
    /// </summary>
    Task<Dictionary<int, int>> CountActiveByCustomerIdsAsync(IEnumerable<int> customerIds);
    /// <summary>
    /// 统计指定客户在垃圾箱中（已软删除）的会话数量，用于客户硬删除前置校验。
    /// </summary>
    Task<int> CountDeletedByCustomerIdAsync(int customerId);
    /// <summary>
    /// 批量统计多个客户在垃圾箱中（已软删除）的会话数量，用于清空垃圾箱前置校验，避免 N+1。
    /// </summary>
    Task<Dictionary<int, int>> CountDeletedByCustomerIdsAsync(IEnumerable<int> customerIds);
    Task<int> CountActiveByUserIdAsync(int userId);
    Task<Dictionary<int, int>> GetCountByCustomerIdsAsync(IEnumerable<int> customerIds);

    /// <summary>
    /// 软删除指定客户的所有未删除会话（级联软删除）。
    /// </summary>
    Task<int> SoftDeleteByCustomerIdAsync(int customerId);
    /// <summary>
    /// 批量软删除多个客户的所有未删除会话（级联软删除），用一条 ExecuteUpdate 完成，避免 N+1。
    /// 返回级联软删除的会话总条数。
    /// </summary>
    Task<int> SoftDeleteByCustomerIdsAsync(IEnumerable<int> customerIds);
    /// <summary>
    /// 恢复指定客户的所有已软删除会话（级联恢复）。
    /// </summary>
    Task<int> RestoreByCustomerIdAsync(int customerId);
    Task<Dictionary<DateTime, (int NewCount, int ClosedCount)>> GetTrendAsync(DateTime startDate, DateTime endDate, int? mailboxId = null);
    Task<IEnumerable<ConversationThread>> GetThreadsByConversationIdAsync(int conversationId);

    Task<Dictionary<int, int>> GetActiveCountByUserIdAsync();
    Task<Dictionary<int, int>> GetClosedCountByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Dictionary<int, int>> GetReplyCountByUserIdAndDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Dictionary<int, (int Active, int Pending, int Closed)>> GetStatusCountsByMailboxIdAsync();

    Task<Dictionary<ConversationStatus, int>> GetStatusDistributionAsync(int? mailboxId = null);
    Task<int> CountThreadRepliesInDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<double> AverageResolutionHoursAsync(DateTime startDate, DateTime endDate, int? mailboxId = null);
    Task<Dictionary<int, (int AssignedCount, int ReplyCount, int ClosedCount, double AvgFirstResponseHours)>> GetUserPerformanceAsync(DateTime startDate, DateTime endDate, int? mailboxId = null);
    Task<Dictionary<int, (int NewCount, int ClosedCount, int ActiveCount)>> GetMailboxLoadAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// 按状态分页获取会话（用于 SLA 扫描等后台任务）
    /// </summary>
    Task<(IEnumerable<Conversation> Items, int Total)> GetPagedByStatusesAsync(
        int page,
        int pageSize,
        IEnumerable<ConversationStatus> statuses);

    /// <summary>
    /// 获取下一个可用的会话编号（Max(Number) + 1，仅统计未软删除记录）。
    /// Number 字段有唯一索引（过滤已软删除记录），新增会话时必须调用此方法避免唯一约束冲突。
    /// </summary>
    Task<int> GetNextNumberAsync();

    /// <summary>
    /// 按会话编号（Number，面向用户引用的 [#数字] 标记）获取未软删除的会话（含完整 Includes，AsNoTracking）。
    /// 用于邮件回复时通过主题标记匹配现有会话。
    /// </summary>
    Task<Conversation?> GetByNumberAsync(int number);

    /// <summary>
    /// 获取指定会话的所有文件夹关联（追踪查询）。
    /// 用于文件夹同步逻辑，不依赖 Conversation.ConversationFolders 导航属性是否已加载，
    /// 避免会话查询时未 Include ConversationFolders 导致集合为空、旧关联不删除、新关联重复 INSERT 触发 PK 冲突（T000060）。
    /// </summary>
    Task<List<ConversationFolder>> GetConversationFoldersTrackedAsync(int conversationId);

    /// <summary>
    /// 删除指定的会话-文件夹关联（追踪实体，需配合 SaveChangesAsync 持久化）。
    /// </summary>
    void RemoveConversationFolder(ConversationFolder folder);

    /// <summary>
    /// 按客户渠道 ID (wa_id) 查找最近指定时间窗口内未关闭的 WhatsApp 会话。
    /// 用于 WhatsApp Webhook 入站消息的会话匹配（24 小时窗口）。
    /// </summary>
    /// <param name="waId">客户 WhatsApp 号码（含国家区号）</param>
    /// <param name="within">时间窗口（如 24 小时）</param>
    /// <returns>匹配的会话；未找到返回 null</returns>
    Task<Conversation?> FindActiveWhatsAppConversationByWaIdAsync(string waId, TimeSpan within);

    /// <summary>
    /// 按钉钉用户 ID（senderStaffId）查找最近指定时间窗口内未关闭的钉钉会话。
    /// 用于钉钉 Webhook 入站消息的会话匹配（72 小时窗口）。
    /// </summary>
    /// <param name="staffId">钉钉发送者 userId（senderStaffId）</param>
    /// <param name="within">时间窗口（如 72 小时）</param>
    /// <returns>匹配的会话；未找到返回 null</returns>
    Task<Conversation?> FindActiveDingTalkConversationByUserIdAsync(string staffId, TimeSpan within);

    /// <summary>
    /// 按 Messenger PSID 查找最近指定时间窗口内未关闭的 Messenger 会话。
    /// 用于 Messenger Webhook 入站消息的会话匹配（默认 24 小时窗口，与平台客服窗口一致）。
    /// </summary>
    /// <param name="psid">客户在本 Page 内的 PSID（Customer.Channel="messenger"）</param>
    /// <param name="within">时间窗口</param>
    Task<Conversation?> FindActiveMessengerConversationByChannelIdAsync(string psid, TimeSpan within);

    /// <summary>
    /// 按 Telegram chat_id 查找最近指定时间窗口内未关闭的 Telegram 会话。
    /// </summary>
    /// <param name="chatId">Telegram chat.id（字符串形态，超级群为 -100… 前缀）</param>
    /// <param name="within">时间窗口</param>
    Task<Conversation?> FindActiveTelegramConversationByChatIdAsync(string chatId, TimeSpan within);

    /// <summary>
    /// 按 LINE userId 查找最近指定时间窗口内未关闭的 LINE 会话。
    /// </summary>
    /// <param name="userId">LINE 用户 id（U 开头的 33 位字符串）</param>
    /// <param name="within">时间窗口</param>
    Task<Conversation?> FindActiveLineConversationByUserIdAsync(string userId, TimeSpan within);

    /// <summary>
    /// 按 iMessage 作用域化标识查找最近指定时间窗口内未关闭的 iMessage 会话。
    /// ChannelId 已按 (mode, external_id) 作用域化（AMB 不透明 id 与 bridge handle/GUID 命名空间不同），
    /// 调用方需传入 IMessageIdentity.BuildScopedChannelId 的结果。
    /// </summary>
    /// <param name="scopedChannelId">作用域化后的 ChannelId</param>
    /// <param name="within">时间窗口</param>
    Task<Conversation?> FindActiveIMessageConversationAsync(string scopedChannelId, TimeSpan within);

    /// <summary>
    /// 批量更新会话优先级（使用 ExecuteUpdateAsync，不经过 ChangeTracker，单次 SQL）。
    /// 返回受影响的行数。
    /// </summary>
    /// <param name="conversationIds">目标会话 ID 列表</param>
    /// <param name="priority">目标优先级</param>
    /// <returns>实际更新的行数</returns>
    Task<int> BulkUpdatePriorityAsync(IEnumerable<int> conversationIds, ConversationPriority priority);

    /// <summary>
    /// 批量软删除会话（使用 ExecuteUpdateAsync，不经过 ChangeTracker，单次 SQL）。
    /// 返回受影响的行数。
    /// </summary>
    /// <param name="conversationIds">目标会话 ID 列表</param>
    /// <returns>实际软删除的行数</returns>
    Task<int> BulkSoftDeleteAsync(IEnumerable<int> conversationIds);
}
