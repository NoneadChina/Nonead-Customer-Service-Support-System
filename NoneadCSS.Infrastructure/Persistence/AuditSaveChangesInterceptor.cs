using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Services;
using System.Data.Common;

namespace NoneadCSS.Infrastructure.Persistence;

/// <summary>
/// EF Core SaveChanges 拦截器，自动记录实体变更审计日志
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;
    private readonly AuditHmacSigner _hmacSigner;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "PasswordHash", "RefreshToken", "AccessToken",
        "OutPassword", "InPassword", "OauthAccessToken", "OauthRefreshToken",
        "Secret", "ApiKey", "ApiSecret"
    };

    private readonly AsyncLocal<List<PendingAuditEntry>?> _pendingAddedEntries = new();

    /// <summary>
    /// 已设置过 WAL 模式的连接字符串集合。
    /// SQLite 的 WAL 模式是数据库文件级别的持久化设置，无需在每次 SaveChanges 时重复设置。
    /// 使用 ConcurrentDictionary 替代 lock+HashSet，避免高并发下的锁竞争。
    /// 缓存键说明（L-Q3）：以连接字符串原文作为键。同一进程内连接字符串通常稳定且数量有限，
    /// 不会无限增长；如未来支持多租户动态连接串，可考虑加 LRU 淘汰策略限制字典容量。
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> _walConfiguredConnections = new(StringComparer.Ordinal);

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="httpContextAccessor">HTTP上下文访问器</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="hmacSigner">审计日志 HMAC 签名器，用于防篡改保护</param>
    public AuditSaveChangesInterceptor(
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditSaveChangesInterceptor> logger,
        AuditHmacSigner hmacSigner)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _hmacSigner = hmacSigner;
    }

    /// <summary>
    /// 在 SaveChanges 前记录审计日志（新增实体暂不填ID）
    /// 审计日志创建失败不阻断主事务
    /// </summary>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnsureWalMode(eventData.Context);
        try
        {
            EnsureRowVersions(eventData.Context);
            CreateAuditEntries(eventData.Context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "审计日志创建失败，不影响主事务");
            _pendingAddedEntries.Value = null;
        }
        return base.SavingChanges(eventData, result);
    }

    /// <summary>
    /// 在 SaveChangesAsync 前记录审计日志（新增实体暂不填ID）
    /// 使用 async/await 避免 .Wait() 阻塞线程池线程
    /// 审计日志创建失败不阻断主事务
    /// </summary>
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await EnsureWalModeAsync(eventData.Context, cancellationToken);
        try
        {
            EnsureRowVersions(eventData.Context);
            await CreateAuditEntriesAsync(eventData.Context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "审计日志创建失败，不影响主事务");
            _pendingAddedEntries.Value = null;
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void EnsureWalMode(DbContext? context)
    {
        if (context == null) return;
        try
        {
            var conn = context.Database.GetDbConnection();
            var connectionString = conn.ConnectionString ?? string.Empty;
            // WAL 模式是数据库级别的持久化设置，已设置过则跳过，避免每次 SaveChanges 都执行 PRAGMA
            if (_walConfiguredConnections.ContainsKey(connectionString))
            {
                return;
            }

            if (conn.State != System.Data.ConnectionState.Open)
            {
                conn.Open();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
            using var fkCmd = conn.CreateCommand();
            fkCmd.CommandText = "PRAGMA foreign_keys=ON;";
            fkCmd.ExecuteNonQuery();

            _walConfiguredConnections.TryAdd(connectionString, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set WAL mode");
        }
    }

    private async Task EnsureWalModeAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context == null) return;
        try
        {
            var conn = context.Database.GetDbConnection();
            var connectionString = conn.ConnectionString ?? string.Empty;
            // WAL 模式是数据库级别的持久化设置，已设置过则跳过，避免每次 SaveChanges 都执行 PRAGMA
            if (_walConfiguredConnections.ContainsKey(connectionString))
            {
                return;
            }

            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            using var fkCmd = conn.CreateCommand();
            fkCmd.CommandText = "PRAGMA foreign_keys=ON;";
            await fkCmd.ExecuteNonQueryAsync(cancellationToken);

            _walConfiguredConnections.TryAdd(connectionString, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set WAL mode");
        }
    }

    /// <summary>
    /// 在 SaveChanges 完成后回填新增实体的ID并分离审计日志
    /// 审计日志回填失败不阻断主事务
    /// </summary>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        try
        {
            UpdateAddedEntityIds(eventData.Context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "审计日志回填新增实体ID失败，不影响主事务");
            _pendingAddedEntries.Value = null;
        }
        DetachAuditLogs(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    /// <summary>
    /// 在 SaveChangesAsync 完成后回填新增实体的ID并分离审计日志
    /// 使用异步 SQL 调用避免阻塞异步管道
    /// 审计日志回填失败不阻断主事务
    /// </summary>
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        try
        {
            await UpdateAddedEntityIdsAsync(eventData.Context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "审计日志回填新增实体ID失败，不影响主事务");
            _pendingAddedEntries.Value = null;
        }
        DetachAuditLogs(eventData.Context);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// SaveChanges 失败时清理 AsyncLocal 中暂存的新增实体引用，
    /// 防止异常路径下 pending 列表残留导致后续请求误回填已回滚的实体ID。
    /// </summary>
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _pendingAddedEntries.Value = null;
        DetachAuditLogs(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    /// <summary>
    /// SaveChangesAsync 失败时清理 AsyncLocal 中暂存的新增实体引用，
    /// 防止异常路径下 pending 列表残留导致后续请求误回填已回滚的实体ID。
    /// </summary>
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pendingAddedEntries.Value = null;
        DetachAuditLogs(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <summary>
    /// 将审计日志从跟踪中分离，避免后续审计操作导致跟踪冲突
    /// </summary>
    private void DetachAuditLogs(DbContext? context)
    {
        if (context == null) return;
        var auditLogs = context.ChangeTracker.Entries<AuditLog>().ToList();
        foreach (var entry in auditLogs)
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// P4 修复：确保所有 Added 状态实体的 byte[] 并发令牌（RowVersion/Version）非空。
    /// 背景：DbContext 配置改为 ValueGeneratedNever() + IsRequired(false) 后，EF Core 不再自动生成值，
    /// 若调用方未显式赋值（如 CreateAsync 遗漏），INSERT 会传 NULL。
    /// 虽然列允许 NULL，但 NULL 的 RowVersion 会导致乐观锁 WHERE 条件永远不匹配（NULL != NULL），
    /// 后续更新无法正确检测并发冲突。
    /// 此方法作为无条件兜底：遍历所有 Added 实体，对 null 或空数组的 byte[] 并发令牌赋随机值。
    /// 与 CreateAsync 显式赋值、DB 级 DEFAULT randomblob(16) 三重保障，确保 RowVersion 永远非空。
    ///
    /// 同时处理 Modified 实体：SQLite + ValueGeneratedNever 配置下 EF Core 不会自动 bump RowVersion，
    /// 导致乐观锁失效（WHERE RowVersion = @original 永远匹配，无法检测并发修改）
    /// 且 SignalR 推送的 RowVersion 不变，前端会因版本相同而丢弃更新。
    /// 此处对 Modified 实体的并发令牌无条件 bump 新版本号，确保每次更新后版本号递增。
    /// </summary>
    private void EnsureRowVersions(DbContext? context)
    {
        if (context == null) return;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                foreach (var property in entry.Properties)
                {
                    // 仅处理 byte[] 类型的并发令牌属性
                    if (property.Metadata.ClrType != typeof(byte[])) continue;
                    if (!property.Metadata.IsConcurrencyToken) continue;

                    var current = property.CurrentValue as byte[];
                    if (current == null || current.Length == 0)
                    {
                        property.CurrentValue = Guid.NewGuid().ToByteArray();
                    }
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                foreach (var property in entry.Properties)
                {
                    if (property.Metadata.ClrType != typeof(byte[])) continue;
                    if (!property.Metadata.IsConcurrencyToken) continue;

                    // 无条件 bump：每次更新都生成新版本号。
                    // EF Core 在 SaveChanges 时会用 OriginalValue（旧版本）做 WHERE 乐观锁，
                    // 成功后数据库行被更新为 CurrentValue（新版本），下次查询返回新版本。
                    property.CurrentValue = Guid.NewGuid().ToByteArray();
                    property.IsModified = true;
                }
            }
        }
    }

    /// <summary>
    /// 创建审计条目
    /// </summary>
    /// <param name="context">数据库上下文</param>
    private void CreateAuditEntries(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString() ?? string.Empty;

        // 转换为 int? 类型的用户ID
        int? userIdInt = null;
        if (int.TryParse(userId, out var parsedId))
        {
            userIdInt = parsedId;
        }

        _pendingAddedEntries.Value = new List<PendingAuditEntry>();
        var auditEntries = new List<AuditLog>();

        // 遍历所有被修改的实体
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
            {
                continue;
            }

            var entityType = entry.Entity.GetType().Name;
            var entityId = GetEntityId(entry);
            var action = entry.State switch
            {
                EntityState.Added => "create",
                EntityState.Modified => "update",
                EntityState.Deleted => "delete",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(action))
            {
                continue;
            }

            string oldValues = string.Empty;
            string newValues = string.Empty;

            if (entry.State == EntityState.Added)
            {
                newValues = GetPropertyValues(entry, entry.CurrentValues);
            }
            else if (entry.State == EntityState.Deleted)
            {
                oldValues = GetPropertyValues(entry, entry.OriginalValues);
            }
            else if (entry.State == EntityState.Modified)
            {
                oldValues = GetChangedPropertyValues(entry, useOriginal: true);
                newValues = GetChangedPropertyValues(entry, useOriginal: false);

                // 如果没有实际变化的属性，跳过
                if (string.IsNullOrEmpty(oldValues) && string.IsNullOrEmpty(newValues))
                {
                    continue;
                }
            }

            var auditLog = new AuditLog
            {
                UserId = userIdInt,
                Action = action,
                EntityType = entityType,
                EntityId = entry.State == EntityState.Added ? null : entityId,
                OldValues = oldValues,
                NewValues = newValues,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                CreatedAt = DateTime.UtcNow
            };

            // 计算 HMAC 签名以防止审计记录被事后篡改
            auditLog.HmacSignature = _hmacSigner.ComputeSignature(auditLog);
            auditEntries.Add(auditLog);

            // 新增实体需要保存引用，待SaveChanges后回填ID
            if (entry.State == EntityState.Added)
            {
                _pendingAddedEntries.Value!.Add(new PendingAuditEntry
                {
                    EntityEntry = entry,
                    AuditLog = auditLog
                });
            }
        }

        // 将审计日志添加到上下文中
        if (auditEntries.Count > 0)
        {
            context.Set<AuditLog>().AddRange(auditEntries);
        }
    }

    /// <summary>
    /// 异步创建审计条目（顺序处理，确保 EF Core 线程安全）
    /// </summary>
    private async Task CreateAuditEntriesAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context == null)
        {
            return;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString() ?? string.Empty;

        int? userIdInt = null;
        if (int.TryParse(userId, out var parsedId))
        {
            userIdInt = parsedId;
        }

        _pendingAddedEntries.Value = new List<PendingAuditEntry>();
        var auditEntries = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
            {
                continue;
            }

            var entityType = entry.Entity.GetType().Name;
            var entityId = GetEntityId(entry);
            var action = entry.State switch
            {
                EntityState.Added => "create",
                EntityState.Modified => "update",
                EntityState.Deleted => "delete",
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(action))
            {
                continue;
            }

            string oldValues = string.Empty;
            string newValues = string.Empty;

            if (entry.State == EntityState.Added)
            {
                newValues = GetPropertyValues(entry, entry.CurrentValues);
            }
            else if (entry.State == EntityState.Deleted)
            {
                oldValues = GetPropertyValues(entry, entry.OriginalValues);
            }
            else if (entry.State == EntityState.Modified)
            {
                oldValues = GetChangedPropertyValues(entry, useOriginal: true);
                newValues = GetChangedPropertyValues(entry, useOriginal: false);

                if (string.IsNullOrEmpty(oldValues) && string.IsNullOrEmpty(newValues))
                {
                    continue;
                }
            }

            var auditLog = new AuditLog
            {
                UserId = userIdInt,
                Action = action,
                EntityType = entityType,
                EntityId = entry.State == EntityState.Added ? null : entityId,
                OldValues = oldValues,
                NewValues = newValues,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                CreatedAt = DateTime.UtcNow
            };

            // 计算 HMAC 签名以防止审计记录被事后篡改
            auditLog.HmacSignature = _hmacSigner.ComputeSignature(auditLog);
            auditEntries.Add(auditLog);

            if (entry.State == EntityState.Added)
            {
                _pendingAddedEntries.Value!.Add(new PendingAuditEntry
                {
                    EntityEntry = entry,
                    AuditLog = auditLog
                });
            }
        }

        if (auditEntries.Count > 0)
        {
            context.Set<AuditLog>().AddRange(auditEntries);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 回填新增实体的ID到审计日志
    /// </summary>
    /// <param name="context">数据库上下文</param>
    private void UpdateAddedEntityIds(DbContext? context)
    {
        var pendingList = _pendingAddedEntries.Value;
        if (context == null || pendingList == null || pendingList.Count == 0)
        {
            _pendingAddedEntries.Value = null;
            return;
        }

        foreach (var pending in pendingList)
        {
            var entityId = GetEntityId(pending.EntityEntry);
            if (entityId.HasValue && pending.AuditLog.Id > 0)
            {
                // 使用直接SQL更新，避免触发拦截器导致无限循环
                context.Database.ExecuteSqlRaw(
                    "UPDATE \"AuditLogs\" SET \"EntityId\" = {0} WHERE \"Id\" = {1}",
                    entityId.Value,
                    pending.AuditLog.Id);
            }
        }

        _pendingAddedEntries.Value = null;
    }

    /// <summary>
    /// 异步回填新增实体的ID到审计日志，避免阻塞异步管道
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task UpdateAddedEntityIdsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        var pendingList = _pendingAddedEntries.Value;
        if (context == null || pendingList == null || pendingList.Count == 0)
        {
            _pendingAddedEntries.Value = null;
            return;
        }

        foreach (var pending in pendingList)
        {
            var entityId = GetEntityId(pending.EntityEntry);
            if (entityId.HasValue && pending.AuditLog.Id > 0)
            {
                // 使用异步SQL更新，避免触发拦截器导致无限循环
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE \"AuditLogs\" SET \"EntityId\" = {0} WHERE \"Id\" = {1}",
                    parameters: new object[] { entityId.Value, pending.AuditLog.Id },
                    cancellationToken: cancellationToken);
            }
        }

        _pendingAddedEntries.Value = null;
    }

    /// <summary>
    /// 待回填ID的审计条目
    /// </summary>
    private class PendingAuditEntry
    {
        public EntityEntry EntityEntry { get; set; } = null!;
        public AuditLog AuditLog { get; set; } = null!;
    }

    /// <summary>
    /// 获取实体ID
    /// </summary>
    private int? GetEntityId(EntityEntry entry)
    {
        try
        {
            var idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
            if (idProperty != null && idProperty.CurrentValue is int id)
            {
                return id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取实体ID失败: EntityType={EntityType}", entry.Entity.GetType().Name);
        }
        return null;
    }

    /// <summary>
    /// 获取所有属性值的JSON表示
    /// </summary>
    private static string GetPropertyValues(EntityEntry entry, PropertyValues values)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            // 跳过导航属性和集合属性
            if (property.Metadata.IsPrimaryKey() || property.Metadata.IsForeignKey())
            {
                continue;
            }
            var propName = property.Metadata.Name;
            var propValue = values[property.Metadata];

            // 跳过密码等敏感字段
            if (IsSensitiveProperty(propName))
            {
                continue;
            }

            dict[propName] = propValue;
        }
        return JsonSerializer.Serialize(dict, JsonOptions);
    }

    /// <summary>
    /// 获取变更属性值的JSON表示
    /// </summary>
    private static string GetChangedPropertyValues(EntityEntry entry, bool useOriginal)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (!property.IsModified)
            {
                continue;
            }

            var propName = property.Metadata.Name;

            // 跳过密码等敏感字段
            if (IsSensitiveProperty(propName))
            {
                continue;
            }

            var propValue = useOriginal ? property.OriginalValue : property.CurrentValue;
            dict[propName] = propValue;
        }
        return dict.Count > 0 ? JsonSerializer.Serialize(dict, JsonOptions) : string.Empty;
    }

    /// <summary>
    /// 判断属性是否为敏感字段（不应记录在审计日志中）
    /// </summary>
    /// <param name="propertyName">属性名</param>
    /// <returns>是否为敏感字段</returns>
    private static bool IsSensitiveProperty(string propertyName)
        => SensitiveProperties.Contains(propertyName);
}
