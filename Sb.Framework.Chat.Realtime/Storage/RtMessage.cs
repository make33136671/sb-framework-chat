using FreeSql.DataAnnotations;
using Sb.Framework.Chat.Realtime.Messages;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Realtime.Storage;

/// <summary>
/// 消息全量记录（防撤回根基）：收到即入库（IsWithdrawn=false）；撤回仅置 IsWithdrawn=true，内容保留。
/// 系统消息用 ContentType 前缀 sys.* 标记。
/// </summary>
[Table(Name = "chat_rt_message")]
[Index("ix_rt_message_conv_seq", "ConversationId,Seq")]
[Index("ix_rt_message_withdrawn", "IsWithdrawn")]
[Index("ix_rt_message_status", "Status")]
public class RtMessage : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "conversation_id", IsNullable = false)]
    public long ConversationId { get; set; }

    [Column(Name = "sender_id", StringLength = 64, IsNullable = false)]
    public string SenderId { get; set; } = "";

    /// <summary>消息内容类型：text / image / file / voice / sys.*（系统消息）。</summary>
    [Column(Name = "content_type", StringLength = 32, IsNullable = false)]
    public string ContentType { get; set; } = "text";

    /// <summary>消息内容 JSON（按 contentType 结构化）。</summary>
    [Column(Name = "content_json", StringLength = -2, IsNullable = true)]
    public string? ContentJson { get; set; }

    /// <summary>关联媒体 id（逗号分隔，指向 chat_rt_media.id）。</summary>
    [Column(Name = "media_ids", StringLength = 200, IsNullable = true)]
    public string? MediaIds { get; set; }

    /// <summary>会话内单调递增序列号（用于历史分页 / 已读游标）。</summary>
    [Column(Name = "seq", IsNullable = false)]
    public long Seq { get; set; }

    /// <summary>消息状态（枚举名存储：Sending/Sent/Delivered/Read/Failed/Withdrawn/Edited/Deleted）。</summary>
    [Column(Name = "status", IsNullable = false, InsertValueSql = "2")]
    public MessageStatus Status { get; set; } = MessageStatus.Sent;

    /// <summary>送达时间（UTC）。</summary>
    [Column(Name = "delivered_at", DbType = "timestamp", IsNullable = true)]
    public DateTime? DeliveredAt { get; set; }

    /// <summary>已读时间（UTC）。</summary>
    [Column(Name = "read_at", DbType = "timestamp", IsNullable = true)]
    public DateTime? ReadAt { get; set; }

    /// <summary>编辑时间（UTC）。</summary>
    [Column(Name = "edited_at", DbType = "timestamp", IsNullable = true)]
    public DateTime? EditedAt { get; set; }

    /// <summary>编辑前内容（审计用）。</summary>
    [Column(Name = "previous_content", StringLength = -2, IsNullable = true)]
    public string? PreviousContent { get; set; }

    /// <summary>撤回标记：默认 false，撤回时置 true，内容保留（快速查询索引，与 Status=Withdrawn 同步）。</summary>
    [Column(Name = "is_withdrawn", IsNullable = false, InsertValueSql = "false")]
    public bool IsWithdrawn { get; set; }

    [Column(Name = "timestamp", DbType = "timestamp", IsNullable = false)]
    public DateTime Timestamp { get; set; }

    [Column(Name = "created_time", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedTime { get; set; }
}
