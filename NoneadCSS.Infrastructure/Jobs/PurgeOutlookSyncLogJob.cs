#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;
using Quartz;

namespace NoneadCSS.Infrastructure.Jobs;

/// <summary>
/// 清理过期的 Outlook 同步审计日志（P2P3 #6 审计合规要求）。
/// 继承 RetryableJobBase 获得重试能力。通过 Options 配置保留天数，默认 90 天。
/// 使用物理删除（而非软删除），因为 PURGE 是合规性要求的永久清除操作。
/// </summary>
[DisallowConcurrentExecution]
public class PurgeOutlookSyncLogJob : RetryableJobBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly int _retentionDays;

    public PurgeOutlookSyncLogJob(
        IServiceProvider serviceProvider,
        ILogger<PurgeOutlookSyncLogJob> logger,
        IOptions<PurgeOutlookSyncLogOptions> opts)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
        _retentionDays = Math.Clamp(opts.Value.RetentionDays, 1, 3650);
    }

    /// <summary>
    /// 实际清理逻辑（在基类 RetryableJobBase 的重试保护内执行）
    /// 使用 ExecuteDeleteAsync 直接执行 DELETE SQL，绕过 ChangeTracker 的软删除拦截器
    /// （类似 CalendarEventRepository.PurgeTrashAsync 的模式）
    /// </summary>
    protected override async Task ExecuteInternal(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NoneadCSSDbContext>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);
        _logger.LogInformation(
            "PurgeOutlookSyncLog: 开始清理 {Cutoff:yyyy-MM-dd HH:mm:ss} 之前的审计日志（保留天数={Days}）",
            cutoff, _retentionDays);

        // 使用原生 SQL DELETE：SQLite 提供程序不支持 DateTimeOffset 的大小比较（仅支持相等），
        // 若用 .Where(l => l.CreatedAt < cutoff).ExecuteDeleteAsync() 会抛出
        // "could not be translated" 异常。改用 ExecuteSqlInterpolatedAsync + FormattableString 插值，
        // EF Core 会将其转为参数化查询（自动防 SQL 注入），且原生 SQL 天然绕过全局查询过滤器。
        var deleted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"OutlookSyncLogs\" WHERE \"CreatedAt\" < {cutoff}").ConfigureAwait(false);

        _logger.LogInformation(
            "PurgeOutlookSyncLog: 完成清理，成功物理删除 {Deleted} 条",
            deleted);
    }
}
