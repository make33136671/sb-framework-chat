using Microsoft.AspNetCore.SignalR;
using Sb.Framework.Chat.Realtime.Hub;
using Sb.Framework.Chat.Realtime.Presence;

namespace Sb.Framework.Chat.Realtime.Services;

/// <summary>
/// 用户级广播服务：向指定用户的所有在线连接推送消息。
/// </summary>
public interface IConnectionBroadcastService
{
    /// <summary>
    /// 向指定用户的所有在线连接广播消息。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="methodName">客户端方法名</param>
    /// <param name="payload">消息负载</param>
    /// <param name="ct">取消令牌</param>
    Task BroadcastToUserAsync(string userId, string methodName, object? payload, CancellationToken ct = default);
}

/// <summary>
/// 用户级广播服务默认实现：通过 IPresenceService 获取用户连接列表，使用 SignalR HubContext 发送。
/// </summary>
public class ConnectionBroadcastService : IConnectionBroadcastService
{
    private readonly IHubContext<RealtimeHub> _hub;
    private readonly IPresenceService _presence;

    public ConnectionBroadcastService(IHubContext<RealtimeHub> hub, IPresenceService presence)
    {
        _hub = hub;
        _presence = presence;
    }

    public Task BroadcastToUserAsync(string userId, string methodName, object? payload, CancellationToken ct = default)
    {
        var connectionIds = _presence.GetConnectionIds(userId);
        if (connectionIds.Count == 0)
            return Task.CompletedTask;

        return _hub.Clients.Clients(connectionIds).SendAsync(methodName, payload, ct);
    }
}
