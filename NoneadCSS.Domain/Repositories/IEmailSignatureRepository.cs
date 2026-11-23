using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IEmailSignatureRepository : IRepository<EmailSignature>
{
    Task<List<EmailSignature>> GetByUserIdAsync(int userId);
    Task<List<EmailSignature>> GetByUserIdAndMailboxIdAsync(int userId, int? mailboxId);
    Task<EmailSignature?> GetDefaultByUserIdAsync(int userId, int? mailboxId = null);
    Task<(IEnumerable<EmailSignature> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        int? userId = null, int? mailboxId = null,
        bool? isDefault = null, string? keyword = null);
    Task SetDefaultAsync(int userId, int? mailboxId, int signatureId);

    /// <summary>
    /// 获取已删除（垃圾箱）的签名列表（分页）
    /// </summary>
    Task<(IEnumerable<EmailSignature> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null);

    /// <summary>
    /// 获取已删除的签名数量
    /// </summary>
    Task<int> CountDeletedAsync();

    /// <summary>
    /// 恢复已删除的签名
    /// </summary>
    Task<bool> RestoreAsync(int id);

    /// <summary>
    /// 物理删除指定签名（仅允许删除已软删除的）
    /// </summary>
    Task<bool> HardDeleteAsync(int id);

    /// <summary>
    /// 物理删除所有已软删除的签名，返回删除数量
    /// </summary>
    Task<int> HardDeleteAllDeletedAsync();
}
