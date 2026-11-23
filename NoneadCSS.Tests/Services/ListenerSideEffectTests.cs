using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure.Listeners;
using System.Reflection;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 精简后的事件监听器副作用单元测试（③组接线的前置条件）。
/// 两件事必须同时成立：
/// 1. 缺失的副作用（站内通知 / 关注者通知）确实执行了；
/// 2. 与 AuditActionFilter、UsersController 重复的副作用已经去掉——
///    表现为监听器不再依赖 IAuditService / IUserService，也不会写审计或二次激活。
/// 同时锁定登录失败与锁定审计写入的 action 字符串，必须与
/// NoneadCSS.Infrastructure/Repositories/AuditLogRepository.cs:101-102 的过滤值逐字一致。
/// </summary>
public class ListenerSideEffectTests
{
    private static Mock<ILogger<T>> NullLoggerMock<T>() => new();

    // ---------- 审计 action 口径（②.3：修复仪表盘「异常事件数」恒为 0） ----------

    [Fact(DisplayName = "LogFailedLoginListener 写入 action=\"loginfailed\"（与仓储过滤值逐字一致）")]
    public async Task LogFailedLoginListener_WritesLowercaseLoginfailedAction()
    {
        var audit = new Mock<IAuditService>();
        AuditLogEntry? captured = null;
        audit.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var listener = new LogFailedLoginListener(audit.Object, NullLoggerMock<LogFailedLoginListener>().Object);

        await listener.HandleAsync(new LoginFailedEvent
        {
            UserId = 42,
            Email = "user@example.com",
            Reason = "密码错误",
            IpAddress = "203.0.113.7",
            UserAgent = "UnitTestAgent"
        });

        captured.Should().NotBeNull();
        captured!.Action.Should().Be("loginfailed");
        captured.EntityType.Should().Be("User");
        captured.EntityId.Should().Be(42);
        captured.UserId.Should().Be(42);
        captured.Description.Should().Contain("密码错误").And.Contain("user@example.com");
        captured.IpAddress.Should().Be("203.0.113.7");
        captured.UserAgent.Should().Be("UnitTestAgent");
    }

    [Fact(DisplayName = "LogLockoutListener 写入 action=\"lockout\"（与仓储过滤值逐字一致）")]
    public async Task LogLockoutListener_WritesLowercaseLockoutAction()
    {
        var audit = new Mock<IAuditService>();
        AuditLogEntry? captured = null;
        audit.Setup(a => a.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var listener = new LogLockoutListener(audit.Object, NullLoggerMock<LogLockoutListener>().Object);

        await listener.HandleAsync(new LockoutEvent
        {
            UserId = 42,
            Email = "user@example.com",
            IpAddress = "203.0.113.7"
        });

        captured.Should().NotBeNull();
        captured!.Action.Should().Be("lockout");
        captured.EntityType.Should().Be("User");
        captured.Description.Should().Contain("user@example.com");
    }

    // ---------- ③.5 会话状态变更：只做站内通知 ----------

    [Fact(DisplayName = "ConversationStatusChangedListener 只发站内通知，不再依赖 IAuditService")]
    public async Task ConversationStatusChangedListener_OnlySendsInAppNotification()
    {
        var notification = new Mock<INotificationService>();
        var listener = new ConversationStatusChangedListener(notification.Object, NullLoggerMock<ConversationStatusChangedListener>().Object);

        var conversation = new Conversation { Id = 11, Subject = "退款问题", Status = ConversationStatus.Closed, UserId = 5 };

        await listener.HandleAsync(new ConversationStatusChangedEvent
        {
            Conversation = conversation,
            OldStatus = "Active",
            NewStatus = "Closed",
            ChangedByUserId = 9
        });

        notification.Verify(n => n.SendNotificationAsync(
            5,
            "会话状态已变更",
            It.Is<string>(m => m.Contains("#11") && m.Contains("Closed")),
            "/conversations/11"), Times.Once);

        // 审计由 AuditActionFilter（action="status_change"）覆盖，监听器不得再持有 IAuditService
        AssertNoDependency<ConversationStatusChangedListener>(typeof(IAuditService));
    }

    [Fact(DisplayName = "ConversationStatusChangedListener 在会话无归属人时不发通知")]
    public async Task ConversationStatusChangedListener_UnassignedConversation_SendsNothing()
    {
        var notification = new Mock<INotificationService>();
        var listener = new ConversationStatusChangedListener(notification.Object, NullLoggerMock<ConversationStatusChangedListener>().Object);

        await listener.HandleAsync(new ConversationStatusChangedEvent
        {
            Conversation = new Conversation { Id = 12, Subject = "未分配", Status = ConversationStatus.Pending, UserId = null },
            OldStatus = "Active",
            NewStatus = "Pending"
        });

        notification.Verify(n => n.SendNotificationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    // ---------- ③.6 会话改派：只做站内通知 ----------

    [Fact(DisplayName = "ConversationUserChangedListener 只给新归属人发站内通知，不再依赖 IAuditService")]
    public async Task ConversationUserChangedListener_OnlyNotifiesNewAssignee()
    {
        var notification = new Mock<INotificationService>();
        var listener = new ConversationUserChangedListener(notification.Object, NullLoggerMock<ConversationUserChangedListener>().Object);

        await listener.HandleAsync(new ConversationUserChangedEvent
        {
            Conversation = new Conversation { Id = 13, Subject = "登录异常", Status = ConversationStatus.Active, UserId = 7 },
            OldUserId = 3,
            NewUserId = 7
        });

        notification.Verify(n => n.SendNotificationAsync(
            7,
            "您有新的会话",
            It.Is<string>(m => m.Contains("#13") && m.Contains("登录异常")),
            "/conversations/13"), Times.Once);

        // 审计（action="assign"）由 AuditActionFilter 写入、SignalR 由 ConversationAssignmentService.NotifyUserAsync 推送
        AssertNoDependency<ConversationUserChangedListener>(typeof(IAuditService));
    }

    // ---------- ③.7 坐席回复：只做关注者通知 ----------

    [Fact(DisplayName = "UserRepliedListener 给关注者发通知并排除回复者本人，不再依赖 IAuditService")]
    public async Task UserRepliedListener_NotifiesFollowersExcludingReplier()
    {
        var notification = new Mock<INotificationService>();
        notification.Setup(n => n.GetConversationFollowersAsync(14)).ReturnsAsync(new List<int> { 21, 22, 23 });

        var listener = new UserRepliedListener(notification.Object, NullLoggerMock<UserRepliedListener>().Object);

        await listener.HandleAsync(new UserRepliedEvent
        {
            Conversation = new Conversation { Id = 14, Subject = "合同问题", Status = ConversationStatus.Active },
            Thread = new ConversationThread { Id = 99, ConversationId = 14, UserId = 21, Body = "已处理" },
            User = new User { Id = 21, Email = "agent@example.com", Status = UserStatus.Active }
        });

        notification.Verify(n => n.GetConversationFollowersAsync(14), Times.Once);
        notification.Verify(n => n.SendNotificationAsync(
            It.IsAny<int>(),
            "会话有新回复",
            It.Is<string>(m => m.Contains("#14")),
            "/conversations/14"), Times.Exactly(2));
        notification.Verify(n => n.SendNotificationAsync(21, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        notification.Verify(n => n.SendNotificationAsync(22, "会话有新回复", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        notification.Verify(n => n.SendNotificationAsync(23, "会话有新回复", It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        // 审计（action="reply"）由 AuditActionFilter.cs:209 覆盖
        AssertNoDependency<UserRepliedListener>(typeof(IAuditService));
    }

    // ---------- ③.8 账号激活：只做通知，不得二次激活 ----------

    [Fact(DisplayName = "ActivateUserListener 只发「账号已激活」通知，不再依赖 IUserService（避免二次激活）")]
    public async Task ActivateUserListener_OnlyNotifiesAndDoesNotActivateAgain()
    {
        var notification = new Mock<INotificationService>();
        var listener = new ActivateUserListener(notification.Object, NullLoggerMock<ActivateUserListener>().Object);

        await listener.HandleAsync(new UserActivatedEvent { UserId = 31, IpAddress = "203.0.113.7" });

        notification.Verify(n => n.SendNotificationAsync(
            31,
            "账号已激活",
            It.Is<string>(m => m.Contains("已激活")),
            "/login"), Times.Once);

        // 激活动作本身由 UserService.ActivateUserAsync 完成（UsersController.cs:278 的唯一入口），
        // 监听器再调一次就是二次激活。
        AssertNoDependency<ActivateUserListener>(typeof(IUserService));
    }

    /// <summary>
    /// 断言目标类型的公共构造函数中不存在指定类型的依赖（用于锁定「重复副作用已删除」）。
    /// </summary>
    /// <typeparam name="TListener">被检查的监听器类型。</typeparam>
    /// <param name="forbidden">不应再作为构造函数参数出现的依赖类型。</param>
    private static void AssertNoDependency<TListener>(Type forbidden)
    {
        var ctors = typeof(TListener).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        ctors.Should().NotBeEmpty($"{typeof(TListener).Name} 必须由 DI 构造");
        foreach (var ctor in ctors)
        {
            ctor.GetParameters().Select(p => p.ParameterType).Should().NotContain(forbidden,
                $"{typeof(TListener).Name} 不应再依赖 {forbidden.Name}（该副作用已由别处覆盖，保留会造成重复写入）");
        }
    }
}
