using FreeSql;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Realtime.Events;
using Sb.Framework.Chat.Realtime.Options;
using Sb.Framework.Chat.Realtime.Storage;
using System.Text.Json;

namespace Sb.Framework.Chat.Realtime.Messages;

/// <summary>
/// 默认消息服务实现（FreeSql 落库 + 序列号分配 + 投递）。
/// </summary>
public class MessageService : IMessageService
{
    private readonly IFreeSql _fsql;
    private readonly IMessageDeliveryService _delivery;
    private readonly IRealtimeEventSink _eventSink;
    private readonly RealtimeOptions _options;
    private readonly ILogger _logger;

    public MessageService(
        IFreeSql fsql,
        IMessageDeliveryService delivery,
        IRealtimeEventSink eventSink,
        RealtimeOptions options,
        ILogger<MessageService> logger)
    {
        _fsql = fsql;
        _delivery = delivery;
        _eventSink = eventSink;
        _options = options;
        _logger = logger;
    }

    public async Task<RealtimeMessage?> SendAsync(RealtimeMessage message, CancellationToken ct = default)
    {
        // 1. 业务校验/改写钩子（敏感词、审计、自动回复入口）
        var processed = _eventSink.OnBeforeSend(message);
        if (processed == null) return null;
        message = processed;

        if (string.IsNullOrEmpty(message.Id))
            message.Id = Guid.NewGuid().ToString("N");
        message.Timestamp = DateTime.UtcNow;

        // 2. 分配 seq + 落库
        var seq = await AllocateSeqAsync(message.ConversationId);
        message.Seq = seq;

        var entity = new RtMessage
        {
            ConversationId = message.ConversationId,
            SenderId = message.SenderId,
            ContentType = message.ContentType,
            ContentJson = message.Content,
            MediaIds = message.MediaIds is { Count: > 0 } ? string.Join(",", message.MediaIds) : null,
            Seq = seq,
            Timestamp = message.Timestamp,
            Status = message.Status,
        };

        try
        {
            await _fsql.Insert(entity).ExecuteAffrowsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Realtime 消息落库失败: ConversationId={ConversationId}", message.ConversationId);
            return null;
        }

        // 3. 更新会话摘要
        await TouchConversationAsync(message.ConversationId, seq, message.ContentType, message.Content, ct);

        // 4. 投递到会话组
        await _delivery.DeliverAsync(message, ct);
        _eventSink.OnMessageDelivered(message);

        _logger.LogDebug("Realtime 消息已发送: Conv={ConversationId}, Seq={Seq}, Type={Type}",
            message.ConversationId, seq, message.ContentType);
        return message;
    }

    public Task<RealtimeMessage?> SendSystemAsync(long conversationId, string systemType, string? payload,
        string senderId = "system", CancellationToken ct = default) =>
        SendAsync(new RealtimeMessage
        {
            ConversationId = conversationId,
            SenderId = senderId,
            ContentType = "custom",
            Content = payload,
        }, ct);

    public async Task<IReadOnlyList<RealtimeMessage>> GetHistoryAsync(long conversationId, long beforeSeq = 0,
        int count = 50, CancellationToken ct = default)
    {
        if (count <= 0) count = 50;
        if (count > _options.MaxHistoryBatch) count = _options.MaxHistoryBatch;

        var list = await _fsql.Select<RtMessage>()
            .Where(m => m.ConversationId == conversationId)
            .WhereIf(beforeSeq > 0, m => m.Seq < beforeSeq)
            .OrderByDescending(m => m.Seq)
            .Take(count)
            .ToListAsync(ct);

        // 升序返回（按时间正序）
        list.Reverse();
        return list.Select(ToDto).ToList();
    }

    [Obsolete("请使用带 userId 参数的 WithdrawAsync 重载。")]
    public async Task<bool> WithdrawAsync(long conversationId, long seq, CancellationToken ct = default)
    {
        var result = await WithdrawAsync(conversationId, seq, "", ct);
        return result != null;
    }

    /// <inheritdoc />
    public async Task<bool> MarkDeliveredAsync(long conversationId, string userId, long seq, CancellationToken ct = default)
    {
        var entity = await _fsql.Select<RtMessage>()
            .Where(m => m.ConversationId == conversationId && m.Seq == seq)
            .FirstAsync(ct);
        if (entity == null) return false;
        if (entity.Status >= MessageStatus.Delivered) return true;

        entity.Status = MessageStatus.Delivered;
        entity.DeliveredAt = DateTime.UtcNow;

        await _fsql.Update<RtMessage>().SetSource(entity)
            .UpdateColumns(m => new { m.Status, m.DeliveredAt })
            .ExecuteAffrowsAsync(ct);

        return true;
    }

    /// <inheritdoc />
    public async Task<RealtimeMessage?> WithdrawAsync(long conversationId, long seq, string userId, CancellationToken ct = default)
    {
        var entity = await _fsql.Select<RtMessage>()
            .Where(m => m.ConversationId == conversationId && m.Seq == seq)
            .FirstAsync(ct);
        if (entity == null || entity.IsWithdrawn) return null;

        // 权限校验：发送者本人才能撤回（userId 为空时跳过校验，兼容旧调用）
        if (!string.IsNullOrEmpty(userId) && entity.SenderId != userId)
        {
            _logger.LogWarning("WithdrawAsync: 无权限撤回消息 Conv={ConvId}, Seq={Seq}, UserId={UserId}",
                conversationId, seq, userId);
            return null;
        }

        entity.IsWithdrawn = true;
        entity.Status = MessageStatus.Withdrawn;

        await _fsql.Update<RtMessage>().SetSource(entity)
            .UpdateColumns(m => new { m.IsWithdrawn, m.Status })
            .ExecuteAffrowsAsync(ct);

        var dto = ToDto(entity);
        await _delivery.DeliverWithdrawnAsync(dto, ct);
        return dto;
    }

    /// <inheritdoc />
    public async Task<RealtimeMessage?> EditAsync(long conversationId, long seq, string userId, string newContent,
        CancellationToken ct = default)
    {
        var entity = await _fsql.Select<RtMessage>()
            .Where(m => m.ConversationId == conversationId && m.Seq == seq)
            .FirstAsync(ct);
        if (entity == null) return null;

        // 权限校验：发送者本人才能编辑
        if (entity.SenderId != userId)
        {
            _logger.LogWarning("EditAsync: 无权限编辑消息 Conv={ConvId}, Seq={Seq}, UserId={UserId}",
                conversationId, seq, userId);
            return null;
        }

        // 已撤回或已删除的消息不能编辑
        if (entity.Status == MessageStatus.Withdrawn || entity.Status == MessageStatus.Deleted)
            return null;

        // 保存编辑前内容（审计用）
        entity.PreviousContent = entity.ContentJson;
        entity.ContentJson = newContent;
        entity.Status = MessageStatus.Edited;
        entity.EditedAt = DateTime.UtcNow;

        await _fsql.Update<RtMessage>().SetSource(entity)
            .UpdateColumns(m => new { m.ContentJson, m.PreviousContent, m.Status, m.EditedAt })
            .ExecuteAffrowsAsync(ct);

        var dto = ToDto(entity);
        await _delivery.DeliverEditedAsync(dto, ct);
        return dto;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long conversationId, long seq, string userId, CancellationToken ct = default)
    {
        var entity = await _fsql.Select<RtMessage>()
            .Where(m => m.ConversationId == conversationId && m.Seq == seq)
            .FirstAsync(ct);
        if (entity == null) return false;

        // 权限校验：发送者本人才能删除
        if (entity.SenderId != userId)
        {
            _logger.LogWarning("DeleteAsync: 无权限删除消息 Conv={ConvId}, Seq={Seq}, UserId={UserId}",
                conversationId, seq, userId);
            return false;
        }

        // 已删除的消息无需重复处理
        if (entity.Status == MessageStatus.Deleted) return true;

        entity.Status = MessageStatus.Deleted;

        await _fsql.Update<RtMessage>().SetSource(entity)
            .UpdateColumns(m => m.Status)
            .ExecuteAffrowsAsync(ct);

        await _delivery.DeliverDeletedAsync(conversationId, seq, ct);
        return true;
    }

    private async Task<long> AllocateSeqAsync(long conversationId)
    {
        // 并发安全的 seq：基于会话当前最大 seq + 1（配合数据库行锁/唯一约束在业务侧兜底）
        var max = await _fsql.Select<RtMessage>()
            .Where(m => m.ConversationId == conversationId)
            .MaxAsync(m => m.Seq);
        return max + 1;
    }

    private async Task TouchConversationAsync(long conversationId, long seq, string contentType, string? content,
        CancellationToken ct)
    {
        await _fsql.Update<Storage.RtConversation>()
            .Where(c => c.Id == conversationId)
            .Set(c => c.LastMsgSeq, seq)
            .Set(c => c.LastMsgPreview, BuildPreview(contentType, content))
            .ExecuteAffrowsAsync(ct);
    }

    private static string? BuildPreview(string contentType, string? content)
    {
        if (SystemMessageType.IsSystem(contentType)) return null;
        return contentType switch
        {
            "text" => content,
            "image" => "[图片]",
            "file" => "[文件]",
            "voice" => "[语音]",
            _ => "[消息]",
        };
    }

    private static RealtimeMessage ToDto(RtMessage m) => new()
    {
        Id = m.Id.ToString(),
        ConversationId = m.ConversationId,
        SenderId = m.SenderId,
        ContentType = m.ContentType,
        Content = m.ContentJson,
        MediaIds = string.IsNullOrEmpty(m.MediaIds)
            ? null : m.MediaIds.Split(',').Where(x => long.TryParse(x, out _)).Select(long.Parse).ToList(),
        Seq = m.Seq,
        Timestamp = m.Timestamp,
        Status = m.Status,
        DeliveredAt = m.DeliveredAt,
        ReadAt = m.ReadAt,
        EditedAt = m.EditedAt,
        PreviousContent = m.PreviousContent,
        IsWithdrawn = m.IsWithdrawn,
    };
}
