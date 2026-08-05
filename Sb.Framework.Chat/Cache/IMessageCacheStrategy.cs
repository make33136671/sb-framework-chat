namespace Sb.Framework.Chat.Cache;

/// <summary>
/// 消息缓存策略（可插拔）。默认实现桥接框架 ICacheService（Null/Memory/Redis 自动跟随）。
/// 用于 ChatContext 缓存、Account 热数据等。
/// </summary>
public interface IMessageCacheStrategy
{
    T? Get<T>(string key);

    void Set<T>(string key, T value, TimeSpan? expiration = null);

    void Remove(string key);
}
