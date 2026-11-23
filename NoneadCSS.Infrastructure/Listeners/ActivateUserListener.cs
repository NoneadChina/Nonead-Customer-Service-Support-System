using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 用户激活的事件监听器
/// 对应 PHP: app/Listeners/ActivateUser.php
/// <para>
/// 只负责「账号已激活」站内通知：激活动作本身已由 UserService.ActivateUserAsync
/// （UsersController.cs:278 调用）完成，此处再次调用会造成二次激活，故不再依赖 IUserService。
/// </para>
/// </summary>
public class ActivateUserListener : IListener<UserActivatedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<ActivateUserListener> _logger;

    public ActivateUserListener(
        INotificationService notificationService,
        ILogger<ActivateUserListener> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task HandleAsync(UserActivatedEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        await _notificationService.SendNotificationAsync(
            @event.UserId,
            "账号已激活",
            "您的账号已激活，现在可以登录系统了。",
            "/login");

        _logger.LogInformation("用户 {UserId} 激活通知已发送", @event.UserId);
    }
}
