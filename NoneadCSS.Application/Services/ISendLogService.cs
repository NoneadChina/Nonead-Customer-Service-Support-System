using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 邮件发送日志服务接口
/// </summary>
public interface ISendLogService
{
    /// <summary>分页查询发送日志</summary>
    Task<PagedResponse<SendLogDto>> GetListAsync(
        int page,
        int pageSize,
        int? status = null,
        int? mailType = null,
        int? mailboxId = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? keyword = null);

    /// <summary>获取单个日志详情</summary>
    Task<SendLogDto?> GetByIdAsync(int id);

    /// <summary>获取总览统计（含统计、邮箱分布、趋势、最近失败）</summary>
    Task<SendLogOverviewResponse> GetOverviewAsync(DateTime? startDate = null, DateTime? endDate = null, int? mailboxId = null);

    /// <summary>获取最近失败的发送记录</summary>
    Task<List<SendLogDto>> GetRecentFailuresAsync(int count = 10);

    /// <summary>获取发送状态选项</summary>
    Task<Dictionary<int, string>> GetStatusOptionsAsync();

    /// <summary>获取邮件类型选项</summary>
    Task<Dictionary<int, string>> GetMailTypeOptionsAsync();
}
