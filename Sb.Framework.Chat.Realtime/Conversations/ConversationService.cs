using FreeSql;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Realtime.Events;
using Sb.Framework.Chat.Realtime.Storage;

namespace Sb.Framework.Chat.Realtime.Conversations;

/// <summary>
/// 默认会话服务实现（FreeSql）。
/// </summary>
public class ConversationService : IConversationService
{
    private readonly IFreeSql _fsql;
    private readonly IRealtimeEventSink _eventSink;
    private readonly ILogger _logger;

    public ConversationService(IFreeSql fsql, IRealtimeEventSink eventSink, ILogger<ConversationService> logger)
    {
        _fsql = fsql;
        _eventSink = eventSink;
        _logger = logger;
    }

    public async Task<RealtimeConversation> CreateAsync(string ownerId, string type, string? title,
        IReadOnlyList<string>? memberIds, string creatorId, CancellationToken ct = default)
    {
        var conv = new RtConversation
        {
            OwnerId = ownerId,
            Type = type,
            Title = title,
        };
        // PostgreSQL 用 INSERT ... RETURNING "id" 直接获取自增 Id;
        // MySQL 用 ExecuteInsertedAsync 内部的 LAST_INSERT_ID() 机制。
        var inserted = await _fsql.Insert(conv).ExecuteInsertedAsync(ct);
        if (inserted != null && inserted.Count > 0)
        {
            conv.Id = inserted[0].Id;
        }

        // 创建者作为 owner 成员
        await _fsql.Insert(new RtConversationMember
        {
            ConversationId = conv.Id,
            UserId = creatorId,
            Role = "owner",
        }).ExecuteAffrowsAsync(ct);

        if (memberIds != null)
        {
            foreach (var uid in memberIds)
            {
                if (uid == creatorId) continue;
                await AddMemberAsync(conv.Id, uid, ct: ct);
            }
        }

        _logger.LogDebug("Realtime 会话已创建: Id={Id}, Type={Type}", conv.Id, type);
        return new RealtimeConversation
        {
            Id = conv.Id,
            OwnerId = ownerId,
            Type = type,
            Title = title,
        };
    }

    public async Task<RealtimeConversation?> GetAsync(long conversationId, string userId, CancellationToken ct = default)
    {
        var conv = await _fsql.Select<RtConversation>().Where(c => c.Id == conversationId).FirstAsync(ct);
        if (conv == null) return null;

        var lastRead = await _fsql.Select<RtReadState>()
            .Where(r => r.ConversationId == conversationId && r.UserId == userId)
            .FirstAsync(ct);

        long unread = Math.Max(0, conv.LastMsgSeq - (lastRead?.LastReadSeq ?? 0));

        return new RealtimeConversation
        {
            Id = conv.Id,
            OwnerId = conv.OwnerId,
            Type = conv.Type,
            Title = conv.Title,
            LastMsgSeq = conv.LastMsgSeq,
            LastMsgPreview = conv.LastMsgPreview,
            UnreadCount = unread,
        };
    }

    public async Task<IReadOnlyList<string>> GetMemberIdsAsync(long conversationId, CancellationToken ct = default) =>
        await _fsql.Select<RtConversationMember>()
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync(m => m.UserId, ct);

    public async Task AddMemberAsync(long conversationId, string userId, string role = "member",
        CancellationToken ct = default)
    {
        var exists = await _fsql.Select<RtConversationMember>()
            .Where(m => m.ConversationId == conversationId && m.UserId == userId)
            .AnyAsync(ct);
        if (exists) return;

        await _fsql.Insert(new RtConversationMember
        {
            ConversationId = conversationId,
            UserId = userId,
            Role = role,
        }).ExecuteAffrowsAsync(ct);

        _eventSink.OnMembershipChanged(conversationId, userId, true);
    }

    public async Task RemoveMemberAsync(long conversationId, string userId, CancellationToken ct = default)
    {
        await _fsql.Delete<RtConversationMember>()
            .Where(m => m.ConversationId == conversationId && m.UserId == userId)
            .ExecuteAffrowsAsync(ct);

        _eventSink.OnMembershipChanged(conversationId, userId, false);
    }

    public async Task<IReadOnlyList<RealtimeConversation>> GetConversationsAsync(string userId,
        CancellationToken ct = default)
    {
        // 该用户所属的所有会话（成员表） + 已读游标（已读表，可能无记录 → 左联）
        var rows = await _fsql.Select<RtConversation, RtConversationMember, RtReadState>()
            .InnerJoin((c, m, r) => c.Id == m.ConversationId)
            .LeftJoin((c, m, r) => r.ConversationId == c.Id && r.UserId == userId)
            .Where((c, m, r) => m.UserId == userId)
            .OrderByDescending((c, m, r) => c.UpdatedTime)
            .ToListAsync((c, m, r) => new
            {
                Conv = c,
                LastReadSeq = r.LastReadSeq,
            }, ct);

        return rows.Select(r => new RealtimeConversation
        {
            Id = r.Conv.Id,
            OwnerId = r.Conv.OwnerId,
            Type = r.Conv.Type,
            Title = r.Conv.Title,
            LastMsgSeq = r.Conv.LastMsgSeq,
            LastMsgPreview = r.Conv.LastMsgPreview,
            UnreadCount = Math.Max(0, r.Conv.LastMsgSeq - r.LastReadSeq),
        }).ToList();
    }
}
