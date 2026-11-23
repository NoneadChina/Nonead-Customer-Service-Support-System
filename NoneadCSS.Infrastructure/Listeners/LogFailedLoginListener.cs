using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 登录失败日志的事件监听器
/// 对应 PHP: app/Listeners/LogFailedLogin.php
///
/// 写入的 audit.action 必须与 AuditLogRepository.cs:101-102 的过滤值逐字一致（小写 "loginfailed"），
/// 因为 AuditActionFilter.cs:165-170 明确跳过 Auth 控制器的 login 动作，这里是该计数唯一来源。
/// </summary>
public class LogFailedLoginListener : IListener<LoginFailedEvent>
{
    private readonly IAuditService _auditService;
    private readonly ILogger<LogFailedLoginListener> _logger;

    public LogFailedLoginListener(IAuditService auditService, ILogger<LogFailedLoginListener> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task HandleAsync(LoginFailedEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "loginfailed",
            EntityType = "User",
            EntityId = @event.UserId,
            UserId = @event.UserId,
            Description = $"登录失败: {(@event.Reason ?? "未知原因")} (尝试邮箱: {@event.Email})",
            IpAddress = @event.IpAddress,
            UserAgent = @event.UserAgent
        }, cancellationToken);

        _logger.LogWarning("登录失败日志已记录: {Email}, 原因: {Reason}", @event.Email, @event.Reason);
    }
}
