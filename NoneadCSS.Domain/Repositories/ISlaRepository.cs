using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ISlaPolicyRepository : IRepository<SlaPolicy>
{
    /// <summary>
    /// 获取启用中的 SLA 策略（按邮箱筛选，包含"所有邮箱"的全局策略），按优先级降序排序。
    /// </summary>
    Task<List<SlaPolicy>> GetActiveForMailboxAsync(int mailboxId);

    /// <summary>
    /// 获取某个会话适用的 SLA 策略（按邮箱匹配，取优先级最高）。
    /// </summary>
    Task<SlaPolicy?> GetPolicyForConversationAsync(int mailboxId);

    /// <summary>
    /// 批量获取多个邮箱适用的 SLA 策略（按邮箱匹配，每个邮箱取优先级最高的策略）。
    /// 包含"所有邮箱"的全局策略。返回字典按 MailboxId 索引。
    /// </summary>
    Task<Dictionary<int, SlaPolicy>> GetPoliciesForMailboxesAsync(IEnumerable<int> mailboxIds);

    Task<(IEnumerable<SlaPolicy> Items, int Total)> GetPagedAsync(
        int page, int pageSize, int? mailboxId = null, bool? isActive = null, string? keyword = null);

    Task<List<SlaPolicy>> GetAllWithMailboxAsync();
    Task<Dictionary<string, int>> GetStatsAsync();

    /// <summary>
    /// 获取已软删除的 SLA 策略（分页）。
    /// </summary>
    Task<(IEnumerable<SlaPolicy> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null);

    /// <summary>
    /// 获取已软删除的 SLA 策略总数。
    /// </summary>
    Task<int> CountDeletedAsync();

    /// <summary>
    /// 获取已软删除的 SLA 策略（按 ID）。
    /// </summary>
    Task<SlaPolicy?> GetDeletedByIdAsync(int id);

    /// <summary>
    /// 软删除：标记 DeletedAt。
    /// </summary>
    Task SoftDeleteAsync(SlaPolicy entity);

    /// <summary>
    /// 恢复软删除的 SLA 策略。
    /// </summary>
    Task RestoreAsync(SlaPolicy entity);

    /// <summary>
    /// 彻底删除指定 ID 的 SLA 策略（物理删除）。
    /// </summary>
    Task HardDeleteAsync(SlaPolicy entity);

    /// <summary>
    /// 彻底删除所有已软删除的 SLA 策略。返回删除数量。
    /// </summary>
    Task<int> HardDeleteAllDeletedAsync();
}

public interface ISlaViolationRepository : IRepository<SlaViolation>
{
    Task<SlaViolation?> GetByConversationAsync(int conversationId, string violationType);
    Task<List<SlaViolation>> GetByConversationAsync(int conversationId);

    /// <summary>
    /// 批量获取多个会话的现有 SLA 违规记录，按 (ConversationId, ViolationType) 索引。
    /// 每个 (会话, 违规类型) 只返回最新的一条（按 Id 降序，与 GetByConversationAsync(int, string) 语义一致）。
    /// </summary>
    Task<Dictionary<(int ConversationId, string ViolationType), SlaViolation>> GetByConversationIdsAsync(IEnumerable<int> conversationIds);

    Task<(IEnumerable<SlaViolation> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? policyId = null, int? conversationId = null, string? violationType = null,
        string? status = null, DateTime? startDate = null, DateTime? endDate = null);

    Task<List<SlaViolation>> GetActiveWarningsAsync();
    Task<List<SlaViolation>> GetBreachedAsync();
    Task<Dictionary<string, int>> GetStatsAsync(DateTime? start = null, DateTime? end = null);
    Task<List<SlaViolation>> GetRecentAsync(int limit = 20);
}
