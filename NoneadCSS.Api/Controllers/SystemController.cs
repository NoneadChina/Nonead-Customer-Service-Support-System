using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using System.Diagnostics;
using System.Text;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace NoneadCSS.Api.Controllers;

[ApiController]
[Route("api/system")]
[Authorize]
[RequireModule("core")]
public class SystemController : ControllerBase
{
    // 注：_dbContext 仅用于 VACUUM、sqlite_master 查询、SELECT 1 健康检查等
    // 无法通过仓储抽象表达的底层 DB 操作；所有常规数据访问必须走 _unitOfWork。
    private readonly NoneadCSSDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SystemController> _logger;
    // 面向用户的返回文案走 `ILocalizationService`（DB 覆盖层 → `Messages.resx` 回落 → key），
    // 按 `Accept-Language` / 请求文化取值；日志文案不翻译（运维可读即可，见设计 §5 排除项）。
    private readonly ILocalizationService _localizer;

    public SystemController(NoneadCSSDbContext dbContext, IUnitOfWork unitOfWork, IWebHostEnvironment environment, ILogger<SystemController> logger, ILocalizationService localizer)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _environment = environment;
        _logger = logger;
        _localizer = localizer;
    }

    [HttpGet("status")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetSystemStatus()
    {
        var status = new SystemStatusResponse
        {
            Status = "healthy",
            Version = "1.0.0",
            Environment = _environment.EnvironmentName,
            Timestamp = DateTime.UtcNow,
            Database = new DatabaseStatus
            {
                Connected = true,
                Provider = "SQLite",
                TableCount = await GetTableCountAsync()
            },
            Resources = new SystemResources
            {
                Conversations = await _unitOfWork.Conversations.GetQueryable().CountAsync(c => c.State != ConversationState.Deleted),
                Customers = await _unitOfWork.Customers.CountAsync(),
                Users = await _unitOfWork.Users.CountAsync(),
                Mailboxes = await _unitOfWork.Mailboxes.GetQueryable().CountAsync(m => m.IsActive),
                SendLogs = await _unitOfWork.SendLogs.CountAsync(),
                Webhooks = await _unitOfWork.Webhooks.GetQueryable().CountAsync(w => w.IsActive),
                Workflows = await _unitOfWork.Workflows.GetQueryable().CountAsync(w => w.IsActive),
                Macros = await _unitOfWork.Macros.GetCountAsync(isActive: true)
            },
            Mailboxes = await GetMailboxStatusesAsync()
        };

        return Ok(status);
    }

    [HttpGet("info")]
    [RequireSuperAdmin]
    public IActionResult GetSystemInfo()
    {
        var info = new SystemInfoResponse
        {
            Version = "1.0.0",
            Platform = Environment.OSVersion.Platform.ToString(),
            OSVersion = Environment.OSVersion.VersionString,
            Framework = ".NET Core 10.0",
            Environment = _environment.EnvironmentName,
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            WorkingSet = Environment.WorkingSet / 1024 / 1024,
            StartupTime = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64),
            Features = new[]
            {
                "会话管理",
                "邮件收发",
                "电话会话",
                "高级搜索",
                "工作流自动化",
                "SLA管理",
                "宏操作",
                "满意度调查",
                "Webhook集成",
                "数据导入导出",
                "实时通知(SignalR)",
                "审计日志",
                "API密钥管理"
            }
        };

        return Ok(info);
    }

    [HttpPost("health-check")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HealthCheck()
    {
        var checks = new List<HealthCheckItem>();

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1");
            checks.Add(new HealthCheckItem { Name = _localizer["HealthCheckDbName"], Status = "healthy", Message = _localizer["HealthCheckDbOk"] });
        }
        catch (Exception ex)
        {
            // 记录完整异常到日志，响应只返回脱敏的通用消息，避免泄漏连接串/文件路径等敏感信息
            _logger.LogError(ex, "健康检查-数据库连接失败");
            checks.Add(new HealthCheckItem { Name = _localizer["HealthCheckDbName"], Status = "unhealthy", Message = _localizer["HealthCheckDbFail"] });
        }

        try
        {
            var mailboxes = await _unitOfWork.Mailboxes.GetQueryable().Where(m => m.IsActive).ToListAsync();
            checks.Add(new HealthCheckItem { Name = _localizer["HealthCheckMailboxName"], Status = "healthy", Message = _localizer.GetString("HealthCheckMailboxOk", mailboxes.Count) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "健康检查-邮箱配置查询失败");
            checks.Add(new HealthCheckItem { Name = _localizer["HealthCheckMailboxName"], Status = "unhealthy", Message = _localizer["HealthCheckMailboxFail"] });
        }

        try
        {
            var webhooks = await _unitOfWork.Webhooks.GetQueryable().Where(w => w.IsActive).ToListAsync();
            checks.Add(new HealthCheckItem { Name = _localizer["HealthCheckWebhookName"], Status = "healthy", Message = _localizer.GetString("HealthCheckWebhookOk", webhooks.Count) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "健康检查-Webhook配置查询失败");
            checks.Add(new HealthCheckItem { Name = _localizer["HealthCheckWebhookName"], Status = "unhealthy", Message = _localizer["HealthCheckWebhookFail"] });
        }

        var allHealthy = checks.All(c => c.Status == "healthy");

        return Ok(new { Status = allHealthy ? "healthy" : "degraded", Checks = checks });
    }

    private async Task<int> GetTableCountAsync()
    {
        // ExecuteSqlRawAsync 返回受影响行数（SELECT 恒为 -1），需用 ExecuteScalarAsync 获取聚合结果
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
        if (command.Transaction is null && _dbContext.Database.CurrentTransaction is not null)
        {
            command.Transaction = _dbContext.Database.CurrentTransaction.GetDbTransaction();
        }
        var result = await command.ExecuteScalarAsync();
        return result is long count ? (int)count : 0;
    }

    /// <summary>
    /// 在裸 DbConnection 上执行 VACUUM。
    /// SQLite VACUUM 不能在事务内运行，EF Core 默认会在事务中执行 ExecuteSqlRawAsync，
    /// 故使用独立连接、不开启事务来执行。
    /// </summary>
    private async Task ExecuteVacuumAsync()
    {
        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
            shouldClose = true;
        }
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM";
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    [HttpPost("tools/clear-cache")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearCache()
    {
        var stopwatch = Stopwatch.StartNew();
        var clearedTempFiles = 0;

        // 清理临时文件
        var tempPath = Path.Combine(_environment.ContentRootPath, "temp");
        if (Directory.Exists(tempPath))
        {
            var files = Directory.GetFiles(tempPath);
            clearedTempFiles += files.Length;
            foreach (var file in files)
            {
                try
                {
                    System.IO.File.Delete(file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "删除临时文件失败: {FilePath}", file);
                }
            }
        }

        // 清理上传的临时文件
        var uploadsTempPath = Path.Combine(_environment.ContentRootPath, "uploads", "temp");
        if (Directory.Exists(uploadsTempPath))
        {
            var files = Directory.GetFiles(uploadsTempPath);
            clearedTempFiles += files.Length;
            foreach (var file in files)
            {
                try
                {
                    System.IO.File.Delete(file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "删除上传临时文件失败: {FilePath}", file);
                }
            }
        }

        // 清理日志文件（保留最近7天）
        var logsPath = Path.Combine(_environment.ContentRootPath, "logs");
        if (Directory.Exists(logsPath))
        {
            var cutoffDate = DateTime.Now.AddDays(-7);
            var files = Directory.GetFiles(logsPath, "*.log");
            foreach (var file in files)
            {
                try
                {
                    var creationTime = System.IO.File.GetCreationTime(file);
                    if (creationTime < cutoffDate)
                    {
                        System.IO.File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "清理过期日志文件失败: {FilePath}", file);
                }
            }
        }

        // 清理过期的密码重置令牌
        var expiredTokens = await _unitOfWork.PasswordResetTokens.GetQueryable()
            .Where(t => t.ExpiresAt < DateTime.UtcNow || t.UsedAt != null)
            .ToListAsync();
        foreach (var token in expiredTokens)
        {
            await _unitOfWork.PasswordResetTokens.DeleteAsync(token);
        }
        await _unitOfWork.SaveChangesAsync();

        stopwatch.Stop();

        _logger.LogInformation("系统缓存已清理，耗时 {ElapsedMs}ms，清理临时文件 {ClearedTempFiles} 个，过期令牌 {ClearedTokens} 个",
            stopwatch.ElapsedMilliseconds, clearedTempFiles, expiredTokens.Count);

        return Ok(new
        {
            message = _localizer["SystemCacheCleared"],
            details = new
            {
                elapsedMs = stopwatch.ElapsedMilliseconds,
                clearedTempFiles,
                clearedExpiredTokens = expiredTokens.Count
            }
        });
    }

    [HttpPost("tools/logout-users")]
    [RequireSuperAdmin]
    public async Task<IActionResult> LogoutAllUsers()
    {
        // 通过递增全局令牌版本号，使所有已签发的 JWT 令牌失效
        // JWT 验证时会在 OnTokenValidated 事件中校验 token 的 gv claim 与当前版本号是否一致
        var currentVersionStr = await _unitOfWork.SystemOptions.GetValueAsync("security.token_global_version") ?? "0";

        var currentVersion = int.TryParse(currentVersionStr, out var v) ? v : 0;
        var newVersion = currentVersion + 1;

        await _unitOfWork.SystemOptions.SetAsync("security.token_global_version", newVersion.ToString(), "security");
        await _unitOfWork.SaveChangesAsync();

        var userCount = await _unitOfWork.Users.CountAsync();
        _logger.LogInformation("管理员已强制所有用户登出，令牌版本号从 {OldVersion} 递增到 {NewVersion}，影响 {UserCount} 个用户",
            currentVersion, newVersion, userCount);

        return Ok(new
        {
            message = _localizer.GetString("SystemForceLogoutSuccess", userCount),
            logoutCount = userCount,
            tokenVersion = newVersion
        });
    }

    [HttpPost("tools/optimize-db")]
    [RequireSuperAdmin]
    public async Task<IActionResult> OptimizeDatabase()
    {
        var stopwatch = Stopwatch.StartNew();

        // SQLite 数据库优化：VACUUM 不能在事务内运行，需使用裸 DbConnection 执行
        await ExecuteVacuumAsync();

        // 清理已删除的会话（软删除超过30天的）
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        var oldDeletedConversations = await _unitOfWork.Conversations.GetQueryable()
            .Where(c => c.State == ConversationState.Deleted && c.UpdatedAt < cutoffDate)
            .ToListAsync();

        if (oldDeletedConversations.Any())
        {
            foreach (var conversation in oldDeletedConversations)
            {
                await _unitOfWork.Conversations.DeleteAsync(conversation);
            }
            await _unitOfWork.SaveChangesAsync();
        }

        stopwatch.Stop();

        _logger.LogInformation("数据库优化完成，耗时 {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

        return Ok(new
        {
            message = _localizer["SystemDbOptimized"],
            details = new
            {
                elapsedMs = stopwatch.ElapsedMilliseconds,
                removedOldConversations = oldDeletedConversations.Count
            }
        });
    }

    [HttpGet("tools/logs")]
    [RequireSuperAdmin]
    [EnableRateLimiting("SystemLogsPolicy")]
    public IActionResult GetSystemLogs([FromQuery] int lines = 100)
    {
        // 限制行数上限，防止内存 DoS
        lines = Math.Min(Math.Max(lines, 1), 1000);

        // Serilog File sink 的相对路径 "logs/log-.log" 相对于 Directory.GetCurrentDirectory() 解析，
        // 而 _environment.ContentRootPath 可能与工作目录不同（如生产环境 start-prod.ps1
        // 设置 ASPNETCORE_CONTENTROOT=backend/ 但 WorkingDirectory=Production/）。
        // 解析逻辑与"清空日志文件"共用 SerilogLogFileMaintenance.ResolveLogsPath，
        // 确保"查看"与"清空"始终定位到同一日志目录，避免双日志分裂。
        var logsPath = SerilogLogFileMaintenance.ResolveLogsPath(_environment);
        if (logsPath == null)
        {
            return Ok(new { logs = Array.Empty<string>() });
        }

        // 只读取 Serilog File sink 写入的日志文件（log-*.log 模式），
        // 排除 backend-stdout.log、backend-stderr.log、frontend-*.log、nginx-*.log 等。
        // 这些非 Serilog 文件由 PowerShell Start-Process -RedirectStandardOutput 写入，
        // 在中文 Windows 上使用 GBK 编码，用 UTF-8 的 StreamReader 读取会产生乱码。
        // Serilog File sink 始终使用 UTF-8 without BOM 编码写入。
        var logFiles = Directory.GetFiles(logsPath, "log-*.log")
            .OrderByDescending(f => System.IO.File.GetLastWriteTime(f))
            .Take(3)
            .ToList();

        var allLogs = new List<string>();
        foreach (var logFile in logFiles)
        {
            try
            {
                // 使用 FileShare.ReadWrite 打开文件，避免与正在写入的日志进程冲突
                // 显式指定 UTF-8 编码（Serilog File sink 使用 UTF-8 without BOM）
                using var fileStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream, Encoding.UTF8);

                var fileLogs = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    fileLogs.Add(line);
                }
                allLogs.AddRange(fileLogs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取日志文件失败: {LogFile}", logFile);
            }
        }

        // 按时间戳排序（Serilog 每行以 yyyy-MM-dd HH:mm:ss 开头），取最新的 lines 行
        var sortedLogs = allLogs
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .OrderBy(l => l)
            .TakeLast(lines)
            .ToList();

        return Ok(new
        {
            logs = sortedLogs
        });
    }

    /// <summary>
    /// 删除系统日志中的 log 记录：清空日志目录下所有 log-*.log 文件内容（截断为 0 字节，保留文件本身）。
    /// 系统工具-系统日志「清空日志文件」按钮的后端载体，仅清理 Serilog 主日志（log-*.log），
    /// 与 GetSystemLogs 展示范围保持一致；Serilog 以 shared:true 追加写入，截断后新日志正常追加。
    /// </summary>
    [HttpPost("tools/logs/clear")]
    [RequireSuperAdmin]
    public IActionResult ClearSystemLogs()
    {
        var logsPath = SerilogLogFileMaintenance.ResolveLogsPath(_environment);
        if (logsPath == null)
        {
            return Ok(new { success = true, cleared = 0, freedBytes = 0, failed = 0, message = "未找到日志目录，无需清理" });
        }

        var result = SerilogLogFileMaintenance.ClearLogFiles(logsPath, _logger);

        _logger.LogInformation("系统日志已清空：{ClearedCount} 个文件，释放 {FreedBytes} 字节，失败 {FailedCount} 个",
            result.ClearedCount, result.FreedBytes, result.FailedFiles.Count);

        return Ok(new
        {
            success = true,
            cleared = result.ClearedCount,
            freedBytes = result.FreedBytes,
            failed = result.FailedFiles.Count,
            failedFiles = result.FailedFiles,
            message = result.FailedFiles.Count == 0
                ? _localizer.GetString("SystemClearLogsSuccess", result.ClearedCount, result.FreedBytes)
                : _localizer.GetString("SystemClearLogsPartial", result.ClearedCount, result.FailedFiles.Count)
        });
    }

    private async Task<List<MailboxStatus>> GetMailboxStatusesAsync()
    {
        var mailboxes = await _unitOfWork.Mailboxes.GetQueryable()
            .Include(m => m.MailboxUsers)
            .Where(m => m.IsActive)
            .ToListAsync();

        return mailboxes.Select(m => new MailboxStatus
        {
            Id = m.Id,
            Name = m.Name,
            Email = m.Email,
            UserCount = m.MailboxUsers.Count,
            HasPassword = !string.IsNullOrEmpty(m.InPassword) || !string.IsNullOrEmpty(m.OutPassword),
            IsActive = m.IsActive
        }).ToList();
    }
}
