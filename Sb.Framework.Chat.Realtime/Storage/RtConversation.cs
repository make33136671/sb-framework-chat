using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Realtime.Storage;

/// <summary>
/// 会话主体（1:1 单聊 / group 群聊（广播） / system 系统会话）。
/// </summary>
[Table(Name = "chat_rt_conversation")]
[Index("ix_rt_conversation_owner", "OwnerId,Type")]
public class RtConversation : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>归属方标识（业务侧：租户/站长 OwnerId）。</summary>
    [Column(Name = "owner_id", StringLength = 64, IsNullable = false)]
    public string OwnerId { get; set; } = "";

    /// <summary>会话类型：direct / group / system。</summary>
    [Column(Name = "type", StringLength = 16, IsNullable = false)]
    public string Type { get; set; } = "direct";

    /// <summary>会话标题（群聊/系统消息场景）。</summary>
    [Column(Name = "title", StringLength = 200, IsNullable = true)]
    public string? Title { get; set; }

    /// <summary>扩展 JSON（业务侧附加信息）。</summary>
    [Column(Name = "ext_json", StringLength = -2, IsNullable = true)]
    public string? ExtJson { get; set; }

    /// <summary>最后一条消息序列号（用于列表摘要 / 未读聚合）。</summary>
    [Column(Name = "last_msg_seq", IsNullable = false, InsertValueSql = "0")]
    public long LastMsgSeq { get; set; }

    /// <summary>最后一条消息内容摘要。</summary>
    [Column(Name = "last_msg_preview", StringLength = 500, IsNullable = true)]
    public string? LastMsgPreview { get; set; }

    [Column(Name = "created_time", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedTime { get; set; }

    [Column(Name = "updated_time", DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime UpdatedTime { get; set; }
}
