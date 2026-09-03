using FreeSql;
using Sb.Framework.Chat.Realtime.Storage;

namespace Sb.Framework.Chat.Realtime.ReadState;

/// <summary>
/// 已读状态服务：记录/查询用户在会话中的已读游标。
/// </summary>
public interface IReadStateService
{
    /// <summary>标记已读（upsert 最大游标）。</summary>
    Task MarkReadAsync(long conversationId, string userId, long lastReadSeq, CancellationToken ct = default);

    /// <summary>查询已读游标。</summary>
    Task<long> GetLastReadAsync(long conversationId, string userId, CancellationToken ct = default);
}

public class ReadStateService : IReadStateService
{
    private readonly IFreeSql _fsql;

    public ReadStateService(IFreeSql fsql)
    {
        _fsql = fsql;
    }

    public async Task MarkReadAsync(long conversationId, string userId, long lastReadSeq,
        CancellationToken ct = default)
    {
        var state = await _fsql.Select<RtReadState>()
            .Where(r => r.ConversationId == conversationId && r.UserId == userId)
            .FirstAsync(ct);

        if (state == null)
        {
            await _fsql.Insert(new RtReadState
            {
                ConversationId = conversationId,
                UserId = userId,
                LastReadSeq = lastReadSeq,
            }).ExecuteAffrowsAsync(ct);
        }
        else if (lastReadSeq > state.LastReadSeq)
        {
            state.LastReadSeq = lastReadSeq;
            await _fsql.Update<RtReadState>().SetSource(state)
                .UpdateColumns(r => new { r.LastReadSeq, r.UpdatedTime })
                .ExecuteAffrowsAsync(ct);
        }
    }

    public Task<long> GetLastReadAsync(long conversationId, string userId, CancellationToken ct = default) =>
        _fsql.Select<RtReadState>()
            .Where(r => r.ConversationId == conversationId && r.UserId == userId)
            .ToOneAsync(r => r.LastReadSeq, ct);
}
