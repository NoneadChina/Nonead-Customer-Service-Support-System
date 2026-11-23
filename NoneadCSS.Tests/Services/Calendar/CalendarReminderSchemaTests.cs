using FluentAssertions;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Tests.Services.Calendar;

/// <summary>
/// Commit #7.1: 日历提醒相关实体 Schema 测试。
/// 严格 TDD：此文件应在实体实现前编写，初始状态应编译失败或断言失败。
/// </summary>
public class CalendarReminderSchemaTests
{
    [Fact]
    public void CalendarEvent_DefaultReminderConfirmation_ShouldBePending()
    {
        // H-S2: 新创建的 CalendarEvent 默认 ReminderConfirmation = "pending"
        var evt = new CalendarEvent();

        evt.ReminderConfirmation.Should().Be("pending",
            "新建日历事件的提醒确认状态默认为 pending（规范 §3.2 要求）");
    }

    [Fact]
    public void CalendarEvent_ReminderFields_ShouldBeNullable()
    {
        var evt = new CalendarEvent();

        evt.ReminderSentAt.Should().BeNull("初始时 ReminderSentAt 应为空");
        evt.ReminderConfirmedAt.Should().BeNull("初始时 ReminderConfirmedAt 应为空");
    }

    [Fact]
    public void CalendarReminderAttempt_DefaultValues_ShouldBeCorrect()
    {
        var attempt = new CalendarReminderAttempt
        {
            EventId = 1,
            AttemptChannel = "email",
            Status = "sent"
        };

        attempt.AttemptNo.Should().Be(1, "默认尝试次数为 1");
        attempt.Id.Should().Be(0, "未保存时 Id 为默认值 0");
    }

    [Fact]
    public void CalendarReminderAttempt_ShouldSupportSoftDelete()
    {
        var attempt = new CalendarReminderAttempt
        {
            EventId = 1,
            AttemptChannel = "im",
            Status = "failed"
        };

        // 对齐 OutlookSyncLog 的审计表模式，支持 ISoftDelete
        attempt.IsDeleted().Should().BeFalse();
        attempt.SoftDelete();
        attempt.IsDeleted().Should().BeTrue();
        attempt.DeletedAt.Should().NotBeNull();
    }
}
