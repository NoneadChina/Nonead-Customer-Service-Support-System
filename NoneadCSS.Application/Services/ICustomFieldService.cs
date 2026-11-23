using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public interface ICustomFieldService
{
    Task<CustomFieldDto?> GetByIdAsync(int id);
    /// <summary>
    /// 获取启用中的字段：传入 sourceType（如 "wechat"）时返回全局字段 + 该渠道专属字段，
    /// 并按 HideWhenChannelDisabled 规则过滤已关闭渠道的字段。
    /// </summary>
    Task<List<CustomFieldDto>> GetActiveForEntityAsync(string entityType, string? sourceType = null);
    Task<(List<CustomFieldDto> Items, int Total)> GetPagedAsync(CustomFieldQuery query);
    Task<CustomFieldOverviewResponse> GetOverviewAsync();
    Task<CustomFieldDto> CreateAsync(CreateCustomFieldRequest request);
    Task<CustomFieldDto?> UpdateAsync(UpdateCustomFieldRequest request);
    Task<bool> DeleteAsync(int id);
    Task<bool> ToggleActiveAsync(int id);

    /// <summary>获取可用渠道列表（含启用状态），用于字段管理页"适用渠道"下拉。仅返回已启用渠道 + 全局选项。</summary>
    Task<List<CustomFieldChannelDto>> GetChannelsAsync();

    Task<List<CustomFieldValueDto>> GetEntityValuesAsync(string entityType, int entityId);
    Task<CustomFieldValueDto?> SetEntityValueAsync(int customFieldId, string entityType, int entityId, string? value);
    Task<List<CustomFieldValueDto>> BatchSetEntityValuesAsync(string entityType, int entityId, Dictionary<int, string?> values);
    Task<bool> DeleteEntityValuesAsync(string entityType, int entityId);
    Task<(List<CustomFieldDto> Items, int Total)> GetTrashPagedAsync(int page, int pageSize, string? keyword);
    Task<CustomFieldDto?> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();
}
