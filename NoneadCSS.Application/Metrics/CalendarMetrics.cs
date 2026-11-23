using System.Diagnostics.Metrics;

namespace NoneadCSS.Application.Metrics;

/// <summary>
/// 日历模块业务监控指标定义（基于 System.Diagnostics.Metrics，.NET 内置无需额外 NuGet 包）。
/// 采集后可通过 OTLP 导出至 Grafana/Prometheus，或通过 dotnet-counters 工具实时查看。
/// </summary>
public static class CalendarMetrics
{
    /// <summary> Meter 名称（遵循 OpenTelemetry 命名规范：组织.模块）</summary>
    public const string MeterName = "NoneadCSS.Calendar";

    /// <summary> Meter 版本</summary>
    public const string MeterVersion = "1.0";

    /// <summary> 全局 Meter 实例（静态字段保证整个进程共享）</summary>
    public static readonly Meter Meter = new(MeterName, MeterVersion);

    /// <summary>
    /// 提醒发送总数计数器。
    /// 标签：channel=inapp/email/signalr, status=sent/failed/skipped
    /// </summary>
    public static readonly Counter<int> RemindersSent =
        Meter.CreateCounter<int>("calendar_reminder_sent_total");

    /// <summary>
    /// 冲突检测计数器。
    /// 标签：severity=soft/hard
    /// </summary>
    public static readonly Counter<int> ConflictsDetected =
        Meter.CreateCounter<int>("calendar_conflict_detected_total");

    /// <summary>
    /// 重复日程展开实例数计数器。
    /// 标签：source=realtime/cache（实时展开 vs 缓存命中）
    /// </summary>
    public static readonly Counter<int> RecurrenceExpanded =
        Meter.CreateCounter<int>("calendar_recurrence_expanded_instances");

    /// <summary>
    /// 缓存访问计数器。
    /// 标签：result=hit/miss, scope=list/overview
    /// </summary>
    public static readonly Counter<int> CacheAccess =
        Meter.CreateCounter<int>("calendar_cache_access_total");

    /// <summary>
    /// 记录提醒发送成功。
    /// </summary>
    public static void RecordReminderSent(string channel, string status = "sent")
    {
        RemindersSent.Add(1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("status", status));
    }

    /// <summary>
    /// 记录冲突检测结果。
    /// </summary>
    public static void RecordConflict(string severity)
    {
        ConflictsDetected.Add(1,
            new KeyValuePair<string, object?>("severity", severity));
    }

    /// <summary>
    /// 记录重复日程展开实例数。
    /// </summary>
    public static void RecordRecurrenceExpansion(int count, string source = "realtime")
    {
        if (count > 0)
        {
            RecurrenceExpanded.Add(count,
                new KeyValuePair<string, object?>("source", source));
        }
    }

    /// <summary>
    /// 记录缓存访问结果。
    /// </summary>
    public static void RecordCacheAccess(string result, string scope)
    {
        CacheAccess.Add(1,
            new KeyValuePair<string, object?>("result", result),
            new KeyValuePair<string, object?>("scope", scope));
    }
}
