namespace Sb.Framework.Chat.Realtime.Messages;

/// <summary>
/// 实时消息传输对象（客户端与服务端共用，序列化后走 SignalR）。
/// </summary>
public sealed class RealtimeMessage
{
    /// <summary>消息唯一 id（业务侧可自行生成，空则由服务端生成）。</summary>
    public string Id { get; set; } = "";

    /// <summary>会话 id（对应 chat_rt_conversation.id）。</summary>
    public long ConversationId { get; set; }

    /// <summary>发送者用户 id。</summary>
    public string SenderId { get; set; } = "";

    /// <summary>内容类型：text / image / file / voice / sys.*（系统消息）。</summary>
    public string ContentType { get; set; } = "text";

    /// <summary>内容（text 直接为文本；其它类型为 JSON）。</summary>
    public string? Content { get; set; }

    /// <summary>关联媒体 id。</summary>
    public IReadOnlyList<long>? MediaIds { get; set; }

    /// <summary>会话内序列号（服务端分配）。</summary>
    public long Seq { get; set; }

    /// <summary>发送时间（UTC）。</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>消息状态。</summary>
    public MessageStatus Status { get; set; } = MessageStatus.Sent;

    /// <summary>送达时间（UTC）。</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>已读时间（UTC）。</summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>编辑时间（UTC）。</summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>编辑前内容（审计用）。</summary>
    public string? PreviousContent { get; set; }

    /// <summary>是否撤回（快速查询索引，与 Status=Withdrawn 同步）。</summary>
    public bool IsWithdrawn { get; set; }
}
