#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace NoneadCSS.Application.Metrics;

/// <summary>
/// 基于 <see cref="MeterListener"/> 的轻量级指标采集器：
/// 订阅 <see cref="CalendarMetrics.Meter"/> 下的所有 Counter，在线程安全字典中累加增量，
/// 并通过 <see cref="GetPrometheusText"/> 输出 Prometheus 文本格式，供 /metrics 端点抓取。
/// 不引入额外 NuGet 包（prometheus-net / OpenTelemetry Exporter），
/// 适合单实例部署的 NoneadCSS 当前阶段；未来横向扩展可替换为 OTLP 导出。
/// </summary>
public sealed class CalendarMetricsExporter : IHostedService, IDisposable
{
    /// <summary>键 = metric_name|tag1=v1,tag2=v2</summary>
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private MeterListener? _listener;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = OnInstrumentPublished
        };
        // 仅订阅 int 类型的 Counter（当前所有日历指标均为 Counter&lt;int&gt;）
        _listener.SetMeasurementEventCallback<int>(OnMeasurement);
        _listener.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }

    private static void OnInstrumentPublished(Instrument instrument, MeterListener listener)
    {
        if (instrument.Meter.Name == CalendarMetrics.MeterName
            && instrument is Counter<int>)
        {
            listener.EnableMeasurementEvents(instrument);
        }
    }

    private void OnMeasurement(
        Instrument instrument,
        int measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        var key = BuildKey(instrument.Name, tags);
        // AddOrUpdate 的 update 工厂必须是纯函数（不能抛异常），用 Interlocked 累加
        _counters.AddOrUpdate(key, measurement, (_, existing) => existing + measurement);
    }

    /// <summary>
    /// 输出当前所有计数器快照为 Prometheus 0.0.4 文本格式。
    /// </summary>
    public string GetPrometheusText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HELP calendar_reminder_sent_total Total calendar reminders sent (by channel/status).");
        sb.AppendLine("# TYPE calendar_reminder_sent_total counter");
        sb.AppendLine("# HELP calendar_conflict_detected_total Total calendar conflicts detected (by severity).");
        sb.AppendLine("# TYPE calendar_conflict_detected_total counter");
        sb.AppendLine("# HELP calendar_recurrence_expanded_instances Total recurrence instances expanded (by source).");
        sb.AppendLine("# TYPE calendar_recurrence_expanded_instances counter");
        sb.AppendLine("# HELP calendar_cache_access_total Total calendar cache accesses (by result/scope).");
        sb.AppendLine("# TYPE calendar_cache_access_total counter");

        foreach (var kv in _counters)
        {
            var (metric, labels) = SplitKey(kv.Key);
            if (labels.Length > 0)
            {
                sb.Append(metric).Append('{').Append(labels).Append("} ")
                  .Append(kv.Value.ToString(CultureInfo.InvariantCulture))
                  .AppendLine();
            }
            else
            {
                sb.Append(metric).Append(' ')
                  .Append(kv.Value.ToString(CultureInfo.InvariantCulture))
                  .AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string BuildKey(string metricName, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (tags.IsEmpty)
            return metricName;

        var sb = new StringBuilder(metricName).Append('|');
        var first = true;
        foreach (var t in tags)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(t.Key).Append('=').Append(t.Value);
        }
        return sb.ToString();
    }

    private static (string Metric, string Labels) SplitKey(string key)
    {
        var idx = key.IndexOf('|');
        if (idx < 0) return (key, string.Empty);
        return (key.Substring(0, idx), key.Substring(idx + 1));
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _counters.Clear();
    }
}
