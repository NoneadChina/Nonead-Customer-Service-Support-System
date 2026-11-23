using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 基于数据库唯一索引 + TTL 的分布式锁实现。
/// 原理：对 LockKey 建立唯一索引，获取锁 = 尝试 INSERT；
/// 成功则获得锁；唯一索引冲突表示锁已被持有。
/// 锁通过 ExpiresAt 自动过期（默认 TTL 5 分钟），过期后下次获取时会清理。
/// </summary>
public class DistributedLockService : IDistributedLockService
{
    private readonly NoneadCSSDbContext _dbContext;
    private readonly ILogger<DistributedLockService> _logger;

    /// <summary>默认锁过期时间（5 分钟）。同步任务通常在 5 分钟内完成。</summary>
    private const int DefaultLockTtlSeconds = 300;

    private static readonly string _ownerPrefix =
        $"{Environment.ProcessId}_{Environment.CurrentManagedThreadId}_{Guid.NewGuid():N}";

    public DistributedLockService(NoneadCSSDbContext dbContext, ILogger<DistributedLockService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(string lockKey, int timeoutMs = 0)
    {
        if (string.IsNullOrEmpty(lockKey))
            throw new ArgumentException("锁键不能为空", nameof(lockKey));

        var expiresAt = DateTime.UtcNow.AddSeconds(DefaultLockTtlSeconds);
        var owner = $"{_ownerPrefix}_{Guid.NewGuid():N}";

        using var cts = new CancellationTokenSource(Math.Max(0, timeoutMs));
        var startedAt = DateTime.UtcNow;
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                // 清理过期锁（抢占式，下次获取时顺便清理）
                try
                {
                    var expired = await _dbContext.DistributedLocks
                        .Where(l => l.LockKey == lockKey && l.ExpiresAt < DateTime.UtcNow)
                        .ExecuteDeleteAsync();
                    if (expired > 0)
                        _logger.LogInformation("清理 {Count} 条过期分布式锁：{LockKey}", expired, lockKey);
                }
                catch (Exception ex) when (ex is not DbUpdateException)
                {
                    // ExecuteDeleteAsync 在部分 Provider（如内存数据库）可能不支持，此时退回查询 + 逐个 Delete
                    var stale = await _dbContext.DistributedLocks
                        .Where(l => l.LockKey == lockKey && l.ExpiresAt < DateTime.UtcNow)
                        .ToListAsync();
                    if (stale.Any())
                    {
                        _dbContext.DistributedLocks.RemoveRange(stale);
                        try { await _dbContext.SaveChangesAsync(); }
                        catch (Exception fallbackEx)
                        {
                            _logger.LogDebug(fallbackEx, "清理过期锁 SaveChanges 失败，继续尝试获取锁：{LockKey}", lockKey);
                            _dbContext.ChangeTracker.Clear();
                        }
                    }
                }

                // 尝试 INSERT 获取锁
                var newLock = new DistributedLock
                {
                    LockKey = lockKey,
                    Owner = owner,
                    AcquiredAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt
                };
                _dbContext.DistributedLocks.Add(newLock);
                await _dbContext.SaveChangesAsync();

                _logger.LogDebug("获取分布式锁成功：{LockKey} (owner={Owner}, attempt={Attempt})", lockKey, owner, attempt);
                return new DistributedLockHandle(_dbContext, lockKey, owner, _logger);
            }
            catch (DbUpdateException ex)
            {
                // 唯一索引冲突 = 锁已被持有
                if (IsUniqueViolation(ex))
                {
                    _logger.LogDebug("获取分布式锁冲突（已被持有）：{LockKey}, attempt={Attempt}", lockKey, attempt);
                    _dbContext.ChangeTracker.Clear();

                    // 超时检查
                    if (timeoutMs == 0 || (DateTime.UtcNow - startedAt).TotalMilliseconds >= timeoutMs)
                        return null;

                    try
                    {
                        // 100ms + 轻微抖动后重试
                        await Task.Delay(100 + Random.Shared.Next(50), cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                    continue;
                }

                _logger.LogError(ex, "获取分布式锁发生数据库异常：{LockKey}", lockKey);
                _dbContext.ChangeTracker.Clear();
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分布式锁异常：{LockKey}", lockKey);
                _dbContext.ChangeTracker.Clear();
                return null;
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // 仅匹配唯一索引/唯一约束冲突特征串，避免将外键约束、CHECK 约束等其他约束冲突误判为锁冲突。
        // 覆盖 SQL Server / SQLite / PostgreSQL / MySQL 的唯一冲突消息：
        //   SQL Server : "Cannot insert duplicate key row in object ... with unique index ..."
        //   SQLite     : "UNIQUE constraint failed: ..."
        //   PostgreSQL : "duplicate key value violates unique constraint ..."
        //   MySQL      : "Duplicate entry 'x' for key 'y'"
        var msg = (ex.InnerException?.Message ?? ex.Message).ToLowerInvariant();
        return msg.Contains("unique constraint")
               || msg.Contains("unique index")
               || msg.Contains("unique_violation")
               || msg.Contains("duplicate key")
               || msg.Contains("duplicate entry")
               || msg.Contains("cannot insert duplicate");
    }
}

/// <summary>
/// 分布式锁句柄实现，Dispose 时释放锁。
/// </summary>
public class DistributedLockHandle : IDistributedLockHandle
{
    private readonly NoneadCSSDbContext _dbContext;
    private readonly string _owner;
    private readonly ILogger _logger;
    private bool _disposed;

    public string LockKey { get; }

    public DistributedLockHandle(NoneadCSSDbContext dbContext, string lockKey, string owner, ILogger logger)
    {
        _dbContext = dbContext;
        LockKey = lockKey;
        _owner = owner;
        _logger = logger;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // 仅释放自己持有的锁（防止误删其他进程续期的锁）
            // 先尝试 EF Core 批量 ExecuteDeleteAsync，部分 Provider 不支持时退回 RemoveRange + SaveChanges
            try
            {
                var deleted = await _dbContext.DistributedLocks
                    .Where(l => l.LockKey == LockKey && l.Owner == _owner)
                    .ExecuteDeleteAsync();
                // deleted == 0 可能已过期被清理，无需处理
            }
            catch (Exception ex) when (ex is not DbUpdateException)
            {
                var mine = await _dbContext.DistributedLocks
                    .Where(l => l.LockKey == LockKey && l.Owner == _owner)
                    .ToListAsync();
                if (mine.Any())
                {
                    _dbContext.DistributedLocks.RemoveRange(mine);
                    try { await _dbContext.SaveChangesAsync(); }
                    catch (Exception fallbackEx)
                    {
                        // 回退路径 SaveChanges 失败也仅记日志，避免影响主流程
                        _logger.LogDebug(fallbackEx, "释放锁回退 SaveChanges 失败: LockKey={LockKey}, Owner={Owner}", LockKey, _owner);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 释放失败不抛异常（避免影响主流程），但记录日志便于运维感知
            _logger.LogWarning(ex, "释放分布式锁失败: LockKey={LockKey}, Owner={Owner}", LockKey, _owner);
        }
    }
}
