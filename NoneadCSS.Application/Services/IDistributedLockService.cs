namespace NoneadCSS.Application.Services;

/// <summary>
/// 分布式锁服务，用于跨进程/多实例的互斥操作。
/// 实现方式：基于数据库 advisory lock（通过 SELECT ... FOR UPDATE NOWAIT 模拟），
/// 不依赖 Redis 等外部基础设施，适用于单机与横向扩展场景。
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// 尝试获取指定键的分布式锁。
    /// </summary>
    /// <param name="lockKey">锁的唯一标识（如 "outlook_sync:user:123"）</param>
    /// <param name="timeoutMs">等待超时时间（毫秒）。0 表示不等待，立即返回。</param>
    /// <returns>锁句柄；获取失败返回 null。Dispose 时释放锁。</returns>
    Task<IDistributedLockHandle?> TryAcquireAsync(string lockKey, int timeoutMs = 0);
}

/// <summary>
/// 分布式锁句柄，Dispose 释放锁。
/// </summary>
public interface IDistributedLockHandle : IAsyncDisposable
{
    /// <summary>锁键</summary>
    string LockKey { get; }
}
