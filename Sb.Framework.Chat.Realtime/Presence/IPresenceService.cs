namespace Sb.Framework.Chat.Realtime.Presence;

/// <summary>在线状态变化事件。</summary>
public sealed class PresenceChange
{
    public string UserId { get; set; } = "";
    public bool Online { get; set; }
}

/// <summary>
/// 在线状态服务（内存实时在线表 + 心跳超时判定）。多实例部署时可替换为 Redis 实现。
/// </summary>
public interface IPresenceService
{
    /// <summary>连接接入时登记在线。</summary>
    void MarkOnline(string userId, string connectionId);

    /// <summary>连接断开时注销。</summary>
    void MarkOffline(string userId, string connectionId);

    /// <summary>客户端心跳：更新最后活跃时间。</summary>
    void UpdateHeartbeat(string userId);

    /// <summary>查询是否在线（心跳在阈值内视为在线）。</summary>
    bool IsOnline(string userId);

    /// <summary>批量查询在线状态。</summary>
    IReadOnlyDictionary<string, bool> IsOnline(IEnumerable<string> userIds);

    /// <summary>获取当前在线用户总数（心跳未超时的用户）。</summary>
    int GetOnlineCount();

    /// <summary>获取指定用户的所有连接 ID 列表（只读快照）。</summary>
    IReadOnlyList<string> GetConnectionIds(string userId);

    /// <summary>清理心跳超时的过期条目，返回被清理的 userId 列表。</summary>
    List<string> CleanupStale();

    /// <summary>在线状态变化事件（业务系统可订阅推送/审计）。</summary>
    event Action<PresenceChange>? PresenceChanged;
}
