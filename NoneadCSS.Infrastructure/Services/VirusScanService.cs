using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using nClam;

namespace NoneadCSS.Infrastructure.Services;

public interface IVirusScanService
{
    Task<VirusScanResult> ScanAsync(Stream fileStream, string fileName, string mimeType);
    Task<VirusScanResult> ScanFileAsync(string filePath, string fileName, string mimeType);
}

public class VirusScanResult
{
    public bool IsClean { get; set; }
    public string? VirusName { get; set; }
    public string? Message { get; set; }
    public ScanStatus Status { get; set; }
}

public enum ScanStatus
{
    Clean,
    Infected,
    Error,
    Skipped
}

/// <summary>
/// 病毒扫描配置选项
/// </summary>
public class VirusScanOptions
{
    /// <summary>
    /// 是否启用 ClamAV 引擎（默认 false，使用内置特征码扫描）
    /// </summary>
    public bool EnableClamAv { get; set; } = false;

    /// <summary>
    /// ClamAV 服务器地址（默认 localhost）
    /// </summary>
    public string ClamAvHost { get; set; } = "localhost";

    /// <summary>
    /// ClamAV 服务器端口（默认 3310）
    /// </summary>
    public int ClamAvPort { get; set; } = 3310;

    /// <summary>
    /// ClamAV 连接超时（毫秒，默认 5000）
    /// </summary>
    public int ClamAvTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// ClamAV 不可用时是否阻止上传（true=阻止/fail-closed，false=降级到内置扫描）
    /// 默认 true：安全优先，防止扫描服务降级时恶意文件绕过检测。
    /// H-04：安全默认值为 fail-closed。即便管理员显式置为 false，当 EnableClamAv=true 且
    /// 扫描引擎实际不可达/返回未知结果/构造失败时，仍强制 fail-closed，以避免
    /// "以为启用了 ClamAV 实际被旁路" 的风险。
    /// </summary>
    public bool BlockOnClamAvUnavailable { get; set; } = true;
}

public class VirusScanService : IVirusScanService
{
    private readonly ILogger<VirusScanService> _logger;
    private readonly VirusScanOptions _options;
    private readonly ClamClient? _clamClient;
    // H-04：当 EnableClamAv=true 但 ClamClient 构造/初始化失败（例如 nClam 解析 host 失败、
    // options 非法等）时，标记为"引擎期望启用但实际不可用"，强制所有扫描 fail-closed。
    private readonly bool _clamAvExpectedButUninitialized;

    /// <summary>
    /// 已知的恶意文件特征码（仅包含真正恶意的内容，不包含标准文件格式头）。
    /// 标准文件格式头（如 PNG/JPEG/PDF/ZIP 等）由 AllowedExtensions 和 MIME 类型校验负责过滤，
    /// 不应在此处作为恶意特征拦截，否则会导致正常文件无法上传。
    /// </summary>
    private readonly byte[][] _maliciousSignatures =
    {
        // EICAR 测试病毒签名
        System.Text.Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"),
    };

    private readonly HashSet<string> _executableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".wsf", ".scr",
        ".pif", ".hta", ".msi", ".msp", ".mst", ".cab", ".jar", ".class", ".py", ".pl", ".rb",
        ".php", ".asp", ".aspx", ".jsp", ".jspx", ".cgi", ".sh", ".ksh", ".zsh", ".bash",
        ".apk", ".app", ".dmg", ".pkg", ".deb", ".rpm"
    };

    private readonly HashSet<string> _suspiciousMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/x-dosexec",
        "application/x-msdownload",
        "application/x-executable",
        "application/x-bat",
        "application/x-cmd",
        "application/x-sh",
        "application/x-shellscript",
        "text/x-script",
        "application/x-perl",
        "application/x-python",
        "application/x-ruby",
        "application/x-java-applet",
        "application/java-archive"
    };

    public VirusScanService(ILogger<VirusScanService> logger, IOptions<VirusScanOptions>? options = null)
    {
        _logger = logger;
        _options = options?.Value ?? new VirusScanOptions();

        if (_options.EnableClamAv)
        {
            try
            {
                _clamClient = new ClamClient(_options.ClamAvHost, _options.ClamAvPort);
                _logger.LogInformation("ClamAV 病毒扫描已启用: {Host}:{Port}, BlockOnClamAvUnavailable={BlockFailClosed}",
                    _options.ClamAvHost, _options.ClamAvPort, _options.BlockOnClamAvUnavailable);
            }
            catch (Exception ex)
            {
                // H-04：构造 ClamClient 失败（如非法 host、底层依赖异常等），标记为 fail-closed。
                _clamAvExpectedButUninitialized = true;
                _logger.LogCritical(ex,
                    "ClamAV 已启用（EnableClamAv=true）但 ClamClient 初始化失败，将强制 fail-closed 拒绝所有附件上传。请修复 VirusScan:ClamAv* 配置");
            }
        }
        else
        {
            _logger.LogInformation("ClamAV 未启用，仅使用内置特征码/扩展名/MIME 检查（上传文件不会经过引擎级扫描）");
        }
    }

    public async Task<VirusScanResult> ScanAsync(Stream fileStream, string fileName, string mimeType)
    {
        try
        {
            var originalPosition = fileStream.Position;
            fileStream.Position = 0;

            // 第一层：扩展名和 MIME 类型校验（最关键的防线）
            if (IsExecutableExtension(fileName))
            {
                fileStream.Position = originalPosition;
                _logger.LogWarning("病毒扫描阻止可执行文件: FileName={FileName}", fileName);
                return new VirusScanResult
                {
                    IsClean = false,
                    VirusName = "ExecutableBlocked",
                    Message = "不允许上传可执行文件",
                    Status = ScanStatus.Infected
                };
            }

            if (IsSuspiciousMimeType(mimeType) && !IsSafeExtension(fileName))
            {
                fileStream.Position = originalPosition;
                _logger.LogWarning("病毒扫描阻止可疑MIME类型: FileName={FileName}, MimeType={MimeType}", fileName, mimeType);
                return new VirusScanResult
                {
                    IsClean = false,
                    VirusName = "SuspiciousMimeType",
                    Message = "文件类型与MIME类型不匹配",
                    Status = ScanStatus.Infected
                };
            }

            if (!VerifyFileExtension(fileName, mimeType))
            {
                fileStream.Position = originalPosition;
                _logger.LogWarning("病毒扫描发现扩展名欺骗: FileName={FileName}, MimeType={MimeType}", fileName, mimeType);
                return new VirusScanResult
                {
                    IsClean = false,
                    VirusName = "ExtensionSpoofing",
                    Message = "文件扩展名与实际类型不匹配",
                    Status = ScanStatus.Infected
                };
            }

            // 第二层：内置特征码扫描（扫描文件头部 64KB + 尾部 4KB）
            var signatureResult = await ScanWithSignaturesAsync(fileStream, fileName);
            if (!signatureResult.IsClean)
            {
                fileStream.Position = originalPosition;
                return signatureResult;
            }

            // 第三层：ClamAV 引擎扫描（如果启用）。H-04：若 EnableClamAv=true 但客户端初始化失败，
            // 直接 fail-closed 拒绝上传，避免"管理员以为启用了引擎实际不可用"的误判。
            if (_options.EnableClamAv)
            {
                if (_clamAvExpectedButUninitialized || _clamClient == null)
                {
                    fileStream.Position = originalPosition;
                    _logger.LogCritical(
                        "H-04 fail-closed: EnableClamAv=true 但 ClamAV 客户端未就绪，拒绝文件上传: FileName={FileName}", fileName);
                    return new VirusScanResult
                    {
                        IsClean = false,
                        Message = "病毒扫描服务未就绪（配置错误），已按 fail-closed 策略拒绝上传",
                        Status = ScanStatus.Error
                    };
                }

                fileStream.Position = 0;
                var clamResult = await ScanWithClamAvAsync(fileStream, fileName);
                if (!clamResult.IsClean)
                {
                    fileStream.Position = originalPosition;
                    return clamResult;
                }
            }

            fileStream.Position = originalPosition;
            _logger.LogInformation("病毒扫描通过: FileName={FileName}, ClamAv={ClamAvEnabled}", fileName, _clamClient != null);
            return new VirusScanResult { IsClean = true, Status = ScanStatus.Clean };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "病毒扫描失败: FileName={FileName}", fileName);
            return new VirusScanResult
            {
                IsClean = false,
                Message = "扫描失败",
                Status = ScanStatus.Error
            };
        }
    }

    public async Task<VirusScanResult> ScanFileAsync(string filePath, string fileName, string mimeType)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return await ScanAsync(stream, fileName, mimeType);
    }

    /// <summary>
    /// 内置特征码扫描
    /// </summary>
    private async Task<VirusScanResult> ScanWithSignaturesAsync(Stream fileStream, string fileName)
    {
        const int headerScanSize = 64 * 1024; // 64KB
        const int tailScanSize = 4 * 1024;    // 4KB

        var headerSize = Math.Min((int)(fileStream.Length - fileStream.Position), headerScanSize);
        if (headerSize < 4)
        {
            return new VirusScanResult
            {
                IsClean = false,
                VirusName = "TooSmall",
                Message = "文件太小，可能是恶意文件",
                Status = ScanStatus.Infected
            };
        }

        var headerBytes = new byte[headerSize];
        var headerBytesRead = await fileStream.ReadAsync(headerBytes, 0, headerSize);

        var headerResult = ScanBufferForSignatures(headerBytes.AsSpan(0, headerBytesRead));
        if (!headerResult.IsClean)
        {
            _logger.LogWarning("特征码扫描发现恶意文件头: FileName={FileName}, Virus={VirusName}", fileName, headerResult.VirusName);
            return headerResult;
        }

        // 扫描文件尾部
        if (fileStream.Length > tailScanSize)
        {
            fileStream.Position = fileStream.Length - tailScanSize;
            var tailBytes = new byte[tailScanSize];
            var tailBytesRead = await fileStream.ReadAsync(tailBytes, 0, tailScanSize);

            var tailResult = ScanBufferForSignatures(tailBytes.AsSpan(0, tailBytesRead));
            if (!tailResult.IsClean)
            {
                _logger.LogWarning("特征码扫描发现恶意文件尾: FileName={FileName}, Virus={VirusName}", fileName, tailResult.VirusName);
                return tailResult;
            }
        }

        return new VirusScanResult { IsClean = true, Status = ScanStatus.Clean };
    }

    /// <summary>
    /// 使用 ClamAV 引擎扫描文件
    /// </summary>
    private async Task<VirusScanResult> ScanWithClamAvAsync(Stream fileStream, string fileName)
    {
        if (_clamClient == null)
        {
            return new VirusScanResult { IsClean = true, Status = ScanStatus.Skipped };
        }

        try
        {
            // 使用带超时的 Task 包装，防止 ClamAV 不可达时长时间阻塞
            using var cts = new CancellationTokenSource(_options.ClamAvTimeoutMs);
            var scanTask = _clamClient.SendAndScanFileAsync(fileStream);
            var completedTask = await Task.WhenAny(scanTask, Task.Delay(_options.ClamAvTimeoutMs, cts.Token));

            if (completedTask != scanTask)
            {
                _logger.LogCritical("H-04 fail-closed: ClamAV 扫描超时（EnableClamAv=true），强制拒绝上传: FileName={FileName}, Timeout={Timeout}ms", fileName, _options.ClamAvTimeoutMs);
                return new VirusScanResult
                {
                    IsClean = false,
                    Message = "病毒扫描服务超时（fail-closed），上传被拒绝",
                    Status = ScanStatus.Error
                };
            }

            var clamResult = await scanTask;

            switch (clamResult.Result)
            {
                case ClamScanResults.Clean:
                    _logger.LogDebug("ClamAV 扫描通过: FileName={FileName}", fileName);
                    return new VirusScanResult { IsClean = true, Status = ScanStatus.Clean };

                case ClamScanResults.VirusDetected:
                    _logger.LogWarning("ClamAV 检测到病毒: FileName={FileName}, Virus={VirusName}", fileName, clamResult.InfectedFiles?.FirstOrDefault()?.VirusName);
                    return new VirusScanResult
                    {
                        IsClean = false,
                        VirusName = clamResult.InfectedFiles?.FirstOrDefault()?.VirusName ?? "ClamAV.VirusDetected",
                        Message = $"检测到病毒: {clamResult.InfectedFiles?.FirstOrDefault()?.VirusName}",
                        Status = ScanStatus.Infected
                    };

                case ClamScanResults.Error:
                    _logger.LogCritical("H-04 fail-closed: ClamAV 扫描出错（EnableClamAv=true），强制拒绝上传: FileName={FileName}", fileName);
                    return new VirusScanResult
                    {
                        IsClean = false,
                        Message = "病毒扫描服务错误（fail-closed），上传被拒绝",
                        Status = ScanStatus.Error
                    };

                default:
                    _logger.LogWarning("ClamAV 返回未知结果: FileName={FileName}, Result={Result}", fileName, clamResult.Result);
                    // H-04：未知结果视为引擎不可用。无论 BlockOnClamAvUnavailable 是否为 false，
                    // 只要 EnableClamAv=true 就强制 fail-closed（管理员可显式 EnableClamAv=false 回到仅签名扫描）。
                    return new VirusScanResult
                    {
                        IsClean = false,
                        Message = "病毒扫描服务返回未知结果（fail-closed）",
                        Status = ScanStatus.Error
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "H-04 fail-closed: ClamAV 连接异常（EnableClamAv=true），强制拒绝上传: FileName={FileName}", fileName);
            return new VirusScanResult
            {
                IsClean = false,
                Message = "病毒扫描服务不可用（fail-closed），上传被拒绝",
                Status = ScanStatus.Error
            };
        }
    }

    private VirusScanResult ScanBufferForSignatures(Span<byte> buffer)
    {
        foreach (var signature in _maliciousSignatures)
        {
            if (buffer.Length < signature.Length)
                continue;

            var sigSpan = signature.AsSpan();
            for (var i = 0; i <= buffer.Length - signature.Length; i++)
            {
                if (buffer.Slice(i, signature.Length).SequenceEqual(sigSpan))
                {
                    return new VirusScanResult
                    {
                        IsClean = false,
                        VirusName = "MaliciousSignature",
                        Message = "文件包含恶意特征",
                        Status = ScanStatus.Infected
                    };
                }
            }
        }

        return new VirusScanResult { IsClean = true, Status = ScanStatus.Clean };
    }

    private bool IsExecutableExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && _executableExtensions.Contains(extension);
    }

    private bool IsSuspiciousMimeType(string mimeType)
    {
        return !string.IsNullOrEmpty(mimeType) && _suspiciousMimeTypes.Contains(mimeType);
    }

    private bool IsSafeExtension(string fileName)
    {
        var safeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".csv", ".json", ".xml", ".html", ".css", ".js", ".ts", ".tsx", ".jsx",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz"
        };
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && safeExtensions.Contains(extension);
    }

    private bool VerifyFileExtension(string fileName, string mimeType)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        // application/octet-stream 是最通用的 MIME 类型，表示未知二进制数据。
        // 很多邮件客户端发送附件时会使用此类型（无论实际文件类型是什么），
        // 对此类型放行，避免正常附件被误判为 ExtensionSpoofing 而被静默丢弃。
        // 安全性由后续的特征码扫描和 ClamAV 引擎负责。
        if (string.Equals(mimeType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extensionMimeTypeMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { ".jpg", new[] { "image/jpeg", "image/pjpeg" } },
            { ".jpeg", new[] { "image/jpeg", "image/pjpeg" } },
            { ".png", new[] { "image/png" } },
            { ".gif", new[] { "image/gif" } },
            { ".bmp", new[] { "image/bmp" } },
            { ".webp", new[] { "image/webp" } },
            { ".svg", new[] { "image/svg+xml" } },
            { ".pdf", new[] { "application/pdf" } },
            { ".txt", new[] { "text/plain" } },
            { ".md", new[] { "text/markdown", "text/plain" } },
            { ".csv", new[] { "text/csv", "application/csv" } },
            { ".json", new[] { "application/json" } },
            { ".xml", new[] { "application/xml", "text/xml" } },
            { ".html", new[] { "text/html" } },
            { ".css", new[] { "text/css" } },
            { ".js", new[] { "application/javascript", "text/javascript" } },
            { ".ts", new[] { "application/typescript", "text/typescript" } },
            { ".doc", new[] { "application/msword", "application/octet-stream" } },
            { ".docx", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/octet-stream" } },
            { ".xls", new[] { "application/vnd.ms-excel", "application/octet-stream" } },
            { ".xlsx", new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/octet-stream" } },
            { ".ppt", new[] { "application/vnd.ms-powerpoint", "application/octet-stream" } },
            { ".pptx", new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation", "application/octet-stream" } },
            { ".zip", new[] { "application/zip", "application/x-zip-compressed" } },
            { ".rar", new[] { "application/x-rar-compressed" } },
            { ".7z", new[] { "application/x-7z-compressed" } }
        };

        if (!extensionMimeTypeMap.TryGetValue(extension, out var expectedMimeTypes))
        {
            return true;
        }

        return expectedMimeTypes.Any(m => m.Equals(mimeType, StringComparison.OrdinalIgnoreCase));
    }
}
