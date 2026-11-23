using System.Collections.Concurrent;

namespace NoneadCSS.Infrastructure.Services.IMessage;

/// <summary>
/// iMessage 出站限流（Singleton，按渠道作用域计数）。
/// 存在理由：桥接模式持续大出站量会触发 Apple ID 锁定风险（厂商口径仅容忍每天数百条营销式发送），
/// 因此默认 20 条/分钟；超限直接拒绝发送而不是排队，避免无声积累。
/// 窗口用 UTC 整分钟对齐，进程内计数（多实例部署时按实例计，属已知限制）。
/// </summary>
public class IMessageRateLimiter
{
    private readonly ConcurrentDictionary<string, Ring> _rings = new(StringComparer.Ordinal);

    /// <summary>尝试占用一个发送名额。false = 已超过 limit/分钟。</summary>
    public bool TryAcquire(string scopeKey, int limitPerMinute)
    {
        // 故意 fail-closed：limit <= 0 不解释成「不限流」，而是全部拒绝。
        // 因此调用方的配置必须在下发前钳到 >= 1（见 IMessageSettingsProvider 的限流下限钳制），
        // 否则 0 会表现为「程序在跑但一条都发不出去」。
        if (limitPerMinute <= 0) return false;

        var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var ring = _rings.GetOrAdd(scopeKey, _ => new Ring());

        lock (ring)
        {
            if (ring.Minute != minute)
            {
                ring.Minute = minute;
                ring.Count = 0;
            }

            if (ring.Count >= limitPerMinute) return false;
            ring.Count++;
            return true;
        }
    }

    /// <summary>当前分钟已用名额（状态面板用）。</summary>
    public int CurrentCount(string scopeKey)
    {
        var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        if (!_rings.TryGetValue(scopeKey, out var ring)) return 0;
        lock (ring) return ring.Minute == minute ? ring.Count : 0;
    }

    private sealed class Ring
    {
        public long Minute;
        public int Count;
    }
}
