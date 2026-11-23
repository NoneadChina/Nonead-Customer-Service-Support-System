using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ISlaService
{
    // 策略管理
    Task<(IEnumerable<SlaPolicyDto> Items, int Total)> GetListAsync(
        int page, int pageSize, int? mailboxId = null, bool? isActive = null, string? keyword = null);

    Task<SlaPolicyDto?> GetByIdAsync(int id);
    Task<SlaPolicyDto> CreateAsync(CreateSlaPolicyRequest request);
    Task<SlaPolicyDto?> UpdateAsync(int id, UpdateSlaPolicyRequest request);
    Task<bool> DeleteAsync(int id);
    Task<bool?> ToggleActiveAsync(int id);

    // ====== 垃圾箱 ======
    /// <summary>获取已软删除的 SLA 策略（分页）</summary>
    Task<(IEnumerable<SlaPolicyDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null);

    /// <summary>获取已软删除的 SLA 策略数量</summary>
    Task<int> GetTrashCountAsync();

    /// <summary>恢复软删除的 SLA 策略</summary>
    Task<bool> RestoreAsync(int id);

    /// <summary>彻底删除指定 SLA 策略</summary>
    Task<bool> HardDeleteAsync(int id);

    /// <summary>清空垃圾箱（彻底删除所有已软删除的 SLA 策略）</summary>
    Task<int> ClearTrashAsync();

    // 违规管理
    Task<(IEnumerable<SlaViolationDto> Items, int Total)> GetViolationsAsync(
        int page, int pageSize,
        int? policyId = null, int? conversationId = null, string? violationType = null,
        string? status = null, DateTime? startDate = null, DateTime? endDate = null);

    Task<List<SlaViolationDto>> GetViolationsByConversationAsync(int conversationId);

    // 概览
    Task<SlaOverviewResponse> GetOverviewAsync();

    // 会话 SLA 状态
    Task<SlaConversationStatusResponse?> GetConversationStatusAsync(int conversationId);

    /// <summary>
    /// 扫描所有会话，检测 SLA 违规并更新状态。返回处理的会话数和新增违规数。
    /// </summary>
    Task<(int Scanned, int NewViolations, int Breached)> ScanViolationsAsync();

    /// <summary>
    /// 为指定会话应用 SLA 策略（创建违规预警记录）。
    /// </summary>
    Task ApplyToConversationAsync(int conversationId);
}
