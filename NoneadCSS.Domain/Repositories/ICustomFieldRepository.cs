using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ICustomFieldRepository : IRepository<CustomField>
{
    /// <summary>
    /// 获取启用中的自定义字段（按对象类型 + 渠道筛选，包含全局字段）。
    /// sourceType 传入会话来源（如 "wechat"）时返回：全局字段 + 该渠道专属字段；
    /// sourceType 为 null/空时返回：所有字段（含其他渠道专属字段），主要用于字段管理页。
    /// </summary>
    Task<List<CustomField>> GetActiveForEntityAsync(string entityType, string? sourceType = null);

    Task<(IEnumerable<CustomField> Items, int Total)> GetPagedAsync(
        int page, int pageSize,
        string? entityType = null, int? mailboxId = null, bool? isActive = null, string? keyword = null);

    Task<List<CustomField>> GetAllWithMailboxAsync();
    Task<Dictionary<string, int>> GetStatsAsync();

    Task<CustomField?> GetDeletedByIdAsync(int id);
    Task<(IEnumerable<CustomField> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}

public interface ICustomFieldValueRepository : IRepository<CustomFieldValue>
{
    /// <summary>
    /// 获取某个对象的所有自定义字段值。
    /// </summary>
    Task<List<CustomFieldValue>> GetByEntityAsync(string entityType, int entityId);

    /// <summary>
    /// 批量获取多个对象的自定义字段值。
    /// </summary>
    Task<Dictionary<int, List<CustomFieldValue>>> GetByEntityIdsAsync(string entityType, IEnumerable<int> entityIds);

    /// <summary>
    /// 获取某个对象的指定字段值。
    /// </summary>
    Task<CustomFieldValue?> GetByFieldAndEntityAsync(int customFieldId, string entityType, int entityId);

    /// <summary>
    /// 删除某个对象的所有自定义字段值。
    /// </summary>
    Task DeleteByEntityAsync(string entityType, int entityId);
}
