using Microsoft.AspNetCore.SignalR;
using Sb.Framework.Chat.Realtime.Hub;

namespace Sb.Framework.Chat.Realtime.Messages;

/// <summary>
/// 消息投递服务：把消息推送到会话组内的所有在线连接（SignalR group）。
/// 组名约定：conv:{conversationId}。
/// </summary>
public interface IMessageDeliveryService
{
    /// <summary>向会话组广播消息。</summary>
    Task DeliverAsync(RealtimeMessage message, CancellationToken ct = default);

    /// <summary>向会话组广播已读回执。</summary>
    Task DeliverReadReceiptAsync(ReadReceipt receipt, CancellationToken ct = default);

    /// <summary>向会话组广播撤回通知。</summary>
    Task DeliverWithdrawnAsync(RealtimeMessage message, CancellationToken ct = default);

    /// <summary>向会话组广播编辑通知。</summary>
    Task DeliverEditedAsync(RealtimeMessage message, CancellationToken ct = default);

    /// <summary>向会话组广播删除通知。</summary>
    Task DeliverDeletedAsync(long conversationId, long seq, CancellationToken ct = default);

    /// <summary>向会话组广播送达回执。</summary>
    Task DeliverDeliveryReceiptAsync(DeliveryReceipt receipt, CancellationToken ct = default);

    /// <summary>通知客服有新会话（推送到 agent:{userId} 个人组）。</summary>
    Task NotifyAgentNewConversationAsync(string agentUserId, long conversationId, string? visitorName = null);
}

/// <summary>已读回执负载。</summary>
public sealed class ReadReceipt
{
    public long ConversationId { get; set; }
    public string ReaderId { get; set; } = "";
    public long LastReadSeq { get; set; }
}

/// <summary>送达回执负载。</summary>
public sealed class DeliveryReceipt
{
    public long ConversationId { get; set; }
    public string UserId { get; set; } = "";
    public long Seq { get; set; }
}

public class MessageDeliveryService : IMessageDeliveryService
{
    private readonly IHubContext<RealtimeHub> _hub;

    public MessageDeliveryService(IHubContext<RealtimeHub> hub)
    {
        _hub = hub;
    }

    public Task DeliverAsync(RealtimeMessage message, CancellationToken ct = default) =>
        _hub.Clients.Group(GroupName(message.ConversationId))
            .SendAsync("OnMessage", message, ct);

    public Task DeliverReadReceiptAsync(ReadReceipt receipt, CancellationToken ct = default) =>
        _hub.Clients.Group(GroupName(receipt.ConversationId))
            .SendAsync("OnReadReceipt", receipt, ct);

    public Task DeliverWithdrawnAsync(RealtimeMessage message, CancellationToken ct = default) =>
        _hub.Clients.Group(GroupName(message.ConversationId))
            .SendAsync("OnMessageWithdrawn", message, ct);

    public Task DeliverEditedAsync(RealtimeMessage message, CancellationToken ct = default) =>
        _hub.Clients.Group(GroupName(message.ConversationId))
            .SendAsync("OnMessageEdited", message, ct);

    public Task DeliverDeletedAsync(long conversationId, long seq, CancellationToken ct = default) =>
        _hub.Clients.Group(GroupName(conversationId))
            .SendAsync("OnMessageDeleted", new { ConversationId = conversationId, Seq = seq }, ct);

    public Task DeliverDeliveryReceiptAsync(DeliveryReceipt receipt, CancellationToken ct = default) =>
        _hub.Clients.Group(GroupName(receipt.ConversationId))
            .SendAsync("OnDeliveryReceipt", receipt, ct);

    internal static string GroupName(long conversationId) => $"conv:{conversationId}";

    public Task NotifyAgentNewConversationAsync(string agentUserId, long conversationId, string? visitorName = null) =>
        _hub.Clients.Group($"agent:{agentUserId}")
            .SendAsync("OnNewConversation", new { ConversationId = conversationId, VisitorName = visitorName });
}
