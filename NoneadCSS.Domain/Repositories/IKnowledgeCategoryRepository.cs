using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IKnowledgeCategoryRepository
{
    Task<KnowledgeCategory?> GetByIdAsync(int id);
    Task<IReadOnlyList<KnowledgeCategory>> GetListAsync(int? parentId = null, int? mailboxId = null, bool? isActive = null, string? keyword = null);
    Task<IReadOnlyList<KnowledgeCategory>> GetAllAsync();
    Task<int> CountAsync(int? parentId = null, int? mailboxId = null, bool? isActive = null, string? keyword = null);
    Task AddAsync(KnowledgeCategory category);
    void Update(KnowledgeCategory category);
    void Remove(KnowledgeCategory category);
    Task<int> CountArticlesAsync(int categoryId);
    Task<Dictionary<int, int>> GetArticleCountsByCategoryIdsAsync(IEnumerable<int> categoryIds);
    /// <summary>
    /// 将指定分类下未删除文章的 CategoryId 置 null（解除关联）。
    /// 用于删除/硬删除分类前清理活跃文章引用，防止悬空外键。
    /// </summary>
    Task UnlinkArticlesOfCategoryAsync(int categoryId);

    // 垃圾箱
    Task<KnowledgeCategory?> GetDeletedByIdAsync(int id);
    Task<(IReadOnlyList<KnowledgeCategory> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task RestoreAsync(int id);
    Task RestoreWithParentResetAsync(int id);
    Task HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}
