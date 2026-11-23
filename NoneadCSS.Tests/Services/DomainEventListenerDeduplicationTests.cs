using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Listeners;
using System.Reflection;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 监听器精简（去重复副作用）测试。
/// 接线后监听器真的会执行，因此原先与服务层/过滤器重复的副作用必须已经去掉：
/// - ConversationStatusChangedListener / ConversationUserChangedListener / UserRepliedListener：
///   审计已由 AuditActionFilter（status_change / assign / reply）写入，监听器不再写审计；
/// - ActivateUserListener：激活本身已由 UserService.ActivateUserAsync 完成，监听器不再二次激活；
/// - LogFailedLoginListener / LogLockoutListener：写入的 action 必须与
///   AuditLogRepository.cs:101-102 的过滤值逐字一致（小写 loginfailed / lockout），
///   否则仪表盘「异常事件数」仍会是 0。
/// </summary>
public class DomainEventListenerDeduplicationTests
{
    private static IEnumerable<Type> CtorParamTypes(Type listenerType) =>
        listenerType.GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

    [Fact]
    public void ConversationStatusChangedListener_Should_NotDependOnAuditService_Anymore()
    {
        CtorParamTypes(typeof(ConversationStatusChangedListener)).Should().NotContain(typeof(IAuditService));
    }

    [Fact]
    public void ConversationUserChangedListener_Should_NotDependOnAuditService_Anymore()
    {
        CtorParamTypes(typeof(ConversationUserChangedListener)).Should().NotContain(typeof(IAuditService));
    }

    [Fact]
    public void UserRepliedListener_Should_NotDependOnAuditService_Anymore()
    {
        CtorParamTypes(typeof(UserRepliedListener)).Should().NotContain(typeof(IAuditService));
    }

    [Fact]
    public void ActivateUserListener_Should_NotDependOnUserService_Anymore()
    {
        // 依赖里不再有 IUserService ⇒ 结构上不可能再触发二次激活
        CtorParamTypes(typeof(ActivateUserListener)).Should().NotContain(typeof(IUserService));
    }

    [Fact]
    public async Task ConversationStatusChangedListener_OnlySendsInAppNotification()
    {
        // Arrange
        var notification = new Mock<INotificationService>();
        var listener = new ConversationStatusChangedListener(notification.Object, Mock.Of<ILogger<ConversationStatusChangedListener>>());
        var conversation = new Conversation { Id = 5, Number = 5, Subject = "S", Status = ConversationStatus.Active, UserId = 9 };

        // Act
        await listener.HandleAsync(new ConversationStatusChangedEvent
        {
            Conversation = conversation,
            OldStatus = "Active",
            NewStatus = "Closed",
            ChangedByUserId = 3
        });

        // Assert：只发站内通知，没有别的副作用
        notification.Verify(n => n.SendNotificationAsync(9, "会话状态已变更", It.IsAny<string>(), "/conversations/5"), Times.Once);
        notification.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConversationUserChangedListener_OnlyNotifiesTheNewAssignee()
    {
        // Arrange
        var notification = new Mock<INotificationService>();
        var listener = new ConversationUserChangedListener(notification.Object, Mock.Of<ILogger<ConversationUserChangedListener>>());
        var conversation = new Conversation { Id = 6, Number = 6, Subject = "S", Status = ConversationStatus.Active };

        // Act
        await listener.HandleAsync(new ConversationUserChangedEvent
        {
            Conversation = conversation,
            OldUserId = null,
            NewUserId = 11
        });

        // Assert
        notification.Verify(n => n.SendNotificationAsync(11, "您有新的会话", It.IsAny<string>(), "/conversations/6"), Times.Once);
        notification.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConversationUserChangedListener_Should_NotNotify_When_ConversationUnassigned()
    {
        // Arrange
        var notification = new Mock<INotificationService>();
        var listener = new ConversationUserChangedListener(notification.Object, Mock.Of<ILogger<ConversationUserChangedListener>>());
        var conversation = new Conversation { Id = 7, Number = 7, Subject = "S", Status = ConversationStatus.Active };

        // Act：取消分配（NewUserId 为 null）时不该发「您有新的会话」
        await listener.HandleAsync(new ConversationUserChangedEvent
        {
            Conversation = conversation,
            OldUserId = 11,
            NewUserId = null
        });

        // Assert
        notification.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UserRepliedListener_OnlyNotifiesFollowers_ExcludingTheReplier()
    {
        // Arrange：这是 GetConversationFollowersAsync 的唯一调用点，必须保留
        var notification = new Mock<INotificationService>();
        notification.Setup(n => n.GetConversationFollowersAsync(8)).ReturnsAsync(new List<int> { 21, 22, 23 });
        var listener = new UserRepliedListener(notification.Object, Mock.Of<ILogger<UserRepliedListener>>());
        var conversation = new Conversation { Id = 8, Number = 8, Subject = "S", Status = ConversationStatus.Active };

        // Act：回复人是 22
        await listener.HandleAsync(new UserRepliedEvent
        {
            Conversation = conversation,
            Thread = new ConversationThread { Id = 90, ConversationId = 8, Body = "B" },
            User = new User { Id = 22, Email = "agent@example.com", Role = UserRoleType.Agent, Status = UserStatus.Active }
        });

        // Assert：给 21、23 各发一条，回复人自己不重复收通知
        notification.Verify(n => n.GetConversationFollowersAsync(8), Times.Once);
        notification.Verify(n => n.SendNotificationAsync(21, "会话有新回复", It.IsAny<string>(), "/conversations/8"), Times.Once);
        notification.Verify(n => n.SendNotificationAsync(23, "会话有新回复", It.IsAny<string>(), "/conversations/8"), Times.Once);
        notification.Verify(n => n.SendNotificationAsync(22, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ActivateUserListener_OnlySendsActivationNotification()
    {
        // Arrange
        var notification = new Mock<INotificationService>();
        var listener = new ActivateUserListener(notification.Object, Mock.Of<ILogger<ActivateUserListener>>());

        // Act
        await listener.HandleAsync(new UserActivatedEvent { UserId = 12 });

        // Assert：不再调用激活，只发「账号已激活」通知
        notification.Verify(n => n.SendNotificationAsync(12, "账号已激活", "您的账号已激活，现在可以登录系统了。", "/login"), Times.Once);
        notification.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LogFailedLoginListener_Should_WriteActionMatchingDashboardFilter()
    {
        // Arrange：AuditLogRepository.cs:101-102 按 action == "loginfailed" 统计「异常事件数」
        var audit = new Mock<IAuditService>();
        AuditLogEntry? captured = null;
        audit.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);
        var listener = new LogFailedLoginListener(audit.Object, Mock.Of<ILogger<LogFailedLoginListener>>());

        // Act
        await listener.HandleAsync(new LoginFailedEvent
        {
            UserId = 4,
            Email = "user@example.com",
            Reason = "密码错误",
            IpAddress = "203.0.113.7",
            UserAgent = "UnitTestAgent"
        });

        // Assert：大小写敏感，必须逐字一致
        captured.Should().NotBeNull();
        captured!.Action.Should().Be("loginfailed");
        captured.EntityType.Should().Be("User");
        captured.EntityId.Should().Be(4);
        captured.UserId.Should().Be(4);
        captured.IpAddress.Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task LogLockoutListener_Should_WriteActionMatchingDashboardFilter()
    {
        // Arrange
        var audit = new Mock<IAuditService>();
        AuditLogEntry? captured = null;
        audit.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);
        var listener = new LogLockoutListener(audit.Object, Mock.Of<ILogger<LogLockoutListener>>());

        // Act
        await listener.HandleAsync(new LockoutEvent
        {
            UserId = 4,
            Email = "user@example.com",
            IpAddress = "203.0.113.7"
        });

        // Assert
        captured.Should().NotBeNull();
        captured!.Action.Should().Be("lockout");
        captured.EntityType.Should().Be("User");
        captured.EntityId.Should().Be(4);
    }
}
