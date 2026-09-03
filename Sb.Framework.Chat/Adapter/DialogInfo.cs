namespace Sb.Framework.Chat.Adapter;

/// <summary>
/// 会话（dialog）摘要信息：业务层用于同步真实会话列表（标题/类型/最后消息时间）。
/// </summary>
public sealed class DialogInfo
{
    /// <summary>统一 peer id（与消息 PeerId 同格式：用户/群/频道数字 id）。</summary>
    public required string PeerId { get; init; }

    /// <summary>User / Chat / Channel。</summary>
    public required string PeerType { get; init; }

    /// <summary>会话标题（用户=姓名，群/频道=标题）。</summary>
    public string? Title { get; init; }

    /// <summary>最后一条消息时间（用于列表排序）。</summary>
    public DateTime? LastMessageTime { get; init; }

    /// <summary>未读数。</summary>
    public int UnreadCount { get; init; }

    /// <summary>头像相对 URL（本地已下载），无则 null。</summary>
    public string? AvatarUrl { get; init; }
}
