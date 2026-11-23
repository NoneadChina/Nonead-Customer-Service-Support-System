using FluentAssertions;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using Xunit;

namespace NoneadCSS.Tests.Mappings;

/// <summary>
/// T000100 回归：CalendarEventDto.SanitizeForUser 对 ReminderEmails 的可见性裁剪。
/// 创建者、事件负责人（UserId 匹配）与超管保留 ReminderEmails（编辑回填依赖）；
/// 仅对无关用户清空（隐私保护）。
/// </summary>
public class CalendarEventSanitizeTests
{
    private static CalendarEventDto MakeDto(int createdBy, int? userId, string? reminderEmails = "alice@example.com,bob@test.org")
        => new()
        {
            Id = 7,
            Title = "测试日程",
            CreatedBy = createdBy,
            UserId = userId,
            ReminderEmails = reminderEmails,
        };

    [Fact]
    public void Sanitize_WhenCurrentUserIsCreator_KeepsReminderEmails()
    {
        var dto = MakeDto(createdBy: 42, userId: 42);

        var result = dto.SanitizeForUser(42, isSuperAdmin: false);

        result.ReminderEmails.Should().Be("alice@example.com,bob@test.org",
            "创建者本人编辑日程必须能回填参会人邮箱（T000100）");
    }

    [Fact]
    public void Sanitize_WhenCurrentUserIsAssignee_KeepsReminderEmails()
    {
        // 事件由 admin(1) 创建但指派给 dev-user(42) → dev-user 作为负责人编辑时也应能回填参会人邮箱
        var dto = MakeDto(createdBy: 1, userId: 42);

        var result = dto.SanitizeForUser(42, isSuperAdmin: false);

        result.ReminderEmails.Should().Be("alice@example.com,bob@test.org",
            "T000100：事件负责人(UserId)编辑回显不得被清空，否则再次编辑参会人为空");
    }

    [Fact]
    public void Sanitize_WhenCurrentUserIsSuperAdmin_KeepsReminderEmails()
    {
        var dto = MakeDto(createdBy: 1, userId: 1);

        var result = dto.SanitizeForUser(99, isSuperAdmin: true);

        result.ReminderEmails.Should().Be("alice@example.com,bob@test.org");
    }

    [Fact]
    public void Sanitize_WhenCurrentUserIsUnrelated_ClearsReminderEmails()
    {
        var dto = MakeDto(createdBy: 1, userId: 2);

        var result = dto.SanitizeForUser(999, isSuperAdmin: false);

        result.ReminderEmails.Should().BeNull("无关用户不得读取他人日程的参会人邮箱（隐私）");
    }

    [Fact]
    public void Sanitize_WhenNoReminderEmails_DoesNotMutateOtherFields()
    {
        var dto = MakeDto(createdBy: 1, userId: 2, reminderEmails: null);

        var result = dto.SanitizeForUser(999, isSuperAdmin: false);

        result.Title.Should().Be("测试日程");
        result.ReminderEmails.Should().BeNull();
    }
}
