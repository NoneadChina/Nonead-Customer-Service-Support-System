using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 保存用户语言偏好的事件监听器
/// 对应 PHP: app/Listeners/RememberUserLocale.php
/// </summary>
public class RememberUserLocaleListener : IListener<UserLoggedInEvent>
{
    private readonly IUserService _userService;
    private readonly ILogger<RememberUserLocaleListener> _logger;

    public RememberUserLocaleListener(IUserService userService, ILogger<RememberUserLocaleListener> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    public async Task HandleAsync(UserLoggedInEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        if (!string.IsNullOrEmpty(@event.Locale))
        {
            await _userService.UpdateUserLocaleAsync(@event.UserId, @event.Locale);
            _logger.LogInformation("已保存用户 {UserId} 的语言偏好: {Locale}", @event.UserId, @event.Locale);
        }
    }
}
