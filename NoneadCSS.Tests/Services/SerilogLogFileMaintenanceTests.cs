using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NoneadCSS.Infrastructure.Services;
using Serilog;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 系统工具-系统日志「清空日志文件」功能（删除系统日志中的 log 记录）集成测试。
/// 被测对象：<see cref="SerilogLogFileMaintenance"/>（SystemController POST /api/system/tools/logs/clear 的载体）。
/// 覆盖：正常清空（截断为 0 字节）、被占用文件失败上报、日志目录解析回退、
///      以及关键回归——Serilog shared:true Append 写入器在截断后继续追加不产生空洞/残留。
/// </summary>
public class SerilogLogFileMaintenanceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"serilog-maintenance-{Guid.NewGuid():N}");
    private readonly Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;

    public SerilogLogFileMaintenanceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            // 忽略清理失败（文件可能仍被占用）
        }
    }

    private string WriteLogFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ClearLogFiles_TruncatesAllLogFiles_AndReportsStats()
    {
        // 模拟 Serilog 主日志 + 历史滚动文件
        var f1 = WriteLogFile("log-20260901.log", "line1\nline2\nline3\n");
        var f2 = WriteLogFile("log-20260831.log", new string('x', 2048));
        var f1Size = new FileInfo(f1).Length;
        var f2Size = new FileInfo(f2).Length;

        var result = SerilogLogFileMaintenance.ClearLogFiles(_tempDir, _logger);

        result.ClearedCount.Should().Be(2, "所有 log-*.log 文件都必须被清空");
        result.FailedFiles.Should().BeEmpty();
        result.FreedBytes.Should().Be(f1Size + f2Size, "释放字节数应等于清空前各文件大小之和");
        new FileInfo(f1).Length.Should().Be(0, "文件内容必须被截断为 0 字节（log 记录已删除）");
        new FileInfo(f2).Length.Should().Be(0);
    }

    [Fact]
    public void ClearLogFiles_LockedFile_IsReportedAsFailed_OthersCleared()
    {
        var freeFile = WriteLogFile("log-free.log", "free-content\n");
        var lockedPath = Path.Combine(_tempDir, "log-locked.log");
        File.WriteAllText(lockedPath, "locked-content\n");

        // 模拟旧版 Serilog 独占（FileShare.Read，不允许外部写）：该文件无法被截断
        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = SerilogLogFileMaintenance.ClearLogFiles(_tempDir, _logger);

        result.ClearedCount.Should().Be(1, "未占用文件应正常清空");
        result.FailedFiles.Should().ContainSingle().Which.Should().Be(lockedPath, "被占用文件必须明确上报失败，供前端提示");
        new FileInfo(freeFile).Length.Should().Be(0);
        new FileInfo(lockedPath).Length.Should().BeGreaterThan(0, "被占用文件内容保持不变，不产生部分写入");
    }

    [Fact]
    public void ResolveLogsPath_FallsBackToContentRootLogs_WhenCurrentDirHasNone()
    {
        // 模拟生产独立部署：CurrentDirectory（bin）下无 logs，ContentRootPath 下存在 logs
        var logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "log-20260901.log"), "x\n");

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(_tempDir);

        var resolved = SerilogLogFileMaintenance.ResolveLogsPath(env.Object);

        resolved.Should().Be(logsDir, "查看与清空必须定位到同一日志目录，避免双日志分裂");
    }

    [Fact]
    public void ClearLogFiles_NoLogFiles_ReturnsZero()
    {
        var result = SerilogLogFileMaintenance.ClearLogFiles(_tempDir, _logger);

        result.ClearedCount.Should().Be(0);
        result.FailedFiles.Should().BeEmpty();
        result.FreedBytes.Should().Be(0);
    }

    [Fact]
    public async Task ClearLogFiles_RealSerilogSharedSink_ContinuesAppendingAfterTruncate_WithoutGarbage()
    {
        // 关键回归：生产 Serilog File sink 配置 shared:true（SharedFileSink，每次写入前 Seek(0, End)）。
        // 清空（SetLength(0)）后，同一 sink 继续写日志必须定位到新文件末尾，
        // 不能产生 NUL 空洞或残留被清空的旧记录。
        var path = Path.Combine(_tempDir, "log-serilog-shared.log");

        string[] remaining;
        using (var log = new LoggerConfiguration()
            .WriteTo.File(path, shared: true, rollingInterval: RollingInterval.Infinite)
            .CreateLogger())
        {
            log.Information("before-clear-1");
            log.Information("before-clear-2");

            var result = SerilogLogFileMaintenance.ClearLogFiles(_tempDir, _logger);
            result.ClearedCount.Should().Be(1);
            new FileInfo(path).Length.Should().Be(0, "清空后文件必须为 0 字节");

            // 模拟服务清空后 Serilog 继续追加新日志
            log.Information("after-clear-3");
        }

        // using 块结束后 sink 已释放，可安全读取
        remaining = await File.ReadAllLinesAsync(path);
        remaining.Should().ContainSingle(l => l.Contains("after-clear-3"),
            "截断后新日志必须正常追加（shared:true SharedFileSink 写前 seek 到文件末尾）");
        remaining.Should().NotContain(l => l.Contains("before-clear"), "被清空的旧记录不得残留");
        remaining.Should().NotContain(l => l.Contains('\0'), "不得产生 NUL 空洞字节");
    }
}
