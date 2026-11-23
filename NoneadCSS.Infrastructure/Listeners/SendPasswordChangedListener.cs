using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Listeners;

/// <summary>
/// 用户密码变更通知的事件监听器
/// 对应 PHP: app/Listeners/SendPasswordChanged.php
/// </summary>
public class SendPasswordChangedListener : IListener<PasswordChangedEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SendPasswordChangedListener> _logger;
    private readonly string _frontendUrl;

    public SendPasswordChangedListener(
        INotificationService notificationService,
        IEmailService emailService,
        IUnitOfWork unitOfWork,
        ILogger<SendPasswordChangedListener> logger,
        IConfiguration configuration)
    {
        _notificationService = notificationService;
        _emailService = emailService;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _frontendUrl = configuration["App:FrontendUrl"]
            ?? Environment.GetEnvironmentVariable("App__FrontendUrl")
            ?? throw new InvalidOperationException("App:FrontendUrl 未配置");
    }

    public async Task HandleAsync(PasswordChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // 异常由 DomainEventDispatcher 统一捕获并解包 TargetInvocationException 后记录
        var user = @event.User;
        var title = "密码已修改";
        var message = $"您的密码已于 {DateTime.Now:yyyy-MM-dd HH:mm:ss} 修改。如非本人操作，请立即联系管理员。";

        await _notificationService.SendNotificationAsync(user.Id, title, message, "/profile");

        if (!string.IsNullOrEmpty(user.Email))
        {
            // 仅查询第一个活跃邮箱，避免 GetAllAsync 全表加载到内存再过滤
            var senderMailbox = await _unitOfWork.Mailboxes.GetFirstActiveAsync();
            if (senderMailbox != null)
            {
                await _emailService.SendSystemNotificationAsync(
                    senderMailbox,
                    user.Email,
                    "密码已修改 - 拓德客户服务与支持系统",
                    $"<p>{message}</p><p>登录地址: {_frontendUrl}/login</p>");
            }
            else
            {
                _logger.LogWarning("没有可用的活跃邮箱来发送密码变更通知");
            }
        }

        _logger.LogInformation("用户 {UserId} 密码变更通知已发送", user.Id);
    }
}
