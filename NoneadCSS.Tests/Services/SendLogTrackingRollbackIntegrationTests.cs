using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 回归测试：事务回滚后，同一 DbContext 作用域内再次插入 SendLog 不得撞 IdentityMap。
///
/// 生产证据（Production/logs/log-20260921.log 01:48:29）：
///   InvalidOperationException: The instance of entity type 'SendLog' cannot be tracked because
///   another instance with the same key value for {'Id'} is already being tracked.
///   栈顶 PropagateResults → SetStoreGeneratedValue → DetectKeyChange → UpdateIdentityMap → IdentityMap.Add
///   → 说明是 INSERT 回填主键时撞车，而不是 Add/Attach。
///
/// 触发链路（FetchEmailJob 每邮箱一个 scope）：
///   会话612 的工作流在事务内入队邮件（SendLog 落库，EF 回填真实 Id）
///   → 同工作流后续动作失败，事务回滚
///   → 跟踪器仍把该实例当作 Unchanged 持有（回滚不回退 EF 的跟踪状态）
///   → 同 scope 处理会话613 再次入队；SQLite 会复用被回滚的 rowid（实测：
///      带 AUTOINCREMENT 的表在事务回滚后仍复用 rowid，因为 sqlite_sequence 随事务回退）
///   → INSERT 回填出已被占用的 Id → IdentityMap 抛冲突 → 邮件静默发送失败。
///
/// 本用例锁定不变量：**事务一旦回滚，跟踪器必须不再持有该事务中已持久化的实例。**
/// </summary>
public class SendLogTrackingRollbackIntegrationTests : IAsyncLifetime
{
    private string _dbFile = null!;
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    public async Task InitializeAsync()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"sendlog-rollback-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NoneadCSSDbContext>(o => o.UseSqlite($"Data Source={_dbFile}"));
        services.AddScoped<ISendLogRepository, SendLogRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        _serviceProvider = services.BuildServiceProvider();

        _scope = _serviceProvider.CreateScope();
        var context = _scope.ServiceProvider.GetRequiredService<NoneadCSSDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbFile))
        {
            File.Delete(_dbFile);
        }
    }

    private static SendLog NewQueuedMail(string to) => new()
    {
        MailboxId = 2,
        MessageId = string.Empty,
        Email = to,
        Subject = "[#613] 欢迎",
        Body = "<p>welcome</p>",
        MailType = SendLogMailType.EmailToCustomer,
        Status = SendLogStatus.Accepted,
        CreatedAt = DateTime.UtcNow,
        AttemptCount = 0,
        OpenCount = 0
    };

    [Fact]
    public async Task RollbackAsync_ThenEnqueueAgain_MustNotCollideOnReusedRowId()
    {
        var uow = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var context = _scope.ServiceProvider.GetRequiredService<NoneadCSSDbContext>();

        int rolledBackId;
        await using (var tx = await uow.BeginTransactionAsync())
        {
            var first = NewQueuedMail("first@example.com");
            await context.SendLogs.AddAsync(first);
            await uow.SaveChangesAsync();

            rolledBackId = first.Id;
            rolledBackId.Should().BeGreaterThan(0, "INSERT 成功后 EF 必须回填真实主键（生产日志中为 1776）");

            await tx.RollbackAsync();
        }

        // 同 scope 内再次入队：SQLite 复用被回滚的 rowid，INSERT 回填时会撞上陈旧跟踪实例
        var second = NewQueuedMail("second@example.com");
        await context.SendLogs.AddAsync(second);

        var act = async () => await uow.SaveChangesAsync();

        await act.Should().NotThrowAsync(
            "事务回滚后跟踪器不得再持有该事务中已持久化的 SendLog 实例，" +
            "否则同 scope 内下一次入队会因 SQLite 复用 rowid 而抛 IdentityMap 冲突（生产缺陷，会话613）");
    }
}
