using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Exceptions;
using NoneadCSS.Application.Services.Calendar;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Tests.Helpers;

namespace NoneadCSS.Tests.Services.Calendar;

/// <summary>
/// 覆盖 CalendarConflictService 核心检测分支（不依赖真实 EF Provider）。
/// 使用 NoneadCSS.Tests.Helpers.TestAsyncQueryProvider.CreateAsyncQueryable 包装候选集。
/// </summary>
public class CalendarConflictServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUoW;
    private readonly Mock<ICalendarEventRepository> _mockCalRepo;
    private readonly ILogger<CalendarConflictService> _logger;
    private readonly CalendarConflictService _svc;

    public CalendarConflictServiceTests()
    {
        _mockUoW = new Mock<IUnitOfWork>();
        _mockCalRepo = new Mock<ICalendarEventRepository>();
        _mockUoW.Setup(u => u.CalendarEvents).Returns(_mockCalRepo.Object);
        _logger = new LoggerFactory().CreateLogger<CalendarConflictService>();
        _svc = new CalendarConflictService(_mockUoW.Object, _logger);
    }

    private static readonly DateTime T0 = new(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);

    /* ---------- 快速退出（不触发 DB 查询） ---------- */

    [Fact]
    public async Task CheckAsync_IsAllDay_ReturnsNull()
    {
        var r = await _svc.CheckAsync(DateTime.Now, DateTime.Now.AddHours(1), 1, null, true);
        r.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_UserIsNull_ReturnsNull()
    {
        var r = await _svc.CheckAsync(T0, T0.AddHours(1), null, null, false);
        r.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_EndAtBeforeStart_ReturnsNull()
    {
        var r = await _svc.CheckAsync(T0, T0.AddMinutes(-5), 1, null, false);
        r.Should().BeNull();
    }

    /* ---------- 核心：重叠检测 ---------- */

    [Fact]
    public async Task CheckAsync_EmptyCandidates_ReturnsNull()
    {
        SetupCandidates(Enumerable.Empty<CalendarEvent>());
        var r = await _svc.CheckAsync(T0, T0.AddHours(1), 1, null, false);
        r.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_NoOverlap_ReturnsNull()
    {
        SetupCandidates(new List<CalendarEvent>
        {
            new() { Id = 10, Title = "早会", UserId = 1, IsAllDay = false, Status = "confirmed",
                    StartAt = T0.AddHours(-2), EndAt = T0.AddHours(-1) }
        });

        var r = await _svc.CheckAsync(T0, T0.AddHours(1), 1, null, false);
        r.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_SoftOverlap_10min_ReturnsSoftResult()
    {
        SetupCandidates(new List<CalendarEvent>
        {
            new() { Id = 21, Title = "Soft", UserId = 1, IsAllDay = false, Status = "confirmed",
                    StartAt = T0.AddMinutes(5), EndAt = T0.AddMinutes(15) }
        });

        var r = await _svc.CheckAsync(T0, T0.AddHours(1), 1, null, false);
        r.Should().NotBeNull();
        r!.IsHardConflict.Should().BeFalse("重叠 10 分钟 ≤ 15 阈值应为 soft");
        r.Conflicts.Should().ContainSingle(c => c.Title == "Soft");
    }

    [Fact]
    public async Task EnforceHardConflictAsync_HardOverlap_60min_ThrowsConflictException()
    {
        SetupCandidates(new List<CalendarEvent>
        {
            new() { Id = 31, Title = "Hard", UserId = 1, IsAllDay = false, Status = "pending",
                    StartAt = T0, EndAt = T0.AddHours(1) }
        });

        var act = () => _svc.EnforceHardConflictAsync(T0, T0.AddHours(1), 1, null, false);
        var ex = await act.Should().ThrowAsync<CalendarConflictException>();
        ex.And.Conflicts.Should().ContainSingle(c => c.Title == "Hard");
        ex.And.OverlapMinutes.Should().BeGreaterThan(CalendarConflictService.HardThresholdMinutes);
    }

    [Fact]
    public async Task EnforceHardConflictAsync_SoftOverlap_NoThrow()
    {
        SetupCandidates(new List<CalendarEvent>
        {
            new() { Id = 71, Title = "Soft2", UserId = 1, IsAllDay = false, Status = "confirmed",
                    StartAt = T0.AddMinutes(3), EndAt = T0.AddMinutes(10) }
        });

        var act = () => _svc.EnforceHardConflictAsync(T0, T0.AddHours(1), 1, null, false);
        await act.Should().NotThrowAsync("soft 冲突仅打日志，不抛异常");
    }

    [Fact]
    public async Task EnforceHardConflictAsync_NoConflict_NoThrow()
    {
        SetupCandidates(Enumerable.Empty<CalendarEvent>());
        var act = () => _svc.EnforceHardConflictAsync(T0, T0.AddHours(1), 1, null, false);
        await act.Should().NotThrowAsync();
    }

    /* ---------- 排除项：excludeEventId / RecurrenceParentId ---------- */

    [Fact]
    public async Task CheckAsync_ExcludeEventId_SkipsSelf()
    {
        SetupCandidates(new List<CalendarEvent>
        {
            new() { Id = 42, Title = "Self", UserId = 1, IsAllDay = false, Status = "confirmed",
                    StartAt = T0, EndAt = T0.AddMinutes(30) }
        });

        var r = await _svc.CheckAsync(T0, T0.AddMinutes(30), 1, excludeEventId: 42, false);
        r.Should().BeNull("排除自身 Id 后应视为没有冲突");
    }

    [Fact]
    public async Task CheckAsync_ExcludeRecurrenceParentId_SkipsChildren()
    {
        SetupCandidates(new List<CalendarEvent>
        {
            new() { Id = 100, Title = "Child", UserId = 1, IsAllDay = false, Status = "confirmed",
                    RecurrenceParentId = 52, StartAt = T0, EndAt = T0.AddMinutes(30) }
        });

        var r = await _svc.CheckAsync(T0, T0.AddMinutes(30), 1, excludeEventId: 52, false);
        r.Should().BeNull("RecurrenceParentId == excludeEventId 的子事件应排除");
    }

    /* ---------- 构造函数防御性校验 ---------- */

    [Fact]
    public void Ctor_NullUnitOfWork_ThrowsArgumentNullException()
    {
        Action act = () => new CalendarConflictService(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new CalendarConflictService(_mockUoW.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /* ---------- 辅助 ---------- */

    private void SetupCandidates(IEnumerable<CalendarEvent> candidates)
    {
        // 通过 Helpers 包装可异步枚举的 IQueryable，满足服务中 .AsNoTracking().ToListAsync()
        var queryable = TestAsyncQueryProvider.CreateAsyncQueryable(candidates.ToList());
        _mockCalRepo.Setup(r => r.GetQueryable(It.IsAny<bool>())).Returns(queryable);
    }
}
