#nullable enable
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services.Calendar;

public class CalendarAuthorizationService
{
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IUserRoleResolver _userRoleResolver;

    public CalendarAuthorizationService(ICurrentUserAccessor currentUserAccessor, IUserRoleResolver userRoleResolver)
    {
        ArgumentNullException.ThrowIfNull(currentUserAccessor);
        ArgumentNullException.ThrowIfNull(userRoleResolver);
        _currentUserAccessor = currentUserAccessor;
        _userRoleResolver = userRoleResolver;
    }

    public async Task<IEnumerable<int>?> ResolveAuthorizedUserIdsAsync(int currentUserId)
    {
        if (await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin).ConfigureAwait(false))
            return null;

        if (await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.AgentSupervisor).ConfigureAwait(false))
        {
            var result = new HashSet<int>();
            // T000092 修复：必须包含 Supervisor 自身 ID，否则 Supervisor 创建的事件（UserId=自己）
            // 会在查询时被 userIds 过滤掉，导致"显示添加成功但刷新后消失"。
            result.Add(currentUserId);
            var agentIds = await _userRoleResolver.GetUserIdsBySystemRoleTypeAsync(UserRoleType.Agent).ConfigureAwait(false);
            var viewerIds = await _userRoleResolver.GetUserIdsBySystemRoleTypeAsync(UserRoleType.ReadOnly).ConfigureAwait(false);
            foreach (var id in agentIds) result.Add(id);
            foreach (var id in viewerIds) result.Add(id);
            return result;
        }

        return new[] { currentUserId };
    }

    public async Task ValidateCreateAccessAsync(int? targetUserId)
    {
        var currentUserId = _currentUserAccessor.UserId;

        if (_currentUserAccessor.IsSuperAdmin)
            return;

        if (targetUserId == null || targetUserId == currentUserId)
        {
            if (await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.ReadOnly).ConfigureAwait(false))
                throw new UnauthorizedAccessException("只读用户无权创建日程");
            return;
        }

        if (await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.AgentSupervisor).ConfigureAwait(false))
        {
            var subordinateIds = await ResolveAuthorizedUserIdsAsync(currentUserId).ConfigureAwait(false);
            if (subordinateIds == null || !subordinateIds.Contains(targetUserId.Value))
                throw new UnauthorizedAccessException("无权为该用户创建日程");
            return;
        }

        if (await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.Agent).ConfigureAwait(false))
        {
            var targetRole = await _userRoleResolver.GetUserRoleTypeAsync(targetUserId.Value).ConfigureAwait(false);
            if (targetRole != UserRoleType.ReadOnly)
                throw new UnauthorizedAccessException("专员仅可向只读用户或自己分配任务");
            return;
        }

        throw new UnauthorizedAccessException("无权创建日程");
    }

    public async Task ValidateWriteAccessAsync(CalendarEvent existingEvent)
    {
        var currentUserId = _currentUserAccessor.UserId;

        if (_currentUserAccessor.IsSuperAdmin)
            return;

        if (existingEvent.CreatedBy == currentUserId)
            return;

        if (existingEvent.UserId == currentUserId)
            return;

        if (await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.AgentSupervisor).ConfigureAwait(false))
        {
            var subordinateIds = await ResolveAuthorizedUserIdsAsync(currentUserId).ConfigureAwait(false);
            if (subordinateIds != null && existingEvent.UserId.HasValue &&
                subordinateIds.Contains(existingEvent.UserId.Value))
                return;
        }

        throw new UnauthorizedAccessException("无权操作该日程");
    }

    public async Task<bool> CanAccessEventAsync(CalendarEventDto? evt)
    {
        if (evt == null) return false;
        var currentUserId = _currentUserAccessor.UserId;
        var authorizedUserIds = await ResolveAuthorizedUserIdsAsync(currentUserId).ConfigureAwait(false);
        if (authorizedUserIds == null) return true;
        if (!evt.UserId.HasValue) return false;
        return authorizedUserIds.Contains(evt.UserId.Value);
    }

    public async Task EnsureCanModifyAsync(CalendarEvent? existingEvent)
    {
        if (existingEvent == null)
            throw new KeyNotFoundException("事件不存在");
        await ValidateWriteAccessAsync(existingEvent).ConfigureAwait(false);
    }

    public void EnsureCanAccess(CalendarEventDto? evt)
    {
        if (evt == null)
            throw new KeyNotFoundException("事件不存在");
        var currentUserId = _currentUserAccessor.UserId;
        if (_currentUserAccessor.IsSuperAdmin) return;
        if (evt.CreatedBy == currentUserId) return;
        if (evt.UserId == currentUserId) return;
        throw new UnauthorizedAccessException($"无权访问事件 {evt.Id}");
    }

    public void EnsureCanAccess(CalendarEvent? evt)
    {
        if (evt == null)
            throw new KeyNotFoundException("事件不存在");
        var currentUserId = _currentUserAccessor.UserId;
        if (_currentUserAccessor.IsSuperAdmin) return;
        if (evt.CreatedBy == currentUserId) return;
        if (evt.UserId == currentUserId) return;
        throw new UnauthorizedAccessException($"无权访问事件 {evt.Id}");
    }

    public void EnsureCanModify(CalendarEventDto? evt)
    {
        if (evt == null)
            throw new KeyNotFoundException("事件不存在");
        var currentUserId = _currentUserAccessor.UserId;
        if (_currentUserAccessor.IsSuperAdmin) return;
        if (evt.CreatedBy == currentUserId) return;
        if (evt.UserId == currentUserId) return;
        throw new UnauthorizedAccessException($"无权修改事件 {evt.Id}");
    }

    public void EnsureCanModify(CalendarEvent? evt)
    {
        if (evt == null)
            throw new KeyNotFoundException("事件不存在");
        var currentUserId = _currentUserAccessor.UserId;
        if (_currentUserAccessor.IsSuperAdmin) return;
        if (evt.CreatedBy == currentUserId) return;
        if (evt.UserId == currentUserId) return;
        throw new UnauthorizedAccessException($"无权修改事件 {evt.Id}");
    }

    public CalendarEventQuery CheckQueryScope(CalendarEventQuery q)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (!_currentUserAccessor.IsSuperAdmin)
            q.UserId = _currentUserAccessor.UserId;
        return q;
    }

    public async Task<List<T>> FilterAndCheckAsync<T>(IEnumerable<T> source, CancellationToken ct = default) where T : class, IUserIdCarrier
    {
        var currentUserId = _currentUserAccessor.UserId;
        var authorizedIds = await ResolveAuthorizedUserIdsAsync(currentUserId).ConfigureAwait(false);

        var result = new List<T>();
        foreach (var item in source)
        {
            if (authorizedIds == null)
            {
                result.Add(item);
                continue;
            }
            if (item.UserId.HasValue && authorizedIds.Contains(item.UserId.Value))
                result.Add(item);
        }
        return result;
    }

    public T FilterAndCheck<T>(T req) where T : class, IUserIdCarrier
    {
        ArgumentNullException.ThrowIfNull(req);
        var user = _currentUserAccessor;
        // 只做字段规范化与默认值填充，不做跨用户权限阻断。
        // 跨用户分配的合法性由异步 ValidateCreateAccessAsync / EnsureCanModifyAsync 判定
        // （它们能通过 IUserRoleResolver 解析 AgentSupervisor/Agent 的下属范围）
        if (!user.IsSuperAdmin)
        {
            if (!req.UserId.HasValue)
                req.UserId = user.UserId;
        }
        return req;
    }
}
