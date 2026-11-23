using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IEmailSignatureService
{
    Task<(IEnumerable<EmailSignatureDto> Items, int Total)> GetPagedAsync(EmailSignatureQuery query);
    Task<EmailSignatureDto?> GetByIdAsync(int id);
    Task<EmailSignatureDto> CreateAsync(int userId, CreateEmailSignatureRequest request);
    Task<EmailSignatureDto?> UpdateAsync(int id, UpdateEmailSignatureRequest request);
    Task<bool> DeleteAsync(int id);
    Task<EmailSignatureOverviewResponse> GetOverviewAsync();
    Task<List<EmailSignatureDto>> GetByUserIdAsync(int userId, int? mailboxId = null);
    Task<EmailSignatureDto?> GetDefaultAsync(int userId, int? mailboxId = null);
    Task<bool> SetDefaultAsync(int userId, int id);

    /// <summary>获取垃圾箱中的签名列表（分页）</summary>
    Task<(IEnumerable<EmailSignatureDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null);

    /// <summary>获取垃圾箱中的签名数量</summary>
    Task<int> GetTrashCountAsync();

    /// <summary>从垃圾箱恢复签名</summary>
    Task<bool> RestoreAsync(int id);

    /// <summary>从垃圾箱彻底删除指定签名</summary>
    Task<bool> HardDeleteAsync(int id);

    /// <summary>清空垃圾箱，返回彻底删除的数量</summary>
    Task<int> ClearTrashAsync();
}
