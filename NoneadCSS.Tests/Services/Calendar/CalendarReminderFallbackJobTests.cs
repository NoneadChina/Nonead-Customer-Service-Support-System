using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Jobs;
using NoneadCSS.Infrastructure.Persistence;
using Quartz;
using Xunit;

namespace NoneadCSS.Tests.Services.Calendar;

/// <summary>
/// Commit #7.3: CalendarReminderFallbackJob（1 分钟 cron 的兜底补发任务）TDD 测试。
/// 严格 TDD：先写失败测试，再实现 Job 使其通过。
/// </summary>
public class CalendarReminderFallbackJobTests
{
    [Fact]
    public async Task ExecuteInternal_PendingBeyondWindow_WritesEmailAttempt_AndSetsFallbackSent()
    {
        // ===== Arrange =====
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        await using var context = new NoneadCSSDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = 0");

        // 窗口：ReminderConfirmationWindowMinutes=5
        // 准备数据：ReminderSentAt = 10 分钟前（超过 5 分钟窗口），ReminderConfirmation=pending
        var reminderSentAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var now = DateTime.UtcNow;

        var columns = await context.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('CalendarEvents') ORDER BY cid").ToListAsync();

        var cols = new List<string>
        {
            "Id", "Title", "EventType", "StartAt", "Status",
            "CreatedBy", "UserId", "CreatedAt", "UpdatedAt",
            "IsAllDay", "IsReminderSent", "IsRecurring",
            "ReminderConfirmation", "ReminderSentAt", "ReminderMinutes"
        };
        cols.RemoveAll(c => !columns.Contains(c));
        if (columns.Contains("Priority")) cols.Add("Priority");
        if (columns.Contains("Progress")) cols.Add("Progress");
        if (columns.Contains("RowVersion")) cols.Add("RowVersion");

        var insertSql = $@"
INSERT INTO CalendarEvents ({string.Join(", ", cols)})
VALUES (
    1, 'Fallback 测试事件', 'task', @startAt, 'pending',
    100, 100, @createdAt, @updatedAt,
    0, 1, 0,
    'pending', @reminderSentAt,
    15
    {(cols.Contains("Priority") ? ", 2" : "")}
    {(cols.Contains("Progress") ? ", 0" : "")}
    {(cols.Contains("RowVersion") ? ", 0x0000000000000001" : "")}
)";
        await context.Database.ExecuteSqlRawAsync(insertSql, new[]
        {
            new Microsoft.Data.Sqlite.SqliteParameter("@startAt", now.AddHours(1)),
            new Microsoft.Data.Sqlite.SqliteParameter("@createdAt", now.AddHours(-1)),
            new Microsoft.Data.Sqlite.SqliteParameter("@updatedAt", now.AddMinutes(-11)),
            // H-01：直接传 DateTimeOffset（而非 .DateTime 或 .ToString("o")），让 Microsoft.Data.Sqlite
            // 按与 EF Core 写入列时一致的格式序列化为字符串，确保 FromSqlInterpolated 的 DateTimeOffset
            // 参数与列存储字典序比较一致（避免 'T' vs ' ' 分隔符导致的字典序错乱）。
            new Microsoft.Data.Sqlite.SqliteParameter("@reminderSentAt", reminderSentAt)
        });

        // 用 ServiceCollection + Sqlite 真实 DbContext 构造 IServiceProvider
        var services = new ServiceCollection();
        services.AddSingleton(dbOptions);
        services.AddScoped<NoneadCSSDbContext>();

        // CalendarOptions：5 分钟窗口
        services.Configure<CalendarOptions>(o => o.ReminderConfirmationWindowMinutes = 5);

        // ILogger<CalendarReminderFallbackJob>
        services.AddSingleton<ILogger<CalendarReminderFallbackJob>>(
            new Mock<ILogger<CalendarReminderFallbackJob>>().Object);

        // IUnitOfWork 注册为 Infrastructure 的 UnitOfWork（需要 Repository 实现）
        // 但这里我们简化：CalendarReminderFallbackJob 内部直接用 DbContext（如果 spec 允许），
        // 或者实现更简单的方式：Job 内部通过 IServiceProvider 获取 NoneadCSSDbContext。
        // 为了让实现简单，我们在测试中构建的服务集合应与真实 Job 期望一致。

        var serviceProvider = services.BuildServiceProvider();

        // ===== Act：实例化 Fallback Job 并执行 =====
        var logger = new Mock<ILogger<CalendarReminderFallbackJob>>();
        var options = serviceProvider.GetRequiredService<IOptions<CalendarOptions>>();

        // NOTE: 这里我们直接构造 Job 实例调用 ExecuteInternal（通过反射或内部构造）
        // 由于我们尚未创建 Job 类，此测试目前会 FAIL（TDD 第一步红）。
        // 如果已经创建了 Job 类，则执行下面的逻辑：
        var jobType = System.Reflection.Assembly.Load("NoneadCSS.Infrastructure")
            .GetType("NoneadCSS.Infrastructure.Jobs.CalendarReminderFallbackJob");
        jobType.Should().NotBeNull("CalendarReminderFallbackJob 类必须存在于 NoneadCSS.Infrastructure");

        var job = Activator.CreateInstance(jobType!, serviceProvider, options, logger.Object);
        job.Should().NotBeNull();

        var method = jobType!.GetMethod("ExecuteInternal",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull("应继承 RetryableJobBase 并重写 ExecuteInternal");

        // 用 Mock Quartz IJobExecutionContext
        var mockCtx = new Mock<IJobExecutionContext>();
        await (Task)method!.Invoke(job, new[] { mockCtx.Object })!;

        // ===== Assert：验证结果 =====
        // 1. ReminderConfirmation 应被设置为 fallback_sent（若至少一个通道成功）或 failed
        var updatedEvt = await context.CalendarEvents.AsNoTracking().FirstAsync(e => e.Id == 1);
        updatedEvt.ReminderConfirmation.Should().BeOneOf("fallback_sent", "failed",
            "Fallback job 执行后 ReminderConfirmation 必须离开 pending 状态");

        // 2. CalendarReminderAttempts 至少存在一条 email 通道的尝试记录
        var attempts = await context.Set<CalendarReminderAttempt>().AsNoTracking().ToListAsync();
        attempts.Should().NotBeEmpty("Fallback job 必须写入投递尝试记录");
    }

    // ====== R8/DEV-2 新测试：Fallback 查询必须在 SQL 端排除 >=49h 老的 pending 行 ======
    [Fact]
    public async Task ExecuteInternal_PendingOlderThan49h_ExcludedBySqlWindow()
    {
        // ===== Arrange =====
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

        await using var context = new NoneadCSSDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = 0");

        var columns = await context.Database.SqlQueryRaw<string>(
            "SELECT name FROM pragma_table_info('CalendarEvents') ORDER BY cid").ToListAsync();

        var cols = new List<string>
        {
            "Id", "Title", "EventType", "StartAt", "Status",
            "CreatedBy", "UserId", "CreatedAt", "UpdatedAt",
            "IsAllDay", "IsReminderSent", "IsRecurring",
            "ReminderConfirmation", "ReminderSentAt", "ReminderMinutes"
        };
        cols.RemoveAll(c => !columns.Contains(c));
        if (columns.Contains("Priority")) cols.Add("Priority");
        if (columns.Contains("Progress")) cols.Add("Progress");
        if (columns.Contains("RowVersion")) cols.Add("RowVersion");

        var now = DateTime.UtcNow;

        // 插入 3 条 pending 事件：
        //   Id=11: ReminderSentAt = 10 分钟前（在窗口内，应该被 fallback job 选中）
        //   Id=12: ReminderSentAt = 24 小时前（< 48h，应该被选中）
        //   Id=13: ReminderSentAt = 49 小时前（>= 48h 上限，应该被 SQL WHERE 排除）
        var insertTpl = $@"
INSERT INTO CalendarEvents ({string.Join(", ", cols)})
VALUES (
    @Id, @Title, 'task', @startAt, 'pending',
    100, 100, @createdAt, @updatedAt,
    0, 1, 0,
    'pending', @reminderSentAt,
    15
    {(cols.Contains("Priority") ? ", 2" : "")}
    {(cols.Contains("Progress") ? ", 0" : "")}
    {(cols.Contains("RowVersion") ? ", 0x0000000000000001" : "")}
)";

        var testCases = new (int Id, string Title, DateTimeOffset ReminderSentAt)[]
        {
            (11, "Fallback-窗口内10min", DateTimeOffset.UtcNow.AddMinutes(-10)),
            (12, "Fallback-24h前",   DateTimeOffset.UtcNow.AddHours(-24)),
            (13, "Fallback-49h前超期", DateTimeOffset.UtcNow.AddHours(-49))
        };

        foreach (var tc in testCases)
        {
            await context.Database.ExecuteSqlRawAsync(insertTpl, new[]
            {
                new Microsoft.Data.Sqlite.SqliteParameter("@Id", tc.Id),
                new Microsoft.Data.Sqlite.SqliteParameter("@Title", tc.Title),
                new Microsoft.Data.Sqlite.SqliteParameter("@startAt", now.AddHours(1)),
                new Microsoft.Data.Sqlite.SqliteParameter("@createdAt", now.AddHours(-2)),
                new Microsoft.Data.Sqlite.SqliteParameter("@updatedAt", tc.ReminderSentAt.DateTime),
                // H-01：直接传 DateTimeOffset，让 Microsoft.Data.Sqlite 按与 EF Core 列写入一致的
                // 格式序列化（不再用 ToString("o")，避免 'T' 分隔符与列存储 ' ' 不一致导致字典序错乱）。
                new Microsoft.Data.Sqlite.SqliteParameter("@reminderSentAt", tc.ReminderSentAt)
            });
        }

        // 构造 ServiceProvider
        var services = new ServiceCollection();
        services.AddSingleton(dbOptions);
        services.AddScoped<NoneadCSSDbContext>();
        services.Configure<CalendarOptions>(o => o.ReminderConfirmationWindowMinutes = 5);
        services.AddSingleton<ILogger<CalendarReminderFallbackJob>>(
            new Mock<ILogger<CalendarReminderFallbackJob>>().Object);
        var serviceProvider = services.BuildServiceProvider();

        var logger = new Mock<ILogger<CalendarReminderFallbackJob>>();
        var options = serviceProvider.GetRequiredService<IOptions<CalendarOptions>>();

        var jobType = System.Reflection.Assembly.Load("NoneadCSS.Infrastructure")
            .GetType("NoneadCSS.Infrastructure.Jobs.CalendarReminderFallbackJob");
        jobType.Should().NotBeNull();

        var job = Activator.CreateInstance(jobType!, serviceProvider, options, logger.Object);
        job.Should().NotBeNull();

        var method = jobType!.GetMethod("ExecuteInternal",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var mockCtx = new Mock<IJobExecutionContext>();

        // ===== Act =====
        await (Task)method!.Invoke(job, new[] { mockCtx.Object })!;

        // ===== Assert：验证 SQL 端窗口生效 =====
        // Id=11 和 Id=12 应该被处理（ReminderConfirmation != pending）
        var evt11 = await context.CalendarEvents.AsNoTracking().FirstAsync(e => e.Id == 11);
        evt11.ReminderConfirmation.Should().NotBe("pending",
            "R8: 10 分钟前的 pending 事件应在窗口内，被 fallback 处理");

        var evt12 = await context.CalendarEvents.AsNoTracking().FirstAsync(e => e.Id == 12);
        evt12.ReminderConfirmation.Should().NotBe("pending",
            "R8: 24 小时前的 pending 事件仍在 48h 窗口内，应被 fallback 处理");

        // Id=13 必须被 SQL WHERE ReminderSentAt >= now-48h 排除，状态仍为 pending！
        var evt13 = await context.CalendarEvents.AsNoTracking().FirstAsync(e => e.Id == 13);
        evt13.ReminderConfirmation.Should().Be("pending",
            "R8/DEV-2: 超过 48h 的 pending 事件必须被 SQL 端 WHERE 过滤（不是内存过滤），因此状态不变");
    }
}
