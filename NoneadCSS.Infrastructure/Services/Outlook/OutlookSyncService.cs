using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using System.Text.Json;

namespace NoneadCSS.Infrastructure.Services.Outlook;

/// <summary>
/// Outlook 日历双向同步服务实现
/// </summary>
public class OutlookSyncService : IOutlookSyncService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOutlookOAuthService _oauthService;
    private readonly IOutlookGraphService _graphService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutlookSyncService> _logger;
    private readonly IDistributedLockService _distributedLock;
    private readonly OutlookSyncOptions _options;
    // T000099: 用于运行时读取管理后台配置的 Azure AD 凭据（合并 DB + 基线），让 GetStatus 返回的 IsConfigured 即时反映管理员配置
    private readonly IOutlookSettingsProvider _settingsProvider;
    private readonly IOutlookSyncLogRepository _syncLogRepository;
    private readonly IOutlookSyncQueueRepository _syncQueueRepository;
    private readonly IMailboxRepository _mailboxRepository;
    private readonly IUserRoleResolver _userRoleResolver;

    // 同步窗口：默认同步前后 30 天的事件，可通过 Calendar:OutlookSyncWindowDays 配置覆盖
    private readonly int _syncWindowDays = 30;
    // 同步最小间隔（分钟），可通过 Calendar:OutlookSyncIntervalMinutes 配置覆盖
    private const int DefaultMinSyncIntervalMinutes = 15;

    // 锁键前缀（配合分布式锁）
    private const string SyncLockKeyPrefix = "outlook_sync:user:";

    public OutlookSyncService(
        IUnitOfWork unitOfWork,
        IOutlookOAuthService oauthService,
        IOutlookGraphService graphService,
        IConfiguration configuration,
        ILogger<OutlookSyncService> logger,
        IDistributedLockService distributedLock,
        IOptions<OutlookSyncOptions> options,
        IOutlookSettingsProvider settingsProvider,
        IOutlookSyncLogRepository syncLogRepository,
        IOutlookSyncQueueRepository syncQueueRepository,
        IMailboxRepository mailboxRepository,
        IUserRoleResolver userRoleResolver)
    {
        _unitOfWork = unitOfWork;
        _oauthService = oauthService;
        _graphService = graphService;
        _configuration = configuration;
        _logger = logger;
        _distributedLock = distributedLock;
        _options = options.Value;
        _settingsProvider = settingsProvider;
        _syncWindowDays = configuration.GetValue<int?>("Calendar:OutlookSyncWindowDays") ?? 30;
        _syncLogRepository = syncLogRepository;
        _syncQueueRepository = syncQueueRepository;
        _mailboxRepository = mailboxRepository;
        _userRoleResolver = userRoleResolver;
    }

    /// <inheritdoc />
    public async Task<OutlookSyncSettingDto?> GetSettingAsync(int userId)
    {
        var setting = await GetOrCreateSettingAsync(userId, createIfMissing: false);
        return setting == null ? null : MapToDto(setting);
    }

    /// <inheritdoc />
    public async Task<OutlookSyncStatusDto> GetStatusAsync(int userId)
    {
        var setting = await GetOrCreateSettingAsync(userId, createIfMissing: false);
        // 判断是否正在同步：检查分布式锁是否存在
        var isSyncing = false;
        var testLock = await _distributedLock.TryAcquireAsync(SyncLockKeyPrefix + userId, 0);
        if (testLock == null)
        {
            isSyncing = true;
        }
        else
        {
            await testLock.DisposeAsync();
        }

        // T000099: 通过 settings provider 读取用户级有效配置（用户级 > 全局基线），
        // 让前端 IsConfigured 即时反映用户在 UI 中保存的 Azure AD 凭据
        var effectiveOptions = await _settingsProvider.GetForUserAsync(userId);
        var isConfigured = effectiveOptions.IsConfigured;

        if (setting == null || !isConfigured)
        {
            return new OutlookSyncStatusDto
            {
                IsConfigured = isConfigured,
                IsConnected = false,
                SyncEnabled = false,
                IsSyncing = isSyncing
            };
        }

        return new OutlookSyncStatusDto
        {
            IsConfigured = isConfigured,
            IsConnected = setting.IsConnected,
            SyncEnabled = setting.SyncEnabled,
            SyncDirection = setting.SyncDirection,
            OutlookEmail = setting.OutlookEmail,
            LastSyncAt = setting.LastSyncAt,
            LastSyncStatus = setting.LastSyncStatus,
            LastSyncError = setting.LastSyncError,
            IsSyncing = isSyncing
        };
    }

    /// <inheritdoc />
    public async Task<OutlookSyncSettingDto?> UpdateSettingAsync(int userId, UpdateOutlookSyncSettingRequest request)
    {
        var setting = await GetOrCreateSettingAsync(userId, createIfMissing: true);
        var validDirections = new[] { "both", "to_outlook", "from_outlook" };
        var direction = validDirections.Contains(request.SyncDirection) ? request.SyncDirection : "both";

        // GetOrCreateSettingAsync(createIfMissing: true) 保证返回非 null
        setting!.SyncEnabled = request.SyncEnabled;
        setting.SyncDirection = direction;
        setting.UpdatedAt = DateTime.UtcNow;

        // 实体已通过 GetOrCreateSettingAsync 追踪，无需 UpdateAsync，SaveChangesAsync 自动持久化
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(setting);
    }

    /// <inheritdoc />
    public async Task<bool> DisconnectAsync(int userId)
    {
        var setting = await GetOrCreateSettingAsync(userId, createIfMissing: false);
        if (setting == null) return false;

        setting.AccessToken = null;
        setting.RefreshToken = null;
        setting.TokenExpiresAt = null;
        setting.OutlookEmail = null;
        setting.OutlookUserId = null;
        setting.SyncEnabled = false;
        setting.LastSyncStatus = "disconnected";
        setting.LastSyncError = null;
        setting.UpdatedAt = DateTime.UtcNow;

        // 实体已通过 GetOrCreateSettingAsync 追踪，无需 UpdateAsync
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("用户 {UserId} 已断开 Outlook 连接", userId);
        return true;
    }

    /// <inheritdoc />
    public async Task<OutlookSyncResultDto> SyncAsync(int userId, bool force = false, bool forceDeltaOnly = false)
    {
        var startedAt = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lockKey = SyncLockKeyPrefix + userId;

        // 1. 获取分布式锁（跨进程/多实例互斥），不等待
        var syncLock = await _distributedLock.TryAcquireAsync(lockKey, timeoutMs: 0);
        if (syncLock == null)
        {
            return new OutlookSyncResultDto
            {
                Success = false,
                ErrorMessage = "同步正在进行中，请稍后再试",
                DurationMs = 0,
                SyncedAt = startedAt
            };
        }

        try
        {
            var setting = await GetOrCreateSettingAsync(userId, createIfMissing: false);
            if (setting == null || !setting.IsConnected)
            {
                return FailureResult("未连接 Outlook 账户，请先完成 OAuth 授权", startedAt);
            }

            if (!setting.SyncEnabled && !force)
            {
                return FailureResult("同步已禁用", startedAt);
            }

            // 2. 节流：非强制同步时检查最小间隔
            var minInterval = _configuration.GetValue<int?>("Calendar:OutlookSyncIntervalMinutes")
                              ?? DefaultMinSyncIntervalMinutes;
            if (!force && setting.LastSyncAt.HasValue
                && (DateTime.UtcNow - setting.LastSyncAt.Value).TotalMinutes < minInterval)
            {
                return FailureResult($"距离上次同步不足 {minInterval} 分钟，请稍后再试", startedAt);
            }

            // 3. 获取有效 Access Token（必要时自动刷新并持久化）
            var accessToken = await _oauthService.EnsureValidAccessTokenAsync(setting);
            if (string.IsNullOrEmpty(accessToken))
            {
                await UpdateSyncStatusAsync(setting, "failed", "Access Token 无效或刷新失败");
                return FailureResult("Access Token 无效或刷新失败", startedAt);
            }

            // 若令牌被刷新，需要持久化
            // （EnsureValidAccessTokenAsync 已修改 setting 的令牌字段）
            await PersistSettingAsync(setting);

            // D#2/D#3: 解析关联的 MailboxId（审计日志 + 重试队列按 Mailbox 维度组织）
            // 通过用户关联的 MailboxUser 取第一个有效 mailbox；若缺失则日志仍可写 0 但建议运维补数据
            var mailboxId = await GetUserMailboxIdAsync(setting.UserId);

            // 4. 执行双向同步
            var direction = setting.SyncDirection;
            var result = new OutlookSyncResultDto { SyncedAt = startedAt };

            // D#2: 审计日志统计字段
            var syncStats = new SyncStats();
            var syncStarted = DateTimeOffset.UtcNow;
            bool pullDeltaReset = false;

            var now = DateTime.UtcNow;
            var startDate = now.AddDays(-_syncWindowDays);
            var endDate = now.AddDays(_syncWindowDays);

            string syncStatus = "success";
            string? syncError = null;

            try
            {
                if (direction is "both" or "to_outlook")
                {
                    var pushed = await PushLocalToOutlookAsync(accessToken, setting, startDate, endDate, mailboxId, syncStats);
                    result.PushedToOutlook = pushed.Created;
                    result.Created += pushed.Created;
                    result.Updated += pushed.Updated;
                    result.Deleted += pushed.Deleted;
                    result.Skipped += pushed.Skipped;
                }

                if (direction is "both" or "from_outlook")
                {
                    var pulled = await PullOutlookToLocalAsync(accessToken, setting, startDate, endDate, mailboxId, syncStats, forceDeltaOnly);
                    result.PulledFromOutlook = pulled.Created;
                    result.Created += pulled.Created;
                    result.Updated += pulled.Updated;
                    result.Skipped += pulled.Skipped;
                    result.Conflicts += pulled.Conflicts;
                    pullDeltaReset = pulled.DeltaTokenReset;
                }

                result.Success = true;
                syncStatus = syncStats.ErrorCount == 0 ? "success" : "partial";
                result.Status = syncStatus;
                result.DeltaTokenReset = pullDeltaReset;
                await UpdateSyncStatusAsync(setting, syncStatus, null);
                _logger.LogInformation(
                    "用户 {UserId} Outlook 同步成功：推送 {Pushed}，拉取 {Pulled}，更新 {Updated}，冲突 {Conflicts}，错误项 {Errors}, DeltaReset={DeltaReset}",
                    userId, result.PushedToOutlook, result.PulledFromOutlook, result.Updated, result.Conflicts, syncStats.ErrorCount, pullDeltaReset);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "用户 {UserId} Outlook 同步过程中发生异常", userId);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                syncStatus = "failed";
                result.Status = syncStatus;
                result.DeltaTokenReset = pullDeltaReset;
                syncError = ex.Message;
                syncStats.ErrorCount++;
                await UpdateSyncStatusAsync(setting, "failed", ex.Message);
            }
            finally
            {
                // D#2: 无论成功/失败，都写入审计日志
                if (mailboxId > 0)
                {
                    try
                    {
                        await _syncLogRepository.CreateAsync(new OutlookSyncLog
                        {
                            MailboxId = mailboxId,
                            Direction = direction switch
                            {
                                "both" => "both",
                                "to_outlook" => "outbound",
                                "from_outlook" => "inbound",
                                _ => direction ?? "both"
                            },
                            StartedAt = syncStarted,
                            FinishedAt = DateTimeOffset.UtcNow,
                            Status = syncStatus,
                            CreatedCount = syncStats.CreatedCount,
                            UpdatedCount = syncStats.UpdatedCount,
                            DeletedCount = syncStats.DeletedCount,
                            ErrorCount = syncStats.ErrorCount,
                            // P2 改进：冲突审计持久化
                            ConflictCount = syncStats.ConflictDetails.Count,
                            ConflictDetailsJson = syncStats.ConflictDetails.Count > 0
                                ? JsonSerializer.Serialize(syncStats.ConflictDetails)
                                : null,
                            ErrorMessage = syncError,
                            CreatedAt = DateTimeOffset.UtcNow
                        }, autoSave: true);
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning(logEx, "写入 Outlook 同步审计日志失败（不影响同步结果）");
                    }
                }
            }

            result.DurationMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
        finally
        {
            stopwatch.Stop();
            // 释放分布式锁（finally 确保即使抛异常也释放）
            await syncLock.DisposeAsync();
        }
    }

    /// <inheritdoc />
    public async Task<int?> HandleOAuthCallbackAsync(string code, string state)
    {
        // 从加密的 state 中解析 userId
        var (stateValid, userId) = await _oauthService.TryParseStateAsync(state);
        if (!stateValid)
        {
            _logger.LogWarning("Outlook OAuth 回调 state 解析失败或已过期");
            return null;
        }

        try
        {
            // T000099: 传递 userId 以使用用户级 Azure AD 凭据换码
            var token = await _oauthService.ExchangeCodeAsync(code, userId);

            // 获取 Outlook 用户信息
            var userInfo = await _graphService.GetUserInfoAsync(token.AccessToken);

            // 保存到同步设置（GetOrCreateSettingAsync 传入 createIfMissing: true 保证返回非 null）
            var setting = (await GetOrCreateSettingAsync(userId, createIfMissing: true))!;
            setting.AccessToken = token.AccessToken;
            setting.RefreshToken = token.RefreshToken ?? setting.RefreshToken;
            setting.TokenExpiresAt = token.ExpiresAt;
            setting.OutlookEmail = userInfo?.Email ?? setting.OutlookEmail;
            setting.OutlookUserId = userInfo?.ObjectId ?? setting.OutlookUserId;
            setting.SyncEnabled = true;
            setting.LastSyncStatus = "connected";
            setting.LastSyncError = null;
            setting.UpdatedAt = DateTime.UtcNow;

            await PersistSettingAsync(setting);

            _logger.LogInformation("用户 {UserId} Outlook OAuth 授权成功，邮箱={Email}", userId, setting.OutlookEmail);
            return userId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户 {UserId} Outlook OAuth 回调处理失败", userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<int> SyncAllEnabledUsersAsync()
    {
        var repo = _unitOfWork.Repository<OutlookSyncSetting>();
        // 查询所有启用同步且已连接的用户设置
        // 过滤 NextRetryAt 在未来的用户（指数退避冷却期内不重试）
        // NOTE: EF Core SQLite provider does NOT translate DateTimeOffset comparisons (see OutlookSyncQueueRepository).
        // NextRetryAt 为 DateTimeOffset?，无法在 SQL 层与 DateTime 比较，
        // 故先按可翻译条件物化到内存，再在客户端过滤指数退避冷却期内的用户，避免 InvalidOperationException。
        var utcNow = DateTime.UtcNow;
        var candidates = (await repo.FindAsync(s =>
            s.SyncEnabled
            && !string.IsNullOrEmpty(s.AccessToken)
            && !string.IsNullOrEmpty(s.RefreshToken)
            && s.DeletedAt == null)).ToList();
        var allSettingsList = candidates
            .Where(s => s.NextRetryAt == null || s.NextRetryAt <= utcNow)
            .ToList();

        // 注意：由于 DbContext 非线程安全且 SyncAsync 内部使用共享的 UnitOfWork，
        // 此处无法并行执行多个用户的同步。采用串行方式，但已物化列表避免多次枚举。
        var successCount = 0;
        foreach (var setting in allSettingsList)
        {
            try
            {
                var result = await SyncAsync(setting.UserId, force: false);
                if (result.Success) successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量同步用户 {UserId} 失败", setting.UserId);
            }
        }

        _logger.LogInformation("Outlook 批量同步完成：成功 {Success}/{Total}", successCount, allSettingsList.Count);
        return successCount;
    }

    // ============ 同步核心逻辑 ============

    /// <summary>
    /// 推送本地变更到 Outlook
    /// D#3: 单项推送失败时 Enqueue 到重试队列（不中断批次）
    /// </summary>
    private async Task<(int Created, int Updated, int Deleted, int Skipped)> PushLocalToOutlookAsync(
        string accessToken, OutlookSyncSetting setting, DateTime startDate, DateTime endDate,
        int mailboxId, SyncStats stats)
    {
        var created = 0;
        var updated = 0;
        var deleted = 0;
        var skipped = 0;

        var mappingRepo = _unitOfWork.Repository<OutlookEventMapping>();
        var calendarRepo = _unitOfWork.CalendarEvents;

        // 1. H-28: 分页获取时间窗口内未软删除的本地事件，避免 int.MaxValue 一次性拉取
        var localEvents = new List<CalendarEvent>();
        var pageSize = 200;
        var page = 1;
        List<CalendarEvent> batch;
        do
        {
            batch = await calendarRepo.GetListAsync(
                startDate, endDate,
                null, setting.UserId, null,
                null, null, null,
                page, pageSize);
            localEvents.AddRange(batch);
            page++;
        } while (batch.Count == pageSize);

        // 建立 CalendarEventId -> Mapping 的索引
        var localEventIds = localEvents.Select(e => e.Id).ToList();
        // H-29: 分批 Contains 查询，避免 IN 子句过长
        var mappings = new Dictionary<int, OutlookEventMapping>();
        var batchSize = 500;
        for (int i = 0; i < localEventIds.Count; i += batchSize)
        {
            var idBatch = localEventIds.Skip(i).Take(batchSize).ToList();
            var batchMappings = await mappingRepo.GetQueryable(false)
                .Where(m => m.UserId == setting.UserId && idBatch.Contains(m.CalendarEventId))
                .ToListAsync();
            foreach (var m in batchMappings)
            {
                mappings[m.CalendarEventId] = m;
            }
        }

        // 已知的 Outlook EventId 集合（用于后续检测 Outlook 端删除）
        var knownOutlookIds = new HashSet<string>();

        // H-32: 并发 HTTP 调用（Graph API），DB 写入保留在主线程串行
        const int maxHttpConcurrency = 5;
        using var httpSemaphore = new SemaphoreSlim(maxHttpConcurrency, maxHttpConcurrency);

        // 2. 并行创建/更新本地事件到 Outlook（仅 HTTP 部分并行）
        var pushTasks = localEvents.Select(async localEvent =>
        {
            await httpSemaphore.WaitAsync();
            try
            {
                if (mappings.TryGetValue(localEvent.Id, out var mapping))
                {
                    lock (knownOutlookIds) knownOutlookIds.Add(mapping.OutlookEventId);

                    if (mapping.LocalLastModified.HasValue
                        && localEvent.UpdatedAt <= mapping.LocalLastModified.Value)
                    {
                        return new PushResult(PushAction.Skip, mapping, mapping.OutlookEventId, false);
                    }

                    var ok = await _graphService.UpdateEventAsync(accessToken, mapping.OutlookEventId, localEvent);
                    if (ok)
                    {
                        mapping.LocalLastModified = localEvent.UpdatedAt;
                        mapping.OutlookLastModified = DateTime.UtcNow;
                        mapping.UpdatedAt = DateTime.UtcNow;
                        return new PushResult(PushAction.Update, mapping, mapping.OutlookEventId, false);
                    }
                    return new PushResult(PushAction.Skip, mapping, mapping.OutlookEventId, false);
                }
                else
                {
                    var outlookId = await _graphService.CreateEventAsync(accessToken, localEvent);
                    if (!string.IsNullOrEmpty(outlookId))
                    {
                        var newMapping = new OutlookEventMapping
                        {
                            CalendarEventId = localEvent.Id,
                            OutlookEventId = outlookId!,
                            UserId = setting.UserId,
                            Source = "local",
                            LocalLastModified = localEvent.UpdatedAt,
                            OutlookLastModified = DateTime.UtcNow
                        };
                        lock (knownOutlookIds) knownOutlookIds.Add(outlookId!);
                        return new PushResult(PushAction.Create, newMapping, outlookId, false);
                    }
                    return new PushResult(PushAction.Skip, null, null, false);
                }
            }
            catch (Exception ex)
            {
                // H-23: 检测 401 未授权，标记后由主线程统一刷新（避免并行中改捕获变量）
                if (IsUnauthorized(ex))
                {
                    _logger.LogWarning("推送事件 {EventId} 时检测到 token 过期，将在主线程刷新", localEvent.Id);
                    return new PushResult(PushAction.Skip, null, null, TokenExpired: true);
                }
                _logger.LogWarning(ex, "推送本地事件 {EventId} 到 Outlook 失败", localEvent.Id);
                return new PushResult(PushAction.Skip, null, null, false);
            }
            finally
            {
                httpSemaphore.Release();
            }
        }).ToList();

        var pushResults = await Task.WhenAll(pushTasks);

        // 统一处理 token 刷新（串行安全）
        if (pushResults.Any(r => r.TokenExpired))
        {
            var newToken = await _oauthService.EnsureValidAccessTokenAsync(setting);
            if (!string.IsNullOrEmpty(newToken))
            {
                accessToken = newToken;
                await PersistSettingAsync(setting);
                _logger.LogInformation("并行推送期间检测到 token 过期，已刷新并持久化");
            }
        }

        // 串行写入 DB（避免 DbContext 线程安全问题）
        // C-O4: 用事务包裹 DB 写入，失败时回滚并对已创建的 Outlook 事件执行补偿删除，
        // 防止 Outlook 端残留孤儿事件（HTTP 已成功但 DB mapping 未持久化）
        var createdMappings = pushResults
            .Where(r => r.Action == PushAction.Create && r.Mapping != null && !string.IsNullOrEmpty(r.KnownOutlookId))
            .Select(r => r.Mapping!)
            .ToList();

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            foreach (var r in pushResults)
            {
                switch (r.Action)
                {
                    case PushAction.Update when r.Mapping != null:
                        // mapping 已通过追踪查询加载，无需 UpdateAsync
                        updated++;
                        break;
                    case PushAction.Create when r.Mapping != null:
                        await mappingRepo.AddAsync(r.Mapping);
                        created++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            _logger.LogError(ex, "推送阶段 DB 写入失败，已回滚事务。开始补偿删除 {Count} 个已创建的 Outlook 事件",
                createdMappings.Count);

            // 补偿删除：对已成功创建但 DB mapping 未持久化的 Outlook 事件执行删除
            foreach (var orphanMapping in createdMappings)
            {
                try
                {
                    var deletedOk = await _graphService.DeleteEventAsync(accessToken, orphanMapping.OutlookEventId);
                    if (deletedOk)
                        _logger.LogInformation("补偿删除 Outlook 事件 {OutlookId} 成功", orphanMapping.OutlookEventId);
                    else
                        _logger.LogWarning("补偿删除 Outlook 事件 {OutlookId} 失败（Graph API 返回 false），需人工清理",
                            orphanMapping.OutlookEventId);
                }
                catch (Exception compensationEx)
                {
                    _logger.LogError(compensationEx,
                        "补偿删除 Outlook 事件 {OutlookId} 失败，需人工清理（本地事件 ID={EventId}）",
                        orphanMapping.OutlookEventId, orphanMapping.CalendarEventId);
                }
            }

            // 重新抛出原始异常，让上层 SyncAsync 的 catch 记录失败状态
            throw;
        }

        // D#2: 聚合推送统计到审计日志
        stats.CreatedCount += created;
        stats.UpdatedCount += updated;
        stats.DeletedCount += deleted;
        return (created, updated, deleted, skipped);
    }

    /// <summary>
    /// 拉取 Outlook 变更到本地
    /// D#1: 使用 DeltaToken 做增量同步；增量失败时重置 token 并回退全量，保证不丢数据
    /// D#3: 单个事件失败时 Enqueue 到重试队列，不中断批次
    /// P3-D: 返回 DeltaToken 是否被重置的标记
    /// </summary>
    private async Task<(int Created, int Updated, int Skipped, int Conflicts, bool DeltaTokenReset)> PullOutlookToLocalAsync(
        string accessToken, OutlookSyncSetting setting, DateTime startDate, DateTime endDate,
        int mailboxId, SyncStats stats, bool forceDeltaOnly = false)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var conflicts = 0;
        bool deltaTokenReset = false;

        // D#1: 优先尝试增量查询（若 mailbox 存在且有 token）；失败时回退全量
        Mailbox? mailbox = null;
        string? deltaToken = null;
        List<OutlookEventDto> outlookEvents;
        bool isDeltaExpired = false;

        if (mailboxId > 0)
        {
            mailbox = await _mailboxRepository.GetByIdAsync(mailboxId);
            deltaToken = mailbox?.OutlookDeltaToken;
        }

        try
        {
            if (!string.IsNullOrEmpty(deltaToken))
            {
                // D#1: 尝试增量拉取（使用 Microsoft Graph delta 查询）
                var deltaResult = await _graphService.ListEventsDeltaAsync(accessToken, startDate, endDate, deltaToken);
                outlookEvents = deltaResult.Events;
                isDeltaExpired = deltaResult.IsDeltaExpired;
                var newToken = deltaResult.NewDeltaToken;

                if (isDeltaExpired)
                {
                    deltaTokenReset = true;
                    _logger.LogWarning("Outlook DeltaToken 已过期，重置为全量拉取");
                    if (forceDeltaOnly)
                    {
                        throw new InvalidOperationException("DeltaToken 已失效，forceDeltaOnly 模式下回退全量同步被禁止，请使用普通同步。");
                    }
                    deltaToken = null;
                    outlookEvents = await _graphService.ListEventsAsync(accessToken, startDate, endDate);
                }

                // 保存新 deltaToken（无论增量/回退全量，成功时写下一轮用 token）
                if (mailbox != null && !string.IsNullOrEmpty(newToken))
                {
                    mailbox.OutlookDeltaToken = newToken;
                }
                else if (isDeltaExpired && mailbox != null)
                {
                    // 重置为 null，下次强制全量拉取
                    mailbox.OutlookDeltaToken = null;
                }
            }
            else
            {
                if (forceDeltaOnly)
                {
                    throw new InvalidOperationException("当前 Mailbox 尚未初始化 DeltaToken，forceDeltaOnly 模式下无法执行增量拉取，请先执行一次普通同步。");
                }
                // 无 token：全量拉取 + 尝试获取初始 delta token
                outlookEvents = await _graphService.ListEventsAsync(accessToken, startDate, endDate);

                // 尝试调用 delta 查询获取首 token（传 null deltaToken 通常返回首 token 及完整事件集）
                try
                {
                    var deltaInit = await _graphService.ListEventsDeltaAsync(accessToken, startDate, endDate, null);
                    if (!string.IsNullOrEmpty(deltaInit.NewDeltaToken) && mailbox != null)
                    {
                        mailbox.OutlookDeltaToken = deltaInit.NewDeltaToken;
                    }
                }
                catch (Exception dex)
                {
                    _logger.LogWarning(dex, "初始化 DeltaToken 失败，下次继续全量同步（不影响本次）");
                }
            }
        }
        catch (Exception ex)
        {
            if (forceDeltaOnly)
            {
                // forceDeltaOnly 模式下不回退全量，直接向上抛异常
                _logger.LogWarning(ex, "Outlook 增量拉取失败（forceDeltaOnly），不回退全量");
                throw;
            }
            // D#1: 增量同步失败 → 回退全量拉取
            _logger.LogWarning(ex, "Outlook 增量拉取失败，回退全量拉取保证不丢数据");
            deltaTokenReset = true;
            if (mailbox != null)
            {
                mailbox.OutlookDeltaToken = null;
            }
            deltaToken = null;
            outlookEvents = await _graphService.ListEventsAsync(accessToken, startDate, endDate);
        }

        var mappingRepo = _unitOfWork.Repository<OutlookEventMapping>();
        var calendarRepo = _unitOfWork.CalendarEvents;

        // 按 OutlookEventId 索引现有映射
        var outlookIds = outlookEvents.Select(e => e.Id).ToList();
        // H-29: 分批 Contains 查询，避免 IN 子句过长
        var existingMappings = new Dictionary<string, OutlookEventMapping>();
        var mappingBatchSize = 500;
        for (int i = 0; i < outlookIds.Count; i += mappingBatchSize)
        {
            var idBatch = outlookIds.Skip(i).Take(mappingBatchSize).ToList();
            var batchMappings = await mappingRepo.GetQueryable(false)
                .Where(m => m.UserId == setting.UserId && idBatch.Contains(m.OutlookEventId))
                .ToListAsync();
            foreach (var m in batchMappings)
            {
                existingMappings[m.OutlookEventId] = m;
            }
        }

        // M9 修复：批量预加载所有现有映射对应的本地 CalendarEvent（tracked + 含软删除），
        // 避免循环内逐条 GetByIdTrackedAsync / GetByIdIgnoreQueryFilterAsync 造成的 N+1 查询。
        // 之后按 DeletedAt 区分"未删除"与"已软删除"两类。
        var mappedEventIds = existingMappings.Values.Select(m => m.CalendarEventId).Distinct().ToList();
        var localEventsById = new Dictionary<int, CalendarEvent>();
        for (int i = 0; i < mappedEventIds.Count; i += mappingBatchSize)
        {
            var idBatch = mappedEventIds.Skip(i).Take(mappingBatchSize).ToList();
            var batchEvents = await calendarRepo.GetQueryable(false)
                .IgnoreQueryFilters()
                .Where(e => idBatch.Contains(e.Id))
                .ToListAsync();
            foreach (var evt in batchEvents)
            {
                localEventsById[evt.Id] = evt;
            }
        }

        foreach (var outlookEvent in outlookEvents)
        {
            try
            {
                if (existingMappings.TryGetValue(outlookEvent.Id, out var mapping))
                {
                    // 已存在映射：检查是否需要更新
                    if (mapping.OutlookLastModified.HasValue
                        && outlookEvent.LastModifiedDateTime <= mapping.OutlookLastModified.Value)
                    {
                        skipped++;
                        continue;
                    }

                    // 冲突检测：双方都修改过（本地较新）
                    if (mapping.LocalLastModified.HasValue
                        && mapping.OutlookLastModified.HasValue
                        && mapping.LocalLastModified.Value > mapping.OutlookLastModified.Value)
                    {
                        conflicts++;
                        // P4: 统一从 OutlookSyncOptions.ConflictStrategy 读取，避免分散读取 _configuration
                        // 策略：last_write_wins（默认，本地较新→本地胜出）/ local_wins（本地始终胜出）/ outlook_wins（Outlook 始终胜出）
                        var strategy = _options.IsValidConflictStrategy() ? _options.ConflictStrategy : "last_write_wins";
                        if (!_options.IsValidConflictStrategy())
                        {
                            _logger.LogWarning(
                                "Outlook 同步冲突策略配置非法: configured={Configured}, fallback=last_write_wins. " +
                                "合法值: last_write_wins / local_wins / outlook_wins",
                                _options.ConflictStrategy);
                        }
                        // P4: 结构化日志记录冲突解决决策
                        _logger.LogInformation(
                            "Outlook 同步冲突解决: event_id={EventId}, outlook_id={OutlookId}, strategy={Strategy}, " +
                            "winner={Winner}, local_modified={LocalModified}, outlook_modified={OutlookModified}",
                            mapping.CalendarEventId, outlookEvent.Id, strategy,
                            strategy == "outlook_wins" ? "outlook" : "local",
                            mapping.LocalLastModified, mapping.OutlookLastModified);
                        // P2 改进：收集冲突详情，用于审计日志持久化
                        stats.ConflictDetails.Add(new OutlookConflictDetail
                        {
                            EventId = mapping.CalendarEventId,
                            OutlookId = outlookEvent.Id,
                            Strategy = strategy,
                            Winner = strategy == "outlook_wins" ? "outlook" : "local",
                            LocalModified = mapping.LocalLastModified,
                            OutlookModified = mapping.OutlookLastModified
                        });
                        if (strategy != "outlook_wins")
                        {
                            // 本地较新（last_write_wins / local_wins），跳过 Outlook 更新（本地胜出）
                            skipped++;
                            continue;
                        }
                        // outlook_wins: 继续用 Outlook 版本覆盖本地
                    }

                    // 从批量预加载的字典中取本地事件（M9：避免循环内 N+1 查询）
                    localEventsById.TryGetValue(mapping.CalendarEventId, out var localEvent);

                    if (localEvent == null || localEvent.DeletedAt != null)
                    {
                        // H-27: 本地事件已被删除，按冲突策略处理
                        // P4: 统一使用 _options.ConflictStrategy（带非法值回退）
                        var restoreStrategy = _options.IsValidConflictStrategy() ? _options.ConflictStrategy : "last_write_wins";
                        if (restoreStrategy == "outlook_wins" || restoreStrategy == "last_write_wins")
                        {
                            // Outlook 有更新，恢复本地事件并应用更新
                            // deletedEvent 也来自批量预加载（IgnoreQueryFilters 已含软删除实体）
                            var deletedEvent = localEvent;
                            if (deletedEvent != null && deletedEvent.DeletedAt != null)
                            {
                                deletedEvent.Restore();
                                ApplyOutlookToLocal(deletedEvent, outlookEvent);
                                deletedEvent.UpdatedAt = DateTime.UtcNow;
                                // deletedEvent 通过追踪加载，无需 UpdateAsync

                                mapping.OutlookLastModified = outlookEvent.LastModifiedDateTime;
                                mapping.LocalLastModified = deletedEvent.UpdatedAt;
                                mapping.UpdatedAt = DateTime.UtcNow;
                                // mapping 通过追踪查询加载，无需 UpdateAsync
                                updated++;
                                _logger.LogInformation(
                                    "本地已删除事件 {EventId} 按 {Strategy} 策略恢复并应用 Outlook 更新",
                                    mapping.CalendarEventId, restoreStrategy);
                                continue;
                            }
                        }
                        // local_wins 或恢复失败：保持删除状态，跳过
                        skipped++;
                        continue;
                    }

                    ApplyOutlookToLocal(localEvent, outlookEvent);
                    localEvent.UpdatedAt = DateTime.UtcNow;
                    // localEvent 通过 GetByIdTrackedAsync 追踪加载，无需 UpdateAsync

                    mapping.OutlookLastModified = outlookEvent.LastModifiedDateTime;
                    mapping.LocalLastModified = localEvent.UpdatedAt;
                    mapping.UpdatedAt = DateTime.UtcNow;
                    // mapping 通过追踪查询加载，无需 UpdateAsync
                    updated++;
                }
                else
                {
                    // Outlook 新事件：创建本地事件
                    var newEvent = new CalendarEvent
                    {
                        Title = outlookEvent.Subject ?? "(无标题)",
                        Description = outlookEvent.BodyContent,
                        EventType = "task",
                        StartAt = outlookEvent.Start,
                        EndAt = outlookEvent.End,
                        IsAllDay = outlookEvent.IsAllDay,
                        UserId = setting.UserId,
                        CreatedBy = setting.UserId,
                        Status = "pending",
                        Location = outlookEvent.Location,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await calendarRepo.AddAsync(newEvent);
                    // H-33: 不在循环内 SaveChanges，统一在循环结束后保存。
                    // EF Core 会通过临时键 fixup 在最终 SaveChangesAsync 时自动修正 CalendarEventId。

                    var newMapping = new OutlookEventMapping
                    {
                        CalendarEventId = newEvent.Id,
                        OutlookEventId = outlookEvent.Id,
                        UserId = setting.UserId,
                        Source = "outlook",
                        LocalLastModified = newEvent.UpdatedAt,
                        OutlookLastModified = outlookEvent.LastModifiedDateTime
                    };
                    await mappingRepo.AddAsync(newMapping);
                    created++;
                }
            }
            catch (Exception ex)
            {
                // H-23: 检测 401 未授权，尝试刷新 token
                if (IsUnauthorized(ex))
                {
                    var newToken = await _oauthService.EnsureValidAccessTokenAsync(setting);
                    if (newToken != null)
                    {
                        accessToken = newToken;
                        _logger.LogWarning("拉取事件 {OutlookId} 时 token 过期已刷新，当前事件将在下次同步重试", outlookEvent.Id);
                        skipped++;
                        continue;
                    }
                }
                _logger.LogWarning(ex, "拉取 Outlook 事件 {OutlookId} 到本地失败", outlookEvent.Id);
                stats.ErrorCount++;

                // D#3: 单项失败 → Enqueue 到重试队列（不中断批次）
                if (mailboxId > 0)
                {
                    try
                    {
                        var payload = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            outlookEventId = outlookEvent.Id,
                            outlookEvent.Subject,
                            lastError = ex.Message,
                            retrySource = "pull"
                        });
                        await _syncQueueRepository.EnqueueAsync(new OutlookSyncQueueItem
                        {
                            MailboxId = mailboxId,
                            EventId = null,
                            Direction = "inbound",
                            Operation = "update",
                            PayloadJson = payload,
                            RetryCount = 0,
                            MaxRetry = 5,
                            NextRetryAt = null,
                            Status = "pending",
                            LastError = ex.Message,
                            CreatedAt = DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.UtcNow
                        }, autoSave: true);
                    }
                    catch (Exception qex)
                    {
                        _logger.LogWarning(qex, "写入 Outlook 重试队列失败（Inbound, OutlookId={Id}）", outlookEvent.Id);
                    }
                }
                skipped++;
            }
        }

        await _unitOfWork.SaveChangesAsync();

        // D#2: 聚合统计到审计日志
        stats.CreatedCount += created;
        stats.UpdatedCount += updated;
        stats.DeletedCount += 0;
        return (created, updated, skipped, conflicts, deltaTokenReset);
    }

    /// <summary>
    /// 将 Outlook 事件字段应用到本地事件
    /// </summary>
    private static void ApplyOutlookToLocal(CalendarEvent local, OutlookEventDto outlook)
    {
        if (!string.IsNullOrWhiteSpace(outlook.Subject))
            local.Title = outlook.Subject;
        if (!string.IsNullOrWhiteSpace(outlook.BodyContent))
            local.Description = outlook.BodyContent;
        local.StartAt = outlook.Start;
        local.EndAt = outlook.End;
        local.IsAllDay = outlook.IsAllDay;
        if (!string.IsNullOrWhiteSpace(outlook.Location))
            local.Location = outlook.Location;
        if (!string.IsNullOrWhiteSpace(outlook.OnlineMeetingUrl))
            local.MeetingUrl = outlook.OnlineMeetingUrl;
    }

    // ============ 辅助方法 ============

    /// <summary>
    /// H-23: 检测异常是否为 401 未授权（token 过期）。
    /// HttpRequestException.StatusCode 在 .NET 5+ 可用；同时回退到 Message 检查。
    /// </summary>
    private static bool IsUnauthorized(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return true;
            var msg = httpEx.Message ?? string.Empty;
            if (msg.Contains("401") || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private async Task<OutlookSyncSetting?> GetOrCreateSettingAsync(int userId, bool createIfMissing)
    {
        var repo = _unitOfWork.Repository<OutlookSyncSetting>();
        // 使用追踪查询（非 AsNoTracking），确保调用方修改属性后 SaveChangesAsync 能自动持久化，
        // 无需调用 UpdateAsync（避免全字段 UPDATE 反模式）。
        var settings = await repo.GetQueryable(false)
            .Where(s => s.UserId == userId && s.DeletedAt == null)
            .ToListAsync();
        var setting = settings.FirstOrDefault();

        if (setting != null || !createIfMissing) return setting;

        setting = new OutlookSyncSetting
        {
            UserId = userId,
            SyncEnabled = true,
            SyncDirection = "both",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await repo.AddAsync(setting);
        await _unitOfWork.SaveChangesAsync();
        return setting;
    }

    private async Task PersistSettingAsync(OutlookSyncSetting setting)
    {
        // setting 已通过 GetOrCreateSettingAsync 追踪，无需 UpdateAsync
        await _unitOfWork.SaveChangesAsync();
    }

    private async Task UpdateSyncStatusAsync(OutlookSyncSetting setting, string status, string? error)
    {
        setting.LastSyncAt = DateTime.UtcNow;
        setting.LastSyncStatus = status;
        setting.LastSyncError = error;
        setting.UpdatedAt = DateTime.UtcNow;
        await PersistSettingAsync(setting);
    }

    private static OutlookSyncResultDto FailureResult(string message, DateTime startedAt)
    {
        return new OutlookSyncResultDto
        {
            Success = false,
            ErrorMessage = message,
            SyncedAt = startedAt
        };
    }

    private enum PushAction { Skip, Update, Create }

    private sealed record PushResult(PushAction Action, OutlookEventMapping? Mapping, string? KnownOutlookId, bool TokenExpired);

    private static OutlookSyncSettingDto MapToDto(OutlookSyncSetting s)
    {
        return new OutlookSyncSettingDto
        {
            Id = s.Id,
            UserId = s.UserId,
            OutlookEmail = s.OutlookEmail,
            OutlookUserId = s.OutlookUserId,
            IsConnected = s.IsConnected,
            IsTokenExpired = s.IsTokenExpired,
            SyncEnabled = s.SyncEnabled,
            SyncDirection = s.SyncDirection,
            LastSyncAt = s.LastSyncAt,
            LastSyncStatus = s.LastSyncStatus,
            LastSyncError = s.LastSyncError,
            TokenExpiresAt = s.TokenExpiresAt,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }

    /// <summary>
    /// D#2: 审计日志聚合统计值（Created/Updated/Deleted/Error 计数）
    /// </summary>
    public class SyncStats
    {
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int DeletedCount { get; set; }
        public int ErrorCount { get; set; }

        /// <summary>P2 改进：冲突详情收集（用于写入 OutlookSyncLog.ConflictDetailsJson）</summary>
        public List<OutlookConflictDetail> ConflictDetails { get; } = new();
    }

    /// <summary>
    /// P2 改进：Outlook 同步冲突详情记录，用于审计日志持久化。
    /// </summary>
    public class OutlookConflictDetail
    {
        public int EventId { get; set; }
        public string? OutlookId { get; set; }
        public string? Strategy { get; set; }
        public string? Winner { get; set; }
        public DateTimeOffset? LocalModified { get; set; }
        public DateTimeOffset? OutlookModified { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// D#2/D#3: 查找用户关联的第一个 MailboxId。
    /// 通过 MailboxUser 多对多关联查询；若未找到返回 0（由上层决定是否跳过日志/重试写入）。
    /// </summary>
    private async Task<int> GetUserMailboxIdAsync(int userId)
    {
        try
        {
            var repo = _unitOfWork.Repository<MailboxUser>();
            var mu = await repo.GetQueryable(false)
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderBy(x => x.MailboxId)
                .FirstOrDefaultAsync();
            return mu?.MailboxId ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询用户 {UserId} 的 MailboxId 失败", userId);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<OutlookSyncResultDto> SyncByMailboxIdAsync(int mailboxId, int currentUserId, bool forceDeltaOnly = false)
    {
        var startedAt = DateTime.UtcNow;

        // 1. 校验 Mailbox 是否存在
        var mailbox = await _mailboxRepository.GetByIdAsync(mailboxId);
        if (mailbox == null)
        {
            return new OutlookSyncResultDto
            {
                Success = false,
                ErrorMessage = "Mailbox 不存在",
                Status = "failed",
                SyncedAt = startedAt
            };
        }

        // 2. 校验当前用户是否有该邮箱的管理权
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(currentUserId);
        if (!isSuperAdmin)
        {
            var muRepo = _unitOfWork.Repository<MailboxUser>();
            var hasAccess = await muRepo.GetQueryable(false)
                .AsNoTracking()
                .AnyAsync(x => x.MailboxId == mailboxId && x.UserId == currentUserId);
            if (!hasAccess)
            {
                return new OutlookSyncResultDto
                {
                    Success = false,
                    ErrorMessage = "无权限操作该邮箱",
                    Status = "failed",
                    SyncedAt = startedAt
                };
            }
        }

        // 3. 通过 Mailbox 反查关联的 OutlookSyncSetting（找到该 Mailbox 下拥有同步设置的用户）
        var settingRepo = _unitOfWork.Repository<OutlookSyncSetting>();
        var muList = await _unitOfWork.Repository<MailboxUser>().GetQueryable(false)
            .AsNoTracking()
            .Where(x => x.MailboxId == mailboxId)
            .Select(x => x.UserId)
            .ToListAsync();

        OutlookSyncSetting? matchedSetting = null;
        int matchedUserId = 0;

        if (muList.Count > 0)
        {
            // M9 修复：一次性批量查询该邮箱所有用户的同步设置（含连接状态），
            // 避免循环内逐条 FirstOrDefaultAsync 的 N+1 查询。
            // 按 muList 顺序取第一个已连接用户的设置（与原逻辑语义一致）。
            var settings = await settingRepo.GetQueryable(false)
                .Where(x => muList.Contains(x.UserId) && x.DeletedAt == null && x.IsConnected)
                .ToListAsync();

            var settingsByUser = settings
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.LastSyncAt).First());

            foreach (var uid in muList)
            {
                if (settingsByUser.TryGetValue(uid, out var s))
                {
                    matchedSetting = s;
                    matchedUserId = uid;
                    break;
                }
            }
        }

        if (matchedSetting == null || matchedUserId == 0)
        {
            return new OutlookSyncResultDto
            {
                Success = false,
                ErrorMessage = "该邮箱尚未配置 Outlook 同步设置或未完成 OAuth 授权",
                Status = "failed",
                SyncedAt = startedAt
            };
        }

        _logger.LogInformation("用户 {CurrentUserId} 通过 MailboxId={MailboxId} 触发手动 Delta 拉取（实际关联用户={MatchedUserId}，forceDeltaOnly={ForceDeltaOnly}）",
            currentUserId, mailboxId, matchedUserId, forceDeltaOnly);

        // 4. 复用现有 SyncAsync 逻辑（force=true 强制跳过最小间隔）
        return await SyncAsync(matchedUserId, force: true, forceDeltaOnly: forceDeltaOnly);
    }
}
