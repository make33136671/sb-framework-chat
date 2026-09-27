namespace Sb.Framework.Chat.Adapter;

/// <summary>
/// Peer 资料详情（用户/群/频道）：业务层信息卡展示用。
/// </summary>
public sealed class PeerDetail
{
    /// <summary>统一 peer id。</summary>
    public required string PeerId { get; init; }

    /// <summary>User / Chat / Channel。</summary>
    public required string PeerType { get; init; }

    /// <summary>名称（用户=姓名，群/频道=标题）。</summary>
    public string? Name { get; init; }

    /// <summary>@用户名（无则 null）。</summary>
    public string? Username { get; init; }

    /// <summary>手机号（仅 mutual contact 可见，无则 null）。</summary>
    public string? Phone { get; init; }

    /// <summary>头像相对 URL（本地已下载），无则 null。</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>是否在线（null = 未知/非用户）。</summary>
    public bool? IsOnline { get; init; }

    /// <summary>最后上线时间的可读文本（如「3 小时前上线」；在线时为 null）。</summary>
    public string? LastSeenText { get; init; }

    /// <summary>群/频道成员数（用户为 null）。</summary>
    public int? MembersCount { get; init; }

    /// <summary>简介/签名（about，无则 null）。</summary>
    public string? Bio { get; init; }
}

/// <summary>群成员摘要。</summary>
public sealed class PeerMember
{
    /// <summary>用户数字 id（字符串）。</summary>
    public required string UserId { get; init; }

    /// <summary>显示名。</summary>
    public string? Name { get; init; }

    /// <summary>@用户名（无则 null）。</summary>
    public string? Username { get; init; }

    /// <summary>是否在线。</summary>
    public bool IsOnline { get; init; }

    /// <summary>最后上线时间的可读文本。</summary>
    public string? LastSeenText { get; init; }

    /// <summary>角色：owner / admin / member。</summary>
    public string Role { get; init; } = "member";
}

/// <summary>用户在线状态。</summary>
public sealed class PeerPresence
{
    /// <summary>统一 peer id。</summary>
    public required string PeerId { get; init; }

    /// <summary>是否在线（null = 状态未知/查不到）。</summary>
    public bool? IsOnline { get; init; }

    /// <summary>最后上线时间（UTC，离线且有精确时间时才有）。</summary>
    public DateTime? LastSeenUtc { get; init; }

    /// <summary>最后上线时间的可读文本（最近上线/一周内 等，无精确时间时用）。</summary>
    public string? LastSeenText { get; init; }
}
