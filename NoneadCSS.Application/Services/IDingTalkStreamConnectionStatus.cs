namespace NoneadCSS.Application.Services;

/// <summary>
/// 钉钉 Stream 长连接状态跟踪器（单例）。
/// 由 DingTalkStreamClient 在连接成功/断开时更新，
/// 供 DingDingService.GetStatusAsync 查询，避免在 Stream 模式下重复请求 Access Token API。
/// </summary>
public interface IDingTalkStreamConnectionStatus
{
    /// <summary>Stream 长连接是否已建立</summary>
    bool IsConnected { get; }

    /// <summary>最近一次连接成功时间（UTC）</summary>
    DateTime? LastConnectedAt { get; }

    /// <summary>最近一次断开原因（连接成功后清空）</summary>
    string? LastError { get; }

    /// <summary>标记连接已建立</summary>
    void SetConnected();

    /// <summary>标记连接已断开</summary>
    /// <param name="error">断开原因</param>
    void SetDisconnected(string? error = null);
}

/// <summary>
/// IDingTalkStreamConnectionStatus 的线程安全实现，注册为 Singleton。
/// 使用 volatile + lock 保证多线程读写的可见性和一致性。
/// </summary>
public sealed class DingTalkStreamConnectionStatus : IDingTalkStreamConnectionStatus
{
    private readonly object _lock = new();
    private bool _isConnected;
    private DateTime? _lastConnectedAt;
    private string? _lastError;

    public bool IsConnected
    {
        get { lock (_lock) return _isConnected; }
    }

    public DateTime? LastConnectedAt
    {
        get { lock (_lock) return _lastConnectedAt; }
    }

    public string? LastError
    {
        get { lock (_lock) return _lastError; }
    }

    public void SetConnected()
    {
        lock (_lock)
        {
            _isConnected = true;
            _lastConnectedAt = DateTime.UtcNow;
            _lastError = null;
        }
    }

    public void SetDisconnected(string? error = null)
    {
        lock (_lock)
        {
            _isConnected = false;
            _lastError = error;
        }
    }
}
