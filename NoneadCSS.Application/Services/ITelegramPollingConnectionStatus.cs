namespace NoneadCSS.Application.Services;

/// <summary>
/// Telegram 轮询（getUpdates）后台服务的运行状态（由 HostedService 写入，状态页读取）。
/// 与 IDingTalkStreamConnectionStatus / IFeishuStreamConnectionStatus 同构：单例、进程内、加锁读写。
/// </summary>
public interface ITelegramPollingConnectionStatus
{
    /// <summary>轮询循环是否在运行（已启用 polling 模式且 HostedService 已开始）</summary>
    bool IsRunning { get; }

    /// <summary>最近一次活动时间（UTC：发起 getUpdates 或处理完一批 Update）</summary>
    DateTime? LastActivityAt { get; }

    /// <summary>最近一次错误（含 409 webhook 冲突、429 限流、网络异常）</summary>
    string? LastError { get; }

    /// <summary>标记轮询已启动</summary>
    void SetRunning();

    /// <summary>标记轮询已停止（模式关闭 / 未配置 / 服务退出）</summary>
    void SetStopped(string? reason = null);

    /// <summary>记录一次成功活动（清空错误）</summary>
    void MarkActivity();

    /// <summary>记录一次错误（保持运行，下轮继续）</summary>
    void MarkError(string error);
}

/// <summary>默认实现：进程内单例，仅用于状态展示，不参与业务判定</summary>
public sealed class TelegramPollingConnectionStatus : ITelegramPollingConnectionStatus
{
    private readonly object _lock = new();
    private bool _isRunning;
    private DateTime? _lastActivityAt;
    private string? _lastError;

    public bool IsRunning
    {
        get { lock (_lock) { return _isRunning; } }
    }

    public DateTime? LastActivityAt
    {
        get { lock (_lock) { return _lastActivityAt; } }
    }

    public string? LastError
    {
        get { lock (_lock) { return _lastError; } }
    }

    public void SetRunning()
    {
        lock (_lock)
        {
            _isRunning = true;
            _lastActivityAt = DateTime.UtcNow;
        }
    }

    public void SetStopped(string? reason = null)
    {
        lock (_lock)
        {
            _isRunning = false;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _lastError = reason;
            }
        }
    }

    public void MarkActivity()
    {
        lock (_lock)
        {
            _isRunning = true;
            _lastActivityAt = DateTime.UtcNow;
            _lastError = null;
        }
    }

    public void MarkError(string error)
    {
        lock (_lock)
        {
            _lastActivityAt = DateTime.UtcNow;
            _lastError = error;
        }
    }
}
