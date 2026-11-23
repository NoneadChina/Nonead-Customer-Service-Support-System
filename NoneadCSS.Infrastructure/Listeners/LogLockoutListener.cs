using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 登录锁定日志的事件监听器
/// 对应 PHP: app/Listeners/LogLockout.php
///
/// 写入的 audit.action 必须与 AuditLogRepository.cs:101-102 的过滤值逐字一致（小写 "lockout"），
/// 因为 AuditActionFilter.cs:165-170 明确跳过 Auth 控制器的 login 动作，这里是该计数唯一来源。
/// </summary>
public class LogLockoutListener : IListener<LockoutEvent>
{
    private readonly IAuditService _auditService;
    private readonly ILogger<LogLockoutListener> _logger;

    public LogLockoutListener(IAuditService auditService, ILogger<LogLockoutListener> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleAsync(LockoutEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "lockout",
            EntityType = "User",
            EntityId = @event.UserId,
            UserId = @event.UserId,
            Description = $"账号因连续失败登录被锁定 (邮箱: {@event.Email})",
            IpAddress = @event.IpAddress,
            UserAgent = @event.UserAgent
        }, cancellationToken);

        _logger.LogWarning("账号锁定日志已记录: {Email}", @event.Email);
    }
}
