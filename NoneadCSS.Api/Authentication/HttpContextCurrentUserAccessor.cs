using System.Security.Claims;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Api.Authentication;

/// <summary>
/// 基于 HttpContext 的当前用户访问器实现。
/// </summary>
public class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMailboxService _mailboxService;
    private readonly IUserRoleResolver _userRoleResolver;

    public HttpContextCurrentUserAccessor(
        IHttpContextAccessor httpContextAccessor,
        IMailboxService mailboxService,
        IUserRoleResolver userRoleResolver)
    {
        _httpContextAccessor = httpContextAccessor;
        _mailboxService = mailboxService;
        _userRoleResolver = userRoleResolver;
    }

    public int UserId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
            return claim != null && int.TryParse(claim.Value, out var id) ? id : 0;
        }
    }

    public bool IsSuperAdmin
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role);
            return claim != null && int.TryParse(claim.Value, out var role) && role == (int)UserRoleType.SuperAdmin;
        }
    }

    public async Task<bool> CanAccessMailboxAsync(int? mailboxId)
    {
        if (mailboxId == null) return true;
        if (IsSuperAdmin) return true;
        if (UserId == 0) return false;

        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(UserId);
        return mailboxes.Any(m => m.Id == mailboxId);
    }

    public async Task<int[]?> GetAccessibleMailboxIdsAsync()
    {
        // 超管返回 null，表示不限制（仓储层不附加 mailbox 过滤）
        if (IsSuperAdmin) return null;
        if (UserId == 0) return Array.Empty<int>();

        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(UserId);
        return mailboxes.Select(m => m.Id).ToArray();
    }
}
