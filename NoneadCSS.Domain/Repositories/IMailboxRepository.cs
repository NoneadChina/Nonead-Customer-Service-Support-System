using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IMailboxRepository : IRepository<Mailbox>
{
    /// <summary>
    /// 按 ID 列表批量获取邮箱（避免 N+1 查询）
    /// </summary>
    Task<List<Mailbox>> GetByIdsAsync(IEnumerable<int> ids);

    Task<IEnumerable<Mailbox>> GetByUserIdAsync(int userId);
    Task<IEnumerable<Mailbox>> GetActiveAsync();

    /// <summary>
    /// 获取所有活跃邮箱（tracking，适用于后续修改 + SaveChanges 的场景，如批量更新 LastFetchAt）
    /// </summary>
    Task<IEnumerable<Mailbox>> GetActiveTrackedAsync();

    /// <summary>
    /// 获取第一个活跃邮箱（用于系统通知/密码变更通知等场景，避免 GetAllAsync 全表扫描）
    /// </summary>
    Task<Mailbox?> GetFirstActiveAsync();

    Task<Mailbox?> GetByEmailAsync(string email);
    Task<bool> UserHasAccessAsync(int mailboxId, int userId);
    Task AddUserToMailboxAsync(int mailboxId, int userId);
    Task RemoveUserFromMailboxAsync(int mailboxId, int userId);

    Task<IEnumerable<Mailbox>> GetDeletedAsync();
    Task<Mailbox?> GetDeletedByIdAsync(int id);
    Task HardDeleteAsync(int id);
    Task HardDeleteAllDeletedAsync();
}
