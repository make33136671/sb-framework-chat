using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Storage;

/// <summary>
/// 消息全量记录（防撤回根基）。
/// <b>防撤回语义</b>：收到消息即入库（IsWithdrawn=false）；delete 更新到达时仅置 IsWithdrawn=true，**内容保留**。
/// 审查检索：SELECT * FROM chat_message WHERE account_id=? AND is_withdrawn=1。
/// </summary>
[Table(Name = "chat_message")]
[Index("ix_chat_message_account_peer_time", "AccountId,PeerId,Timestamp")]
[Index("ix_chat_message_withdrawn", "IsWithdrawn")]
[Index("ix_chat_message_account_chanid", "AccountId,ChannelMessageId")]
public class ChatMessage : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "account_id", StringLength = 64, IsNullable = false)]
    public string AccountId { get; set; } = "";

    [Column(Name = "peer_id", StringLength = 64, IsNullable = false)]
    public string PeerId { get; set; } = "";

    /// <summary>通道内消息 id（与 PeerId 组合定位）。</summary>
    [Column(Name = "channel_message_id", IsNullable = false)]
    public long ChannelMessageId { get; set; }

    /// <summary>方向：Incoming / Outgoing。出站消息也入库——防撤回关键（业务员自己发的违规消息）。</summary>
    [Column(Name = "direction", StringLength = 10, IsNullable = false)]
    public string Direction { get; set; } = "";

    /// <summary>撤回标记：默认 false，delete 更新到达时置 true，内容保留。</summary>
    [Column(Name = "is_withdrawn", IsNullable = false, InsertValueSql = "false")]
    public bool IsWithdrawn { get; set; }

    /// <summary>消息类型（Text/Image/Video/...）。</summary>
    [Column(Name = "message_type", StringLength = 16, IsNullable = false)]
    public string MessageType { get; set; } = "";

    /// <summary>消息内容 JSON（统一模型序列化）。</summary>
    [Column(Name = "content_json", StringLength = -2, IsNullable = true)]
    public string? ContentJson { get; set; }

    /// <summary>关联媒体 id（逗号分隔，指向 chat_media.id）。</summary>
    [Column(Name = "media_ids", StringLength = 200, IsNullable = true)]
    public string? MediaIds { get; set; }

    /// <summary>消息时间（UTC）。</summary>
    [Column(Name = "timestamp", DbType = "timestamp", IsNullable = false)]
    public DateTime Timestamp { get; set; }

    [Column(Name = "created_time", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedTime { get; set; }
}
