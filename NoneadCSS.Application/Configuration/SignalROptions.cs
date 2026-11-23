namespace NoneadCSS.Application.Configuration;

/// <summary>
/// SignalR 实时通信配置选项
/// </summary>
public class SignalROptions
{
    /// <summary>KeepAlive 心跳间隔（秒）</summary>
    public int KeepAliveIntervalSeconds { get; set; } = 15;

    /// <summary>客户端超时间隔（秒），超过此时间未收到心跳则断开连接</summary>
    public int ClientTimeoutIntervalSeconds { get; set; } = 30;
}
