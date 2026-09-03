namespace Sb.Framework.Chat.Realtime.Auth;

/// <summary>
/// 连接鉴权/准入校验。业务系统实现，在 SignalR 握手时校验当前连接用户是否能接入实时通讯
/// （可结合 sb-framework 的 JWT/WebSocket query token 鉴权后，再在此做业务级准入）。
/// </summary>
public interface IRealtimeAuthProvider
{
    /// <summary>
    /// 校验连接用户是否允许接入。
    /// </summary>
    /// <param name="context">连接上下文（当前用户标识、业务信息）。</param>
    /// <returns>true 放行；false 拒绝。</returns>
    bool ValidateConnection(RealtimeConnectionContext context);
}

/// <summary>
/// 连接上下文：当前接入用户的基本信息（Hub 握手时从当前 Principal 提取）。
/// </summary>
public sealed class RealtimeConnectionContext
{
    /// <summary>当前用户 id（SignalR 连接 user 标识）。</summary>
    public string UserId { get; set; } = "";

    /// <summary>连接 id。</summary>
    public string ConnectionId { get; set; } = "";

    /// <summary>客户端主动携带的业务参数（如 source/platformKey 等）。</summary>
    public IReadOnlyDictionary<string, string?>? Query { get; set; }
}

/// <summary>
/// 默认实现：userId 非空即放行（业务系统未实现时兜底）。
/// </summary>
public sealed class DefaultRealtimeAuthProvider : IRealtimeAuthProvider
{
    public bool ValidateConnection(RealtimeConnectionContext context) =>
        !string.IsNullOrEmpty(context.UserId);
}
