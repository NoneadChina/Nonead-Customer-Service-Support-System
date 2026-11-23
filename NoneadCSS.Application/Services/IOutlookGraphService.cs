using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Microsoft Graph API 服务接口
/// 负责与 Outlook 日历进行 CRUD 交互
/// </summary>
public interface IOutlookGraphService
{
    /// <summary>
    /// 获取当前登录用户信息（邮箱、OID）
    /// </summary>
    Task<OutlookUserInfo?> GetUserInfoAsync(string accessToken);

    /// <summary>
    /// 创建 Outlook 日历事件
    /// </summary>
    /// <returns>Outlook 事件 ID</returns>
    Task<string?> CreateEventAsync(string accessToken, CalendarEvent calendarEvent);

    /// <summary>
    /// 更新 Outlook 日历事件
    /// </summary>
    Task<bool> UpdateEventAsync(string accessToken, string outlookEventId, CalendarEvent calendarEvent);

    /// <summary>
    /// 删除 Outlook 日历事件
    /// </summary>
    Task<bool> DeleteEventAsync(string accessToken, string outlookEventId);

    /// <summary>
    /// 获取指定时间范围内的 Outlook 事件列表
    /// </summary>
    Task<List<OutlookEventDto>> ListEventsAsync(string accessToken, DateTime startDateTime, DateTime endDateTime);

    /// <summary>
    /// 使用 Graph API delta query 增量拉取事件变更。
    /// </summary>
    /// <param name="accessToken">Graph API Access Token</param>
    /// <param name="startDate">时间窗口起始（首次 delta 查询使用）</param>
    /// <param name="endDate">时间窗口结束（首次 delta 查询使用）</param>
    /// <param name="deltaToken">上一次返回的 deltaLink / 跳过 token；null 时发起首次 delta 查询</param>
    /// <returns>
    ///   Events: 变更的事件列表（含被标记删除的，IsDeleted=true）
    ///   NewDeltaToken: 新的 delta token，可用于下次增量查询
    ///   IsDeltaExpired: 410 Gone / 404 NotFound 时为 true，调用方应回退到 null token 重新全量 delta
    /// </returns>
    Task<(List<OutlookEventDto> Events, string? NewDeltaToken, bool IsDeltaExpired)>
        ListEventsDeltaAsync(string accessToken, DateTime startDate, DateTime endDate, string? deltaToken);
}

/// <summary>
/// Outlook 用户信息
/// </summary>
public class OutlookUserInfo
{
    public string Email { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Outlook 事件 DTO（从 Graph API 返回）
/// </summary>
public class OutlookEventDto
{
    public string Id { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string? BodyPreview { get; set; }
    public string? BodyContentType { get; set; }
    public string? BodyContent { get; set; }

    /// <summary>
    /// 开始时间（ISO 8601）
    /// </summary>
    public DateTime Start { get; set; }

    /// <summary>
    /// 开始时区
    /// </summary>
    public string? StartTimeZone { get; set; }

    /// <summary>
    /// 结束时间（ISO 8601）
    /// </summary>
    public DateTime End { get; set; }

    public string? EndTimeZone { get; set; }
    public bool IsAllDay { get; set; }
    public string? Location { get; set; }
    public string? OnlineMeetingUrl { get; set; }

    /// <summary>
    /// Outlook 端最后修改时间（UTC）
    /// </summary>
    public DateTime LastModifiedDateTime { get; set; }

    /// <summary>
    /// 创建时间（UTC）
    /// </summary>
    public DateTime CreatedDateTime { get; set; }

    /// <summary>
    /// 是否删除（delta query @removed.reason=deleted 时为 true）
    /// </summary>
    public bool IsDeleted { get; set; }
}
