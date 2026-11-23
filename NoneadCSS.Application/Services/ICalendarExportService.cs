using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface ICalendarExportService
{
    /// <summary>
    /// 导出指定事件为 ICS 字符串
    /// </summary>
    Task<string> ExportEventAsync(int eventId);

    /// <summary>
    /// 批量导出事件为 ICS 字符串
    /// </summary>
    /// <param name="userId">可选：限定导出该用户的事件（订阅 Feed 时必须传入以防数据泄露）</param>
    Task<string> ExportRangeAsync(DateTime startDate, DateTime endDate, int? mailboxId, int? userId = null);

    /// <summary>
    /// 供订阅源（Feed）使用：直接按指定 userId 查询事件，绕过 GetListAsync 的权限交集过滤。
    /// 匿名访问 Feed 时 _currentUserAccessor.UserId 为 0，权限交集为空会导致返回空列表，
    /// 此方法避免该问题，仅在已通过 Token 鉴权的前提下调用。
    /// </summary>
    /// <param name="userId">订阅令牌解析出的用户 ID，仅导出该用户的事件</param>
    Task<string> ExportRangeForFeedAsync(DateTime startDate, DateTime endDate, int userId);

    /// <summary>
    /// 从 ICS 字符串导入事件
    /// </summary>
    Task<(int Imported, int Skipped)> ImportAsync(string icsContent, int? mailboxId);
}
