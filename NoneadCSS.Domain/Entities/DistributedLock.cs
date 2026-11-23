using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 分布式锁记录（基于数据库唯一索引 + TTL 实现）。
/// 原理：对 LockKey 建立唯一索引，获取锁 = INSERT 一行记录（成功即获得锁）；
/// 释放锁 = DELETE 该行记录；过期锁由清理任务或下次获取时清理。
/// </summary>
public class DistributedLock
{
    public int Id { get; set; }

    /// <summary>锁键（唯一索引），如 "outlook_sync:user:123"</summary>
    [MaxLength(256)]
    public string LockKey { get; set; } = string.Empty;

    /// <summary>持有者标识（进程ID + 线程ID + 随机数）</summary>
    [MaxLength(64)]
    public string Owner { get; set; } = string.Empty;

    /// <summary>获取锁的时间（UTC）</summary>
    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;

    /// <summary>锁过期时间（UTC）。过期后其他进程可抢占。</summary>
    public DateTime ExpiresAt { get; set; }
}
