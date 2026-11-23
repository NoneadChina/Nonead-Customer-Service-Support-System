#nullable enable
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Metrics;

namespace NoneadCSS.Application.Services.Calendar;

public class MemoryCalendarQueryCache : ICalendarQueryCache
{
    private const string ListKeyPrefix = "cal:list:";
    private const string OverviewKeyPrefix = "cal:overview:";
    private const string UserVersionPrefix = "cal:ver:";

    /// <summary>用户版本表上限：防止长期运行后用户条目无限增长导致内存泄漏（超出时清空重建）</summary>
    private const int MaxUserVersions = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = null
    };

    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCalendarQueryCache> _logger;
    private readonly ConcurrentDictionary<int, long> _userVersions = new();

    public MemoryCalendarQueryCache(IMemoryCache cache, ILogger<MemoryCalendarQueryCache> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);
        _cache = cache;
        _logger = logger;
    }

    public async Task<(List<CalendarEventDto> Items, int Total)> GetListAsync(
        int currentUserId,
        CalendarEventQuery query,
        Func<Task<(List<CalendarEventDto> Items, int Total)>> factory,
        CancellationToken ct = default)
    {
        var version = GetOrCreateUserVersion(currentUserId);
        var key = BuildListCacheKey(currentUserId, version, query);

        if (_cache.TryGetValue(key, out (List<CalendarEventDto> Items, int Total) cached))
        {
            _logger.LogDebug("日历缓存命中(list): user_id={UserId}", currentUserId);
            CalendarMetrics.RecordCacheAccess("hit", "list");
            return cached;
        }

        _logger.LogDebug("日历缓存未命中(list): user_id={UserId}", currentUserId);
        CalendarMetrics.RecordCacheAccess("miss", "list");
        var result = await factory().ConfigureAwait(false);

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        };
        _cache.Set(key, result, options);

        return result;
    }

    public async Task<CalendarOverviewResponse> GetOverviewAsync(
        int currentUserId,
        Func<Task<CalendarOverviewResponse>> factory,
        CancellationToken ct = default)
    {
        var version = GetOrCreateUserVersion(currentUserId);
        var key = BuildOverviewCacheKey(currentUserId, version);

        if (_cache.TryGetValue(key, out CalendarOverviewResponse? cached) && cached != null)
        {
            _logger.LogDebug("日历缓存命中(overview): user_id={UserId}", currentUserId);
            CalendarMetrics.RecordCacheAccess("hit", "overview");
            return cached;
        }

        _logger.LogDebug("日历缓存未命中(overview): user_id={UserId}", currentUserId);
        CalendarMetrics.RecordCacheAccess("miss", "overview");
        var result = await factory().ConfigureAwait(false);

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
        };
        _cache.Set(key, result, options);

        return result;
    }

    public void InvalidateAllForUser(int userId)
    {
        var newVersion = _userVersions.AddOrUpdate(
            userId,
            _ => 1,
            (_, existing) => existing + 1);
        TrimUserVersionsIfNeeded();
        _logger.LogDebug("日历缓存失效: user_id={UserId}, new_version={Version}", userId, newVersion);
    }

    private long GetOrCreateUserVersion(int userId)
    {
        var version = _userVersions.GetOrAdd(userId, 0);
        TrimUserVersionsIfNeeded();
        return version;
    }

    /// <summary>
    /// 版本表写入后做上限保护：超出上限时清空并重新生成。
    /// 旧缓存条目携带旧 version，清空后不会再被命中，条目本身会在 1 分钟内自动过期回收。
    /// </summary>
    private void TrimUserVersionsIfNeeded()
    {
        if (_userVersions.Count <= MaxUserVersions) return;
        _userVersions.Clear();
        _logger.LogWarning("日历缓存用户版本表条目数超出上限（{Limit}），已清空重建。", MaxUserVersions);
    }

    private static string BuildListCacheKey(int userId, long version, CalendarEventQuery query)
    {
        var queryJson = JsonSerializer.Serialize(query, JsonOptions);
        var raw = $"{userId}:{version}:{queryJson}";
        var hash = Sha256Hex(raw);
        return $"{ListKeyPrefix}{userId}:{version}:{hash}";
    }

    private static string BuildOverviewCacheKey(int userId, long version)
    {
        return $"{OverviewKeyPrefix}{userId}:{version}";
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
