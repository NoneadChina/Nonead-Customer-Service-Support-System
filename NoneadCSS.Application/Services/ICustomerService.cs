using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ICustomerService
{
    Task<PagedResponse<CustomerResponse>> SearchCustomersAsync(string query, int page, int pageSize);
    Task<PagedResponse<CustomerResponse>> SearchCustomersByMailboxIdsAsync(string query, List<int> mailboxIds, int page, int pageSize);
    Task<CustomerResponse?> GetCustomerByIdAsync(int id);
    Task<(CustomerResponse? Customer, string? ErrorMessage)> CreateCustomerAsync(CreateCustomerRequest request);
    Task<(CustomerResponse? Customer, string? ErrorMessage)> UpdateCustomerAsync(int id, CreateCustomerRequest request, string? ifMatch = null);
    Task<(bool Success, string? ErrorMessage)> DeleteCustomerAsync(int id);
    Task<PagedResponse<CustomerResponse>> GetTrashAsync(int page, int pageSize, string? query = null);
    Task<bool> RestoreCustomerAsync(int id);
    /// <summary>
    /// 查询指定客户硬删除前的关联会话状态（活跃会话数、垃圾箱会话数），供前端展示提示。
    /// </summary>
    Task<(int ActiveCount, int TrashCount)> GetHardDeleteStatusAsync(int customerId);
    Task<(bool Success, string? ErrorMessage)> HardDeleteCustomerAsync(int id);
    /// <summary>
    /// 查询清空垃圾箱前的关联会话状态（垃圾箱客户数、活跃会话总数、垃圾箱会话总数），供前端展示提示。
    /// </summary>
    Task<(int CustomerCount, int TotalActiveCount, int TotalTrashCount)> GetClearTrashStatusAsync();
    Task<(int Count, string? ErrorMessage)> ClearTrashAsync();
    Task<BulkOperationResponse> BulkDeleteAsync(List<int> ids);
    Task<(byte[] Content, string FileName)> ExportAsync(List<int>? ids = null);
}
