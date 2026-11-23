using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// M-26：CalendarExportService 单元测试
/// 覆盖 ICS 导出/导入、ParseIcsDate 边界、Feed 订阅安全边界
/// </summary>
public class CalendarExportServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<ICalendarService> _mockCalendarService;
    private readonly Mock<ICalendarEventRepository> _mockCalendarRepo;
    private readonly Mock<ILogger<CalendarExportService>> _mockLogger;
    private readonly IConfiguration _configuration;
    private readonly CalendarExportService _service;

    public CalendarExportServiceTests()
    {
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockCalendarService = new Mock<ICalendarService>();
        _mockCalendarRepo = new Mock<ICalendarEventRepository>();
        _mockLogger = new Mock<ILogger<CalendarExportService>>();

        _mockUnitOfWork.Setup(u => u.CalendarEvents).Returns(_mockCalendarRepo.Object);
        _mockUnitOfWork.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        // mock 事务：所有方法为 no-op（异步返回完成任务）
        var mockTx = new Mock<IUnitOfWorkTransaction>();
        mockTx.Setup(t => t.CommitAsync()).Returns(Task.CompletedTask);
        mockTx.Setup(t => t.RollbackAsync()).Returns(Task.CompletedTask);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync())
            .ReturnsAsync(mockTx.Object);

        var inMemory = new Dictionary<string, string?>
        {
            ["Calendar:ExportPageSize"] = "200",
        };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        _service = new CalendarExportService(
            _mockUnitOfWork.Object,
            _mockCalendarService.Object,
            _mockLogger.Object,
            _configuration);
    }

    private static CalendarEvent CreateEvent(int id = 1, string title = "测试会议")
    {
        return new CalendarEvent
        {
            Id = id,
            Title = title,
            Description = "测试描述",
            EventType = "meeting",
            StartAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            EndAt = new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc),
            IsAllDay = false,
            Status = "pending",
            Location = "会议室 A",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    #region ExportEventAsync

    [Fact]
    public async Task ExportEventAsync_ExistingEvent_ReturnsValidIcs()
    {
        // Arrange
        var ev = CreateEvent();
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ev);

        // Act
        var ics = await _service.ExportEventAsync(1);

        // Assert
        ics.Should().NotBeNullOrEmpty();
        ics.Should().StartWith("BEGIN:VCALENDAR");
        ics.Should().Contain("BEGIN:VEVENT");
        ics.Should().Contain("SUMMARY:测试会议");
        ics.Should().Contain("DESCRIPTION:测试描述");
        ics.Should().Contain("LOCATION:会议室 A");
        ics.Should().Contain("DTSTART:20260115T100000Z");
        ics.Should().EndWith("END:VCALENDAR\r\n");
    }

    [Fact]
    public async Task ExportEventAsync_NonExistingEvent_ReturnsEmptyWrappedCalendar()
    {
        // Arrange
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((CalendarEvent?)null);

        // Act
        var ics = await _service.ExportEventAsync(99);

        // Assert
        ics.Should().StartWith("BEGIN:VCALENDAR");
        ics.Should().NotContain("BEGIN:VEVENT");
    }

    [Fact]
    public async Task ExportEventAsync_DeletedEvent_ReturnsEmptyCalendar()
    {
        // Arrange
        var ev = CreateEvent();
        ev.DeletedAt = DateTime.UtcNow;
        _mockCalendarRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ev);

        // Act
        var ics = await _service.ExportEventAsync(1);

        // Assert
        ics.Should().NotContain("BEGIN:VEVENT");
    }

    #endregion

    #region ICS Import 安全边界 (H-19)

    [Fact]
    public async Task ImportAsync_ContentExceeds10MB_ThrowsArgumentException()
    {
        // Arrange
        var huge = new string('A', 11 * 1024 * 1024);

        // Act
        Func<Task> act = async () => await _service.ImportAsync(huge, 1);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ICS 文件内容过大*");
    }

    [Fact]
    public async Task ImportAsync_TooManyEvents_ThrowsArgumentException()
    {
        // Arrange: 构造 1001 个 VEVENT（超过单次导入上限 1000）
        var sb = new System.Text.StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n");
        for (var i = 0; i < 1001; i++)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:{i}@test\r\n");
            sb.Append("SUMMARY:测试\r\n");
            sb.Append("DTSTART:20260101T000000Z\r\n");
            sb.Append("END:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR\r\n");

        // Act
        Func<Task> act = async () => await _service.ImportAsync(sb.ToString(), 1);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ICS 文件包含*个事件，超过单次导入上限*");
    }

    [Fact]
    public async Task ImportAsync_EmptyOrNull_ZeroImported()
    {
        // Act
        var (imported, skipped) = await _service.ImportAsync("", 1);

        // Assert
        imported.Should().Be(0);
        skipped.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_SingleValidEvent_ShouldCreateEvent()
    {
        // Arrange
        var ics =
            "BEGIN:VCALENDAR\r\n" +
            "VERSION:2.0\r\n" +
            "BEGIN:VEVENT\r\n" +
            "UID:test-1@local\r\n" +
            "SUMMARY:团队周会\r\n" +
            "DESCRIPTION:每周例会\r\n" +
            "LOCATION:线上\r\n" +
            "DTSTART:20260301T090000Z\r\n" +
            "DTEND:20260301T100000Z\r\n" +
            "END:VEVENT\r\n" +
            "END:VCALENDAR\r\n";

        CalendarEvent? captured = null;
        _mockCalendarRepo.Setup(r => r.AddAsync(It.IsAny<CalendarEvent>()))
            .Callback<CalendarEvent>(e => { e.Id = 42; captured = e; })
            .ReturnsAsync((CalendarEvent e) => e);

        _mockCalendarService.Setup(s => s.CreateAsync(It.IsAny<CreateCalendarEventRequest>()))
            .ReturnsAsync((CreateCalendarEventRequest r) => new CalendarEventDto { Id = 42, Title = r.Title });

        // Act
        var (imported, skipped) = await _service.ImportAsync(ics, 1);

        // Assert
        imported.Should().Be(1);
        skipped.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_EventWithoutSummary_Skipped()
    {
        // Arrange: 没有 SUMMARY 的事件应跳过
        var ics =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
            "BEGIN:VEVENT\r\nUID:no-summary@local\r\n" +
            "DTSTART:20260301T090000Z\r\nEND:VEVENT\r\n" +
            "END:VCALENDAR\r\n";

        // Act
        var (imported, skipped) = await _service.ImportAsync(ics, 1);

        // Assert
        imported.Should().Be(0);
        skipped.Should().Be(1);
    }

    #endregion

    #region ICS 日期解析边界

    public static IEnumerable<object[]> IcsDateTestCases()
    {
        yield return new object[] { "20260115T100000Z", new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc), false };
        yield return new object[] { "20260115", new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), true };
        // 本地时间不带 Z
        yield return new object[] { "20260115T100000", new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Unspecified), false };
    }

    [Theory]
    [MemberData(nameof(IcsDateTestCases))]
    public void ParseIcsDate_ThroughImport_VariousFormats_ParsedCorrectly(
        string raw, DateTime expectedDate, bool expectedAllDay)
    {
        // 用反射调用私有方法
        var method = typeof(CalendarExportService).GetMethod(
            "ParseIcsDate",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        // Act
        var result = method!.Invoke(null, new object[] { raw });
        result.Should().NotBeNull();
        // 强转为可空结构体
        var resultTyped = (ValueTuple<DateTime?, bool>?)result;
        resultTyped.Should().NotBeNull();
        var (date, isAllDay) = resultTyped!.Value;

        // Assert
        date.Should().NotBeNull();
        date!.Value.Should().Be(expectedDate);
        isAllDay.Should().Be(expectedAllDay);
    }

    [Fact]
    public void ParseIcsDate_InvalidFormat_ReturnsNull()
    {
        var method = typeof(CalendarExportService).GetMethod(
            "ParseIcsDate",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        var result = method!.Invoke(null, new object[] { "not-a-date" });
        var tuple = ((DateTime? Date, bool IsAllDay))result!;
        tuple.Date.Should().BeNull();
    }

    #endregion

    #region ExportRangeForFeedAsync 安全

    [Fact]
    public async Task ExportRangeForFeedAsync_AlwaysScopedToGivenUserId()
    {
        // Arrange
        var startDate = new DateTime(2026, 3, 1);
        var endDate = new DateTime(2026, 4, 1);
        const int feedUserId = 7;

        var events = new List<CalendarEvent>
        {
            CreateEvent(1, "Feed 用户事件")
        };
        // ExportRangeForFeedAsync 直接调用仓储而非 CalendarService
        // 仓储签名: GetListAsync(start, end, mailboxId, userId, userIds, eventType, status, keyword, page, pageSize)
        _mockCalendarRepo.Setup(r => r.GetListAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(events);
        _mockCalendarRepo.Setup(r => r.CountAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(events.Count);

        // Act
        var ics = await _service.ExportRangeForFeedAsync(startDate, endDate, feedUserId);

        // Assert: 必须生成非空 ICS 且仓储被按指定 userId 过滤调用（第 4 个参数 userId == feedUserId）
        ics.Should().NotBeNullOrEmpty();
        _mockCalendarRepo.Verify(r => r.GetListAsync(
            startDate, endDate,
            It.IsAny<int?>(),
            feedUserId,                                       // userId 参数严格相等（防止越权）
            It.IsAny<IEnumerable<int>?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            1, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task ExportRangeForFeedAsync_DoesNotExpandRecurring()
    {
        // Arrange
        var events = new List<CalendarEvent>();
        _mockCalendarRepo.Setup(r => r.GetListAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(events);
        _mockCalendarRepo.Setup(r => r.CountAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(0);

        // Act
        await _service.ExportRangeForFeedAsync(DateTime.UtcNow, DateTime.UtcNow.AddDays(30), 1);

        // Assert：该方法直接查询仓储（重复展开由 CalendarService 负责）。
        // 验证仓储被至少调用一次以完成分页扫描。
        _mockCalendarRepo.Verify(r => r.GetListAsync(
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<IEnumerable<int>?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            1, It.IsAny<int>()), Times.Once);
    }

    #endregion
}
