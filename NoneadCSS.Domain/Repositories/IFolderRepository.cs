using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IFolderRepository : IRepository<Folder>
{
    Task<IEnumerable<Folder>> GetByMailboxIdAsync(int mailboxId);

    /// <summary>
    /// 获取邮箱下所有文件夹（tracking，适用于批量修改 + SaveChanges 的场景，如更新计数器）
    /// </summary>
    Task<IEnumerable<Folder>> GetByMailboxIdTrackedAsync(int mailboxId);

    Task<IEnumerable<Folder>> GetByUserIdAsync(int userId);
    Task<Folder?> GetByMailboxIdAndTypeAsync(int mailboxId, int type);
}
