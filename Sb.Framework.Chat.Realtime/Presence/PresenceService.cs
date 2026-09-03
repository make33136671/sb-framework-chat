using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Realtime.Options;
using System.Linq;

namespace Sb.Framework.Chat.Realtime.Presence;

/// <summary>
/// 默认内存在线状态实现：以"最后心跳时间"判定在线（心跳超时视为离线）。
/// </summary>
public class PresenceService : IPresenceService
{
    private sealed class PresenceEntry
    {
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
        public readonly HashSet<string> Connections = new();
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, PresenceEntry> _entries = new();
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;

    public event Action<PresenceChange>? PresenceChanged;

    public PresenceService(RealtimeOptions options, ILogger<PresenceService> logger)
    {
        _timeout = options.HeartbeatTimeout;
        _logger = logger;
    }

    public void MarkOnline(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(userId, out var entry))
            {
                entry = new PresenceEntry();
                _entries[userId] = entry;
                _logger.LogDebug("Realtime 用户上线: UserId={UserId}", userId);
            }
            entry.LastHeartbeat = DateTime.UtcNow;
            entry.Connections.Add(connectionId);
            PresenceChanged?.Invoke(new PresenceChange { UserId = userId, Online = true });
        }
    }

    public void MarkOffline(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(userId, out var entry))
            {
                _logger.LogDebug("Realtime 用户下线(未找到): UserId={UserId}, ConnectionId={ConnectionId}", userId, connectionId);
                return;
            }
            entry.Connections.Remove(connectionId);
            if (entry.Connections.Count == 0)
            {
                _entries.Remove(userId);
                _logger.LogDebug("Realtime 用户下线: UserId={UserId}", userId);
                PresenceChanged?.Invoke(new PresenceChange { UserId = userId, Online = false });
            }
            else
            {
                _logger.LogDebug("Realtime 用户连接断开(仍有其他连接): UserId={UserId}, Remaining={Remaining}",
                    userId, entry.Connections.Count);
            }
        }
    }

    public void UpdateHeartbeat(string userId)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(userId, out var entry))
            {
                entry.LastHeartbeat = DateTime.UtcNow;
            }
        }
    }

    public bool IsOnline(string userId)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(userId, out var entry)) return false;
            // 始终以心跳超时判定在线（应对 SignalR OnDisconnectedAsync 延迟触发的情况）
            return DateTime.UtcNow - entry.LastHeartbeat <= _timeout;
        }
    }

    public IReadOnlyDictionary<string, bool> IsOnline(IEnumerable<string> userIds)
    {
        var result = new Dictionary<string, bool>();
        foreach (var id in userIds)
            result[id] = IsOnline(id);
        return result;
    }

    public int GetOnlineCount()
    {
        lock (_lock)
        {
            int count = 0;
            var now = DateTime.UtcNow;
            foreach (var entry in _entries.Values)
            {
                if (now - entry.LastHeartbeat <= _timeout) count++;
            }
            return count;
        }
    }

    public IReadOnlyList<string> GetConnectionIds(string userId)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(userId, out var entry))
                return Array.Empty<string>();
            return entry.Connections.ToArray();
        }
    }

    public List<string> CleanupStale()
    {
        var removed = new List<string>();
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var staleKeys = _entries
                .Where(kvp => now - kvp.Value.LastHeartbeat > _timeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _entries.Remove(key);
                removed.Add(key);
                _logger.LogDebug("Presence 清理过期条目: UserId={UserId}", key);
                PresenceChanged?.Invoke(new PresenceChange { UserId = key, Online = false });
            }
        }
        return removed;
    }
}
