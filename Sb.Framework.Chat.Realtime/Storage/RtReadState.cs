using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Realtime.Storage;

/// <summary>
/// 已读游标：记录某用户在某会话已读到的最大 Seq。
/// </summary>
[Table(Name = "chat_rt_read_state")]
[Index("ix_rt_read_state_conv_user", "ConversationId,UserId", IsUnique = true)]
public class RtReadState : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "conversation_id", IsNullable = false)]
    public long ConversationId { get; set; }

    [Column(Name = "user_id", StringLength = 64, IsNullable = false)]
    public string UserId { get; set; } = "";

    /// <summary>已读到的最大消息序列号。</summary>
    [Column(Name = "last_read_seq", IsNullable = false, InsertValueSql = "0")]
    public long LastReadSeq { get; set; }

    [Column(Name = "updated_time", DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime UpdatedTime { get; set; }
}
