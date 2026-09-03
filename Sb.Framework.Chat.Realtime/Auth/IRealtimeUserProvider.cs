namespace Sb.Framework.Chat.Realtime.Auth;

/// <summary>
/// 实时用户资料（由业务系统通过 IRealtimeUserProvider 提供）。
/// </summary>
public sealed class RealtimeUserInfo
{
    /// <summary>业务侧用户 id（与 Hub 连接的用户 id 对应）。</summary>
    public string UserId { get; set; } = "";

    /// <summary>显示昵称。</summary>
    public string? Nickname { get; set; }

    /// <summary>头像 URL。</summary>
    public string? Avatar { get; set; }

    /// <summary>业务侧附加属性（如客服/访客角色、归属 OwnerId）。</summary>
    public Dictionary<string, object?>? Extras { get; set; }
}

/// <summary>
/// 实时用户资料提供器。业务系统实现，把自身用户映射为实时用户（如客服项目映射 Member/ChatUser）。
/// </summary>
public interface IRealtimeUserProvider
{
    /// <summary>根据用户 id 解析用户资料；返回 null 表示用户不存在。</summary>
    RealtimeUserInfo? Resolve(string userId);
}

/// <summary>
/// 默认实现：仅透传 userId，无附加资料（业务系统未实现时兜底）。
/// </summary>
public sealed class DefaultRealtimeUserProvider : IRealtimeUserProvider
{
    public RealtimeUserInfo? Resolve(string userId) =>
        string.IsNullOrEmpty(userId) ? null : new RealtimeUserInfo { UserId = userId };
}
