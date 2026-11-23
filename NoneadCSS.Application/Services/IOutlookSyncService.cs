using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Outlook 日历双向同步服务接口
/// </summary>
public interface IOutlookSyncService
{
    /// <summary>
    /// 获取当前用户的同步设置
    /// </summary>
    Task<OutlookSyncSettingDto?> GetSettingAsync(int userId);

    /// <summary>
    /// 获取当前用户的同步状态（前端轮询使用）
    /// </summary>
    Task<OutlookSyncStatusDto> GetStatusAsync(int userId);

    /// <summary>
    /// 更新同步设置（启用/禁用、方向）
    /// </summary>
    Task<OutlookSyncSettingDto?> UpdateSettingAsync(int userId, UpdateOutlookSyncSettingRequest request);

    /// <summary>
    /// 断开 Outlook 连接（清除令牌，保留设置记录）
    /// </summary>
    Task<bool> DisconnectAsync(int userId);

    /// <summary>
    /// 触发一次同步（用户主动点击或定时任务调用）
    /// </summary>
    /// <param name="userId">目标用户 ID</param>
    /// <param name="force">是否强制同步（忽略最小间隔限制）</param>
    /// <param name="forceDeltaOnly">是否强制只走增量（true=若 DeltaToken 失效直接报错而非回退全量）</param>
    Task<OutlookSyncResultDto> SyncAsync(int userId, bool force = false, bool forceDeltaOnly = false);

    /// <summary>
    /// P3-D: 按 MailboxId 触发一次增量同步（手动 Delta 拉取）
    /// 从 mailboxId 反查关联用户后立即走一遍完整增量同步流程。
    /// </summary>
    /// <param name="mailboxId">目标邮箱 ID</param>
    /// <param name="currentUserId">当前操作的用户 ID（用于权限校验）</param>
    /// <param name="forceDeltaOnly">是否强制只走增量（true=若 DeltaToken 失效直接报错而非回退全量）</param>
    Task<OutlookSyncResultDto> SyncByMailboxIdAsync(int mailboxId, int currentUserId, bool forceDeltaOnly = false);

    /// <summary>
    /// 处理 OAuth 回调：用授权码换取令牌并保存
    /// </summary>
    /// <param name="code">授权码</param>
    /// <param name="state">state 参数（含 userId）</param>
    /// <returns>成功时返回 userId，失败返回 null</returns>
    Task<int?> HandleOAuthCallbackAsync(string code, string state);

    /// <summary>
    /// 批量同步所有启用了同步的用户（定时任务使用）
    /// </summary>
    Task<int> SyncAllEnabledUsersAsync();
}
