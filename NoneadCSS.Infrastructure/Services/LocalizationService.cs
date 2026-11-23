using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 本地化服务实现：优先从数据库读取翻译（通过后台预热缓存），回退到资源文件
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly IStringLocalizer _localizer;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LocalizationService> _logger;
    private readonly TimeSpan _cacheDuration;

    public static IReadOnlyList<string> SupportedCultures { get; } = new[] { "en", "zh-Hans" };

    /// <summary>
    /// 初始化本地化服务
    /// </summary>
    public LocalizationService(IStringLocalizerFactory factory, IHttpContextAccessor httpContextAccessor, IServiceProvider serviceProvider, IMemoryCache cache, IOptions<CacheOptions> cacheOptions, ILogger<LocalizationService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _serviceProvider = serviceProvider;
        _cache = cache;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.DefaultCacheMinutes);
        _logger = logger;
        // 资源文件位于 Application 程序集
        _localizer = factory.Create("Messages", typeof(NoneadCSS.Application.DependencyInjection).Assembly.FullName!);
    }

    public string this[string key] => GetString(key);

    public string GetString(string key, params object[] args)
    {
        var culture = CurrentLanguage;
        var value = GetStringFromCache(key, culture);

        if (string.IsNullOrEmpty(value))
        {
            var str = _localizer[key];
            value = str.Value;
        }

        if (string.IsNullOrEmpty(value))
        {
            value = key;
        }

        return args.Length == 0 ? value : string.Format(value, args);
    }

    /// <summary>
    /// 从 IMemoryCache 获取翻译文本（仅同步读缓存，不阻塞线程）。
    /// 缓存由 <see cref="TranslationCacheWarmupService"/> 在后台异步预热与刷新。
    /// 缓存未命中时返回 null，由调用方回退到资源文件。
    /// </summary>
    private string? GetStringFromCache(string key, string culture)
    {
        var cacheKey = $"Translation:{culture}:{key}";
        return _cache.TryGetValue<string>(cacheKey, out var cached) ? cached : null;
    }

    /// <summary>
    /// 异步预热所有支持的文化的翻译缓存。由后台服务调用，避免在同步路径中执行 async DB 查询。
    /// </summary>
    public async Task PreloadCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (var culture in SupportedCultures)
            {
                var translations = await unitOfWork.Translations.GetByCultureAsync(culture);
                foreach (var t in translations)
                {
                    var cacheKey = $"Translation:{culture}:{t.Key}";
                    _cache.Set(cacheKey, t.Value ?? string.Empty, _cacheDuration);
                }
                // 标记该文化已预热，便于诊断
                _cache.Set($"Translation:Preloaded:{culture}", true, _cacheDuration);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "预热翻译缓存失败，将回退到资源文件。");
        }
    }

    public string CurrentLanguage
    {
        get
        {
            var culture = _httpContextAccessor.HttpContext?
                .Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()?
                .RequestCulture.UICulture.Name;

            return string.IsNullOrEmpty(culture) ? "zh-Hans" : culture;
        }
    }

    public IReadOnlyList<string> SupportedLanguages => SupportedCultures;
}

/// <summary>
/// 后台服务：应用启动时预热翻译缓存，并按缓存有效期周期性刷新，避免同步路径中的 sync-over-async。
/// </summary>
public class TranslationCacheWarmupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslationCacheWarmupService> _logger;
    private readonly TimeSpan _refreshInterval;

    public TranslationCacheWarmupService(IServiceProvider serviceProvider, IOptions<CacheOptions> cacheOptions, ILogger<TranslationCacheWarmupService> logger)
    {
        _serviceProvider = serviceProvider;
        _refreshInterval = TimeSpan.FromMinutes(cacheOptions.Value.LocalizationRefreshIntervalMinutes);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时立即预热
        await WarmupAsync(stoppingToken);

        // 周期性刷新（略短于缓存有效期，确保缓存不会过期）
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_refreshInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            await WarmupAsync(stoppingToken);
        }
    }

    private async Task WarmupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var localizationService = scope.ServiceProvider.GetRequiredService<ILocalizationService>();
            if (localizationService is LocalizationService concrete)
            {
                await concrete.PreloadCacheAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "翻译缓存后台刷新失败。");
        }
    }
}
