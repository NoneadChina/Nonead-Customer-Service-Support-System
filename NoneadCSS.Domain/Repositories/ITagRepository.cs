using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ITagRepository : IRepository<Tag>
{
    Task<IEnumerable<Tag>> GetByMailboxIdAsync(int? mailboxId = null);
    Task<Tag?> GetByNameAsync(string name, int? mailboxId = null);
    Task<IEnumerable<Tag>> GetByConversationIdAsync(int conversationId);
    Task<IEnumerable<Tag>> GetByIdsAsync(IEnumerable<int> ids);
    Task<(IEnumerable<Tag> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? query = null);
    Task<int> CountDeletedAsync();
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();

    /// <summary>
    /// 批量获取指定标签集合的会话使用计数（单次 GroupBy 查询，避免 N+1）。
    /// 返回的字典仅包含存在关联记录的 TagId；调用方对未出现的 TagId 应视为 0。
    /// </summary>
    Task<Dictionary<int, int>> GetTagCountsAsync(IEnumerable<int> tagIds);

    /// <summary>
    /// 获取单个标签的会话使用计数（翻译为 SQL COUNT，不再加载整表到内存）。
    /// </summary>
    Task<int> GetTagCountAsync(int tagId);
}
