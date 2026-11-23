namespace NoneadCSS.Application.Common;

/// <summary>
/// Telegram 出站速率限制器（按 bot 共享的令牌桶 + 每 chat 间隔 + 429/420 退避登记）。
/// 官方限制：全局 ≤30 条/秒，同一 chat 约 1 条/秒，同一群 ≤20 条/分钟。
/// 429 带 parameters.retry_after：退避登记后由调用方决定等待或让消息队列延后重试（见 MaxLocalBackoffSeconds）。
/// 纯内存实现，时钟可注入，便于单元测试。注册为 Singleton 以便多个客户端实例共享同一配额。
/// </summary>
public sealed class TelegramThrottle
{
    private const long WindowMilliseconds = 1000;

    private readonly object _gate = new();
    private readonly Queue<long> _globalStamps = new();
    private readonly Dictionary<long, Queue<long>> _chatStamps = [];
    private readonly Dictionary<long, long> _chatBlockedUntilMs = [];
    private long _globalBlockedUntilMs;

    private readonly Func<long> _clock;
    private int _globalPerSeconds;
    private int _perChatPerSeconds;

    public TelegramThrottle(int globalPerSeconds = 25, int perChatPerSeconds = 1, Func<long>? unixClock = null)
    {
        _globalPerSeconds = globalPerSeconds > 0 ? globalPerSeconds : 25;
        _perChatPerSeconds = perChatPerSeconds > 0 ? perChatPerSeconds : 1;
        _clock = unixClock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>配置变更后同步配额（设置保存时调用）</summary>
    public void Configure(int globalPerSeconds, int perChatPerSeconds)
    {
        lock (_gate)
        {
            if (globalPerSeconds > 0)
            {
                _globalPerSeconds = globalPerSeconds;
            }

            if (perChatPerSeconds > 0)
            {
                _perChatPerSeconds = perChatPerSeconds;
            }
        }
    }

    /// <summary>
    /// 只读查询：当前需要等待的毫秒数，0 表示立即可发，&gt;0 表示被限流（含 retry_after 退避）。
    /// 注意：本方法不做任何记账，只用于状态展示与诊断，不在发送路径上——
    /// 发送路径必须用 <see cref="TryReserveSend"/>，否则"先查再发"在多坐席并发时会双双突破配额（审计 #6）。
    /// </summary>
    public int PeekWaitMilliseconds(long chatId)
    {
        lock (_gate)
        {
            var now = _clock();
            var wait = 0L;

            if (_globalBlockedUntilMs > now)
            {
                wait = Math.Max(wait, _globalBlockedUntilMs - now);
            }

            if (_chatBlockedUntilMs.TryGetValue(chatId, out var chatBlockedUntil) && chatBlockedUntil > now)
            {
                wait = Math.Max(wait, chatBlockedUntil - now);
            }

            TrimWindow(_globalStamps, now);
            if (_globalStamps.Count >= _globalPerSeconds)
            {
                var oldestGlobal = _globalStamps.Peek();
                wait = Math.Max(wait, oldestGlobal + WindowMilliseconds - now);
            }

            if (_chatStamps.TryGetValue(chatId, out var chatQueue))
            {
                TrimWindow(chatQueue, now);
                if (chatQueue.Count >= _perChatPerSeconds)
                {
                    var oldestChat = chatQueue.Peek();
                    wait = Math.Max(wait, oldestChat + WindowMilliseconds - now);
                }
            }

            return wait > int.MaxValue ? int.MaxValue : (int)wait;
        }
    }

    /// <summary>
    /// 原子预约：在同一个锁里完成"检查配额 + 记账"，返回需要等待的毫秒数（0 表示已就地占用当前时刻）。
    /// Granted=false 表示需要等待的时长超过 maxWaitMilliseconds，此时不做任何记账，由调用方把消息退回队列稍后重试。
    /// 记账时刻取"最早合法槽位"（可以是未来）：并发的第二个坐席会看到该槽位已被占用而继续往后排，
    /// 因此不会出现 PeekWait→Delay→RegisterSend 三步之间互相穿透、突破全局 25 条/秒或单 chat 1 条/秒的情况（审计 #6）。
    /// 槽位搜索只读（ScanWindow），每轮都把候选推到"窗口内最早那条戳 + 1 秒"之后，故必然收敛；被拒时不留下任何记账。
    /// </summary>
    public ThrottleReservation TryReserveSend(long chatId, int maxWaitMilliseconds)
    {
        lock (_gate)
        {
            var now = _clock();
            var slot = now;

            // retry_after 退避窗口优先：退避未结束前不得提前占用配额
            if (_globalBlockedUntilMs > slot)
            {
                slot = _globalBlockedUntilMs;
            }

            if (_chatBlockedUntilMs.TryGetValue(chatId, out var chatBlockedUntil) && chatBlockedUntil > slot)
            {
                slot = chatBlockedUntil;
            }

            if (!_chatStamps.TryGetValue(chatId, out var chatQueue))
            {
                chatQueue = new Queue<long>();
                _chatStamps[chatId] = chatQueue;
            }

            // 只按 now 清理过期戳；搜索候选槽位期间不得改动队列——
            // 否则一次"因超过本地上限而被拒绝"的预约会把还没出窗口的记账抹掉，等于把配额白送给后来者（审计 #6）。
            TrimWindow(_globalStamps, now);
            TrimWindow(chatQueue, now);

            while (true)
            {
                var (globalCount, globalOldest) = ScanWindow(_globalStamps, slot);
                var (chatCount, chatOldest) = ScanWindow(chatQueue, slot);

                var next = long.MinValue;
                if (globalCount >= _globalPerSeconds)
                {
                    next = Math.Max(next, globalOldest + WindowMilliseconds);
                }

                if (chatCount >= _perChatPerSeconds)
                {
                    next = Math.Max(next, chatOldest + WindowMilliseconds);
                }

                if (next == long.MinValue)
                {
                    break;
                }

                // 时钟回退或同毫秒并发时也必须前进，避免原地打转
                slot = next > slot ? next : slot + 1;
            }

            var wait = slot - now;
            if (wait > maxWaitMilliseconds)
            {
                return new ThrottleReservation(false, wait > int.MaxValue ? int.MaxValue : (int)wait);
            }

            _globalStamps.Enqueue(slot);
            chatQueue.Enqueue(slot);
            return new ThrottleReservation(true, wait > int.MaxValue ? int.MaxValue : (int)wait);
        }
    }

    /// <summary>
    /// 记录一次实际发出请求（占用全局与单 chat 配额）。
    /// 仅用于"等待已由外部条件决定"的场合（例如 429/420 之后按 Telegram 给的 retry_after 等完了再补记账）；
    /// 发送前的常规路径一律走 <see cref="TryReserveSend"/>，不要 Peek 之后再 RegisterSend。
    /// </summary>
    public void RegisterSend(long chatId)
    {
        lock (_gate)
        {
            var now = _clock();
            TrimWindow(_globalStamps, now);
            _globalStamps.Enqueue(now);

            if (!_chatStamps.TryGetValue(chatId, out var chatQueue))
            {
                chatQueue = new Queue<long>();
                _chatStamps[chatId] = chatQueue;
            }

            TrimWindow(chatQueue, now);
            chatQueue.Enqueue(now);
        }
    }

    /// <summary>
    /// 收到 429/420：登记退避。420 只退避该 chat，429 退避整个 bot（全局）。
    /// </summary>
    public void RegisterRetryAfter(long chatId, int retryAfterSeconds, bool chatScoped)
    {
        if (retryAfterSeconds <= 0)
        {
            retryAfterSeconds = 1;
        }

        lock (_gate)
        {
            var until = _clock() + retryAfterSeconds * 1000L;
            if (chatScoped)
            {
                _chatBlockedUntilMs[chatId] = Math.Max(_chatBlockedUntilMs.TryGetValue(chatId, out var existing) ? existing : 0, until);
            }
            else
            {
                _globalBlockedUntilMs = Math.Max(_globalBlockedUntilMs, until);
            }
        }
    }

    /// <summary>清空配额与退避（测试与配置重载使用）</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _globalStamps.Clear();
            _chatStamps.Clear();
            _chatBlockedUntilMs.Clear();
            _globalBlockedUntilMs = 0;
        }
    }

    private static void TrimWindow(Queue<long> stamps, long now)
    {
        while (stamps.Count > 0 && now - stamps.Peek() >= WindowMilliseconds)
        {
            stamps.Dequeue();
        }
    }

    /// <summary>
    /// 只读统计：落在 slot 这个 1 秒窗口内（stamp &gt; slot - 1000）的戳数，以及其中最早的一条（用于推导下一个合法槽位）。
    /// 别的坐席已预约到未来（stamp &gt; slot）时同样计入，所以并发预约会依次往后排，不会叠在同一毫秒。
    /// Count 为 0 时 Oldest 无意义（调用方只在 Count ≥ 上限时取用它）。
    /// </summary>
    private static (int Count, long Oldest) ScanWindow(Queue<long> stamps, long slot)
    {
        var count = 0;
        var oldest = long.MaxValue;

        foreach (var stamp in stamps)
        {
            if (stamp > slot - WindowMilliseconds)
            {
                count++;
                if (stamp < oldest)
                {
                    oldest = stamp;
                }
            }
        }

        return (count, oldest);
    }
}

/// <summary>
/// 一次原子预约的结果。Granted=true 时配额已记账（戳记在 WaitMilliseconds 之后的合法槽位上，调用方等到那时再发）；
/// Granted=false 时未记账，WaitMilliseconds 是"至少要等多久才合法"，超过调用方愿意等的上限。
/// </summary>
public readonly record struct ThrottleReservation(bool Granted, int WaitMilliseconds);
