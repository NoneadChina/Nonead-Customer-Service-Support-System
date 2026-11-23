namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// SignalR 连接管理器：维护 userId 与 connectionId(s) 的映射。
/// 由于 IHubContext 无法直接按 userId 操作组，需通过此管理器追踪连接后
/// 才能在服务端（非 Hub 上下文）将用户加入/移出特定组。
/// </summary>
public interface IConnectionManager
{
    void AddConnection(int userId, string connectionId);
    void RemoveConnection(string connectionId);
    IEnumerable<string> GetConnectionIds(int userId);
    bool IsUserOnline(int userId);
    int GetOnlineUserCount();
    IEnumerable<int> GetOnlineUserIds();
}

public class ConnectionManager : IConnectionManager
{
    private readonly Dictionary<int, HashSet<string>> _userConnections = new();
    private readonly Dictionary<string, int> _connectionToUser = new();
    private readonly object _lock = new();

    public void AddConnection(int userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_userConnections.ContainsKey(userId))
            {
                _userConnections[userId] = new HashSet<string>();
            }
            _userConnections[userId].Add(connectionId);
            _connectionToUser[connectionId] = userId;
        }
    }

    public void RemoveConnection(string connectionId)
    {
        lock (_lock)
        {
            if (_connectionToUser.TryGetValue(connectionId, out var userId))
            {
                _connectionToUser.Remove(connectionId);
                if (_userConnections.TryGetValue(userId, out var connections))
                {
                    connections.Remove(connectionId);
                    if (connections.Count == 0)
                    {
                        _userConnections.Remove(userId);
                    }
                }
            }
        }
    }

    public IEnumerable<string> GetConnectionIds(int userId)
    {
        lock (_lock)
        {
            if (_userConnections.TryGetValue(userId, out var connections))
            {
                return connections.ToList();
            }
            return Enumerable.Empty<string>();
        }
    }

    public bool IsUserOnline(int userId)
    {
        lock (_lock)
        {
            return _userConnections.ContainsKey(userId) && _userConnections[userId].Count > 0;
        }
    }

    public int GetOnlineUserCount()
    {
        lock (_lock)
        {
            return _userConnections.Count;
        }
    }

    public IEnumerable<int> GetOnlineUserIds()
    {
        lock (_lock)
        {
            return _userConnections.Keys.ToList();
        }
    }
}
