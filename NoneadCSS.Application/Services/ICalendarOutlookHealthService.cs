using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// P3-D: Outlook 同步健康仪表盘服务接口
/// 按 UserId/MailboxId 返回同步健康聚合数据
/// </summary>
public interface ICalendarOutlookHealthService
{
    /// <summary>
    /// 获取指定 Mailbox 的同步健康仪表盘数据
    /// </summary>
    /// <param name="mailboxId">邮箱 ID</param>
    /// <param name="recentCount">返回最近 N 次同步历史，默认 10</param>
    /// <param name="historyDays">统计最近多少天的处理记录数，默认 7</param>
    Task<CalendarOutlookHealthDashboardDto?> GetMailboxHealthAsync(int mailboxId, int recentCount = 10, int historyDays = 7);

    /// <summary>
    /// 获取当前用户可访问的所有 Mailbox 的健康仪表盘列表
    /// </summary>
    /// <param name="userId">当前用户 ID</param>
    /// <param name="recentCount">每个邮箱返回最近 N 次同步历史，默认 10</param>
    /// <param name="historyDays">统计最近多少天的处理记录数，默认 7</param>
    Task<List<CalendarOutlookHealthDashboardDto>> GetUserMailboxesHealthAsync(int userId, int recentCount = 10, int historyDays = 7);
}
