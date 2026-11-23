using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// Serilog 日志文件维护：日志目录解析 + 清空 log-*.log 文件内容。
/// 供「系统管理 → 系统工具 → 系统日志」的"清空日志文件"功能使用，
/// 真正删除系统日志中的 log 记录（截断文件内容为 0 字节）。
/// 与 SystemController.GetSystemLogs 共用同一候选路径解析逻辑，
/// 避免"清空"与"查看"定位到不同目录（CWD/ContentRoot 双日志分裂隐患）。
/// </summary>
public static class SerilogLogFileMaintenance
{
    /// <summary>Serilog File sink 写入的文件名模式（排除 backend-stdout/nginx 等非 Serilog 文件）。</summary>
    public const string LogFileNamePattern = "log-*.log";

    /// <summary>
    /// 解析 Serilog 日志目录：
    /// 优先 Directory.GetCurrentDirectory()/logs（与 Serilog File sink 默认相对路径解析一致），
    /// 回退 ContentRootPath/logs（ASPNETCORE_CONTENTROOT 独立部署场景）。
    /// 仅当目录存在且包含至少一个 log-*.log 文件时才选中，避免命中"目录存在但文件为空"的误判。
    /// </summary>
    public static string? ResolveLogsPath(IHostEnvironment environment)
    {
        var candidatePaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "logs"),
            Path.Combine(environment.ContentRootPath, "logs")
        };

        string? logsPath = null;
        foreach (var candidate in candidatePaths)
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            try
            {
                if (Directory.GetFiles(candidate, LogFileNamePattern).Length > 0)
                {
                    return candidate;
                }
                logsPath ??= candidate; // 目录存在但尚无匹配文件：记住第一个存在的目录作为最后回退
            }
            catch (Exception ex)
            {
                _ = ex; // 目录不可枚举：跳过该候选
            }
        }

        return logsPath;
    }

    /// <summary>
    /// 清空日志目录下所有 log-*.log 文件内容（截断为 0 字节，保留文件本身）。
    /// Serilog File sink 以 FileMode.Append + FileShare.ReadWrite（shared: true）打开文件，
    /// 截断后下一次写入会自动定位到新文件末尾，新日志正常追加，不会产生空洞字节。
    /// </summary>
    /// <returns>清理统计：成功清空数、失败文件列表、释放字节数。</returns>
    public static ClearLogsResult ClearLogFiles(string logsPath, ILogger logger)
    {
        var files = Directory.GetFiles(logsPath, LogFileNamePattern);
        var clearedCount = 0;
        var failedFiles = new List<string>();
        long freedBytes = 0;

        foreach (var file in files)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                freedBytes += stream.Length;
                stream.SetLength(0);
                clearedCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "清空日志文件失败（文件可能正被写入占用）：{FilePath}", file);
                failedFiles.Add(file);
            }
        }

        return new ClearLogsResult
        {
            ClearedCount = clearedCount,
            FailedFiles = failedFiles,
            FreedBytes = freedBytes
        };
    }
}

/// <summary>日志清空结果统计。</summary>
public sealed class ClearLogsResult
{
    /// <summary>成功清空的日志文件数。</summary>
    public int ClearedCount { get; init; }

    /// <summary>清空失败（被占用）的文件完整路径列表。</summary>
    public IReadOnlyList<string> FailedFiles { get; init; } = Array.Empty<string>();

    /// <summary>本次清空释放的磁盘字节数。</summary>
    public long FreedBytes { get; init; }
}
