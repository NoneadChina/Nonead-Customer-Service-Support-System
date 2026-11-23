using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 密码重置日志的事件监听器
/// 对应 PHP: app/Listeners/LogPasswordReset.php
///
/// 状态说明：PasswordResetEvent 目前**没有发布点**（本次接线未覆盖该事件），所以本监听器仍不会被执行。
/// 这里把 action 字符串统一成小写，是为了将来接线时与 AuditLogRepository.cs:101-102 的过滤值
/// （"resetfailed"）以及 AuditActionFilter 的小写口径保持一致。
/// </summary>
public class LogPasswordResetListener : IListener<PasswordResetEvent>
{
    private readonly IAuditService _auditService;
    private readonly ILogger<LogPasswordResetListener> _logger;

    public LogPasswordResetListener(IAuditService auditService, ILogger<LogPasswordResetListener> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleAsync(PasswordResetEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = @event.Success ? "reset" : "resetfailed",
            EntityType = "User",
            EntityId = @event.UserId,
            UserId = @event.Success ? @event.UserId : null,
            Description = @event.Success ? "密码重置成功" : "密码重置失败",
            IpAddress = @event.IpAddress,
            UserAgent = @event.UserAgent
        }, cancellationToken);

        _logger.LogInformation("用户 {UserId} 密码重置日志已记录 (成功: {Success})", @event.UserId, @event.Success);
    }
}
