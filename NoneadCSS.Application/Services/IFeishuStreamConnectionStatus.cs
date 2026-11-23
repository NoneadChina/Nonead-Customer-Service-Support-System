namespace NoneadCSS.Application.Services;

/// <summary>
/// 飞书 Stream 长连接状态跟踪器（单例）。
/// 由 FeishuStreamClient 在连接成功/断开时更新，
/// 供 FeishuService.GetStatusAsync 查询，避免在 Stream 模式下重复请求 Access Token API。
/// </summary>
public interface IFeishuStreamConnectionStatus
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

    /// <summary>
    /// 触发立即重连（配置变更后调用）。
    /// 返回一个可等待的 Task，供 Stream 客户端在退避延迟中提前退出。
    /// </summary>
    void TriggerReconnect();

    /// <summary>
    /// 等待重连信号。在退避延迟中调用，若 TriggerReconnect 被触发则提前返回 true。
    /// </summary>
    Task<bool> WaitForReconnectSignalAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>
/// IFeishuStreamConnectionStatus 的线程安全实现，注册为 Singleton。
/// 使用 volatile + lock 保证多线程读写的可见性和一致性。
/// </summary>
public sealed class FeishuStreamConnectionStatus : IFeishuStreamConnectionStatus
{
    private readonly object _lock = new();
    private bool _isConnected;
    private DateTime? _lastConnectedAt;
    private string? _lastError;
    private TaskCompletionSource<bool>? _reconnectTcs;

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

    public void TriggerReconnect()
    {
        lock (_lock)
        {
            _reconnectTcs?.TrySetResult(true);
            _reconnectTcs = null;
        }
    }

    public async Task<bool> WaitForReconnectSignalAsync(TimeSpan delay, CancellationToken ct)
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            _reconnectTcs?.TrySetResult(false); // 取消旧的等待
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _reconnectTcs = tcs;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(delay);

        try
        {
            // 注册取消：当延迟到期或外部取消时，完成 TCS
            await using var registration = linkedCts.Token.Register(() => tcs.TrySetResult(false));
            return await tcs.Task;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
