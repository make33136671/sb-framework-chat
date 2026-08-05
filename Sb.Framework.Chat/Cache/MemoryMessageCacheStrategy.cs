namespace Sb.Framework.Chat.Cache;

/// <summary>
/// 进程内存缓存策略（默认实现）。≤100 业务员规模下足够；
/// 多实例部署时替换为桥接框架 Redis ICacheService 的实现即可。
/// </summary>
public sealed class MemoryMessageCacheStrategy : IMessageCacheStrategy
{
    private static readonly Dictionary<string, (object Value, DateTime Expiry)> _store = new();
    private static readonly object _lock = new();

    public T? Get<T>(string key)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue(key, out var entry)) return default;
            if (entry.Expiry < SB.Framework.Core.Util.AppTime.UtcNow)
            {
                _store.Remove(key);
                return default;
            }
            return (T)entry.Value;
        }
    }

    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        var expiry = SB.Framework.Core.Util.AppTime.UtcNow + (expiration ?? TimeSpan.FromMinutes(30));
        lock (_lock) _store[key] = (value!, expiry);
    }

    public void Remove(string key)
    {
        lock (_lock) _store.Remove(key);
    }
}
