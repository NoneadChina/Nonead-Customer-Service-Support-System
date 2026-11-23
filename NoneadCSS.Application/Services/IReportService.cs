using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 报表服务接口 - 提供综合运营数据统计与分析
/// </summary>
public interface IReportService
{
    /// <summary>
    /// 获取综合报表
    /// </summary>
    /// <param name="startDate">开始日期（含）</param>
    /// <param name="endDate">结束日期（不含）</param>
    /// <param name="mailboxId">可选邮箱筛选</param>
    /// <returns>综合报表数据</returns>
    Task<ReportResponse> GetReportAsync(DateTime startDate, DateTime endDate, int? mailboxId = null);

    /// <summary>
    /// 分页获取客服绩效报表（用于独立表格展示，避免综合报表截断客服数据）
    /// </summary>
    /// <param name="startDate">开始日期（含）</param>
    /// <param name="endDate">结束日期（不含）</param>
    /// <param name="mailboxId">可选邮箱筛选</param>
    /// <param name="page">页码，从1开始</param>
    /// <param name="pageSize">每页条数，按 PaginationHelper 夹取</param>
    /// <returns>分页后的客服绩效数据 + 总数</returns>
    Task<(List<ReportUserPerformanceDto> Items, int TotalCount)> GetUserPerformancePagedAsync(
        DateTime startDate, DateTime endDate, int? mailboxId, int page, int pageSize);
}
