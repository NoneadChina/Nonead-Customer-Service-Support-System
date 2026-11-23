using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Services;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// H-8：分布式锁服务单元测试（SQLite 内存，支持唯一约束）
/// 覆盖 TryAcquire 成功/失败并发、Handle 释放、过期锁清理
/// </summary>
public class DistributedLockServiceTests : IAsyncLifetime
{
    private NoneadCSSDbContext _dbContext = null!;
    private ILogger<DistributedLockService> _logger = null!;
    private DistributedLockService _service = null!;
    private DbConnection _dbConnection = null!;

    public async Task InitializeAsync()
    {
        // 使用 SQLite 内存模式 + 共享连接，保证跨 DbContext 共享数据并支持唯一约束
        _dbConnection = new SqliteConnection("Filename=:memory:");
        await _dbConnection.OpenAsync();
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(_dbConnection)
            .Options;

        _dbContext = new NoneadCSSDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _logger = Mock.Of<ILogger<DistributedLockService>>();
        _service = new DistributedLockService(_dbContext, _logger);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _dbConnection.DisposeAsync();
    }

    private NoneadCSSDbContext CreateFreshContext()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(_dbConnection)
            .Options;
        return new NoneadCSSDbContext(options);
    }

    [Fact]
    public async Task TryAcquireAsync_FreshLock_AcquiresSuccessfully()
    {
        // Act
        await using var handle = await _service.TryAcquireAsync("lock:fresh:1", timeoutMs: 0);

        // Assert
        handle.Should().NotBeNull();
        handle!.LockKey.Should().Be("lock:fresh:1");

        // 验证 DB 中确实写入了锁记录
        await using var verifyCtx = CreateFreshContext();
        var persisted = await verifyCtx.Set<DistributedLock>()
            .FirstOrDefaultAsync(l => l.LockKey == "lock:fresh:1");
        persisted.Should().NotBeNull();
        persisted!.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task TryAcquireAsync_SameKeyAlreadyHeld_ReturnsNullImmediately()
    {
        // Arrange
        await using var first = await _service.TryAcquireAsync("lock:contended", 0);
        first.Should().NotBeNull();

        // Act
        await using var secondCtx = CreateFreshContext();
        var secondSvc = new DistributedLockService(secondCtx, _logger);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var second = await secondSvc.TryAcquireAsync("lock:contended", 0);
        sw.Stop();

        // Assert
        second.Should().BeNull("同 Key 锁被持有时应立即返回 null");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "timeoutMs=0 应立即返回");
    }

    [Fact]
    public async Task TryAcquireAsync_ExpiredLock_ReclaimsSuccessfully()
    {
        // Arrange: 手动插入一个已过期的锁（通过直接写入 DbContext）
        await using var ctx = CreateFreshContext();
        await ctx.Set<DistributedLock>().AddAsync(new DistributedLock
        {
            LockKey = "lock:expired",
            Owner = "stale-owner",
            AcquiredAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
        });
        await ctx.SaveChangesAsync();

        // Act：创建新 Service 用新 Context（避免本地追踪缓存）尝试获取
        await using var reclaimCtx = CreateFreshContext();
        var reclaimSvc = new DistributedLockService(reclaimCtx, _logger);
        await using var handle = await reclaimSvc.TryAcquireAsync("lock:expired", 0);

        // Assert
        handle.Should().NotBeNull("过期锁应被清理并成功获取");
    }

    [Fact]
    public async Task HandleDisposeAsync_ReleasesLock()
    {
        // Arrange
        const string key = "lock:release";
        var handle = await _service.TryAcquireAsync(key, 0);
        handle.Should().NotBeNull();

        // Act
        await handle!.DisposeAsync();

        // Assert：再次尝试用另一个 context 能成功获取
        await using var anotherCtx = CreateFreshContext();
        var anotherSvc = new DistributedLockService(anotherCtx, _logger);
        await using var secondHandle = await anotherSvc.TryAcquireAsync(key, 0);
        secondHandle.Should().NotBeNull("释放后应可被重新获取");
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentKeys_Independent()
    {
        // Act
        await using var hA = await _service.TryAcquireAsync("lock:A", 0);
        await using var hB = await _service.TryAcquireAsync("lock:B", 0);
        await using var hC = await _service.TryAcquireAsync("lock:C", 0);

        // Assert
        hA.Should().NotBeNull();
        hB.Should().NotBeNull();
        hC.Should().NotBeNull();
    }

    [Fact]
    public void DistributedLockEntity_PropertyShape()
    {
        // 防御性：验证实体字段符合预期（特别是 TTL 相关）
        var lockEntity = new DistributedLock
        {
            LockKey = "key:validation",
            Owner = "owner1",
            AcquiredAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 3, 1, 0, 5, 0, DateTimeKind.Utc),
        };

        lockEntity.LockKey.Should().NotBeNullOrEmpty();
        lockEntity.Owner.Should().NotBeNullOrEmpty();
        (lockEntity.ExpiresAt - lockEntity.AcquiredAt).TotalMinutes.Should().Be(5);
    }
}
