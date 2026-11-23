using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 快捷回复模板服务接口
/// </summary>
public interface ICannedResponseService
{
    /// <summary>获取模板列表（按邮箱和用户过滤）</summary>
    Task<List<CannedResponseDto>> GetListAsync(int? mailboxId = null, int? userId = null, string? category = null);

    /// <summary>获取单个模板详情</summary>
    Task<CannedResponseDto?> GetByIdAsync(int id);

    /// <summary>搜索模板</summary>
    Task<List<CannedResponseDto>> SearchAsync(string keyword, int? mailboxId = null, int? userId = null);

    /// <summary>获取所有分类</summary>
    Task<List<string>> GetCategoriesAsync(int? mailboxId = null);

    /// <summary>创建模板</summary>
    Task<CannedResponseDto> CreateAsync(CreateCannedResponseRequest request, int createdByUserId);

    /// <summary>更新模板</summary>
    Task<CannedResponseDto?> UpdateAsync(int id, UpdateCannedResponseRequest request, int userId, bool isAdmin);

    /// <summary>删除模板（移至垃圾箱）</summary>
    Task<bool> DeleteAsync(int id, int userId, bool isAdmin);

    /// <summary>获取垃圾箱列表（分页）</summary>
    Task<(IEnumerable<CannedResponseDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null);

    /// <summary>获取垃圾箱中的模板数量</summary>
    Task<int> GetTrashCountAsync();

    /// <summary>恢复垃圾箱中的模板（需要权限校验）</summary>
    Task<bool> RestoreAsync(int id, int userId, bool isAdmin);

    /// <summary>彻底删除模板（需要权限校验）</summary>
    Task<bool> HardDeleteAsync(int id, int userId, bool isAdmin);

    /// <summary>清空垃圾箱（仅管理员）</summary>
    Task<int> ClearTrashAsync();
}
