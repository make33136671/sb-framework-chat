namespace Sb.Framework.Chat.Realtime.Conversations;

/// <summary>会话 DTO（客户端/列表展示）。</summary>
public sealed class RealtimeConversation
{
    public long Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Type { get; set; } = "direct";
    public string? Title { get; set; }
    public long LastMsgSeq { get; set; }
    public string? LastMsgPreview { get; set; }
    public long UnreadCount { get; set; }
}

/// <summary>
/// 会话服务：会话创建、成员管理（广播模式）、未读聚合、会话列表。
/// </summary>
public interface IConversationService
{
    /// <summary>创建会话并加入创建者（及可选成员）。</summary>
    Task<RealtimeConversation> CreateAsync(string ownerId, string type, string? title,
        IReadOnlyList<string>? memberIds, string creatorId, CancellationToken ct = default);

    /// <summary>获取会话（含未读）。</summary>
    Task<RealtimeConversation?> GetAsync(long conversationId, string userId, CancellationToken ct = default);

    /// <summary>查询某用户在会话中的成员 id 列表（广播模式多个监听者）。</summary>
    Task<IReadOnlyList<string>> GetMemberIdsAsync(long conversationId, CancellationToken ct = default);

    /// <summary>加入成员。</summary>
    Task AddMemberAsync(long conversationId, string userId, string role = "member", CancellationToken ct = default);

    /// <summary>移除成员（广播模式客服下线时）。</summary>
    Task RemoveMemberAsync(long conversationId, string userId, CancellationToken ct = default);

    /// <summary>某用户的会话列表（含未读数）。</summary>
    Task<IReadOnlyList<RealtimeConversation>> GetConversationsAsync(string userId, CancellationToken ct = default);
}
