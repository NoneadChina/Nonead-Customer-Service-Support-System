using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

/// <summary>
/// 快捷回复模板仓储接口
/// </summary>
public interface ICannedResponseRepository : IRepository<CannedResponse>
{
    /// <summary>
    /// 获取指定邮箱可见的模板（共享 + 个人）
    /// </summary>
    Task<List<CannedResponse>> GetByMailboxAsync(int? mailboxId, int? userId = null);

    /// <summary>
    /// 按分组获取模板
    /// </summary>
    Task<List<CannedResponse>> GetByCategoryAsync(string category, int? mailboxId = null);

    /// <summary>
    /// 搜索模板（按名称和正文）
    /// </summary>
    Task<List<CannedResponse>> SearchAsync(string keyword, int? mailboxId = null, int? userId = null);

    /// <summary>
    /// 获取所有分类名称
    /// </summary>
    Task<List<string>> GetAllCategoriesAsync(int? mailboxId = null);

    /// <summary>
    /// 分页获取已删除（垃圾箱）的模板
    /// </summary>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="keyword">搜索关键词（匹配名称、正文、分类）</param>
    /// <returns>已删除模板列表及总数</returns>
    Task<(IEnumerable<CannedResponse> Items, int Total)> GetDeletedAsync(int page, int pageSize, string? keyword = null);

    /// <summary>
    /// 获取已删除模板的数量
    /// </summary>
    Task<int> CountDeletedAsync();

    /// <summary>
    /// 根据 ID 获取已删除的模板（绕过软删除过滤器，用于权限校验）
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>已删除的模板实体，若不存在或未删除则返回 null</returns>
    Task<CannedResponse?> GetDeletedByIdAsync(int id);

    /// <summary>
    /// 恢复已删除的模板
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>是否恢复成功</returns>
    Task<bool> RestoreAsync(int id);

    /// <summary>
    /// 彻底删除（物理删除）已删除的模板
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>是否删除成功</returns>
    Task<bool> HardDeleteAsync(int id);

    /// <summary>
    /// 清空垃圾箱：物理删除所有已删除的模板
    /// </summary>
    /// <returns>删除的模板数量</returns>
    Task<int> HardDeleteAllDeletedAsync();
}
