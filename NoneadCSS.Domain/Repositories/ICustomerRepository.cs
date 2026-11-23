using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<Customer?> GetByEmailAsync(string email);
    Task<bool> ExistsByEmailAsync(string email, int? excludeCustomerId = null);
    /// <summary>
    /// 检查邮箱是否已被任意客户占用（包括已软删除的客户）。
    /// 用于 enrich 时避免 Emails.EmailAddress 全局唯一约束冲突。
    /// </summary>
    Task<bool> IsEmailOwnedByAnyCustomerAsync(string email, int? excludeCustomerId = null);
    Task<List<Customer>> GetByIdsAsync(IEnumerable<int> ids);
    Task<int> CountCreatedInDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<(IEnumerable<Customer> Items, int Total)> SearchAsync(string query, int page, int pageSize, int[]? accessibleMailboxIds = null);
    Task<Customer?> GetOrCreateByEmailAsync(string email, string? firstName = null, string? lastName = null);
    /// <summary>
    /// 根据电话号码查找客户（只读，AsNoTracking）。
    /// </summary>
    Task<Customer?> GetByPhoneAsync(string phoneNumber);
    /// <summary>
    /// 根据电话号码查找客户（tracking，适用于后续修改 + SaveChanges 的场景）。
    /// </summary>
    Task<Customer?> GetByPhoneTrackedAsync(string phoneNumber);
    /// <summary>
    /// 按渠道 + 渠道 ID 查找客户（只读，AsNoTracking，不创建）。
    /// </summary>
    Task<Customer?> GetByChannelAsync(string channel, string channelId);
    /// <summary>
    /// 按渠道 + 渠道 ID 查找客户（tracking，适用于后续修改 + SaveChanges 的场景，不创建）。
    /// </summary>
    Task<Customer?> GetByChannelTrackedAsync(string channel, string channelId);

    /// <summary>
    /// 按渠道标识（如微信 OpenId）获取或创建客户。
    /// channel 为渠道名（如 "wechat"），channelId 为渠道内唯一标识（如 OpenId）。
    /// </summary>
    Task<Customer?> GetOrCreateByChannelAsync(string channel, string channelId, string? displayName = null);
    Task<(IEnumerable<Customer> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? query = null);
    Task<Customer?> GetDeletedByIdAsync(int id);
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();

    /// <summary>
    /// 获取所有未删除客户的所有邮箱地址（无 Take 限制，用于导入重复检查）。
    /// </summary>
    Task<HashSet<string>> GetAllActiveEmailsAsync();
}
