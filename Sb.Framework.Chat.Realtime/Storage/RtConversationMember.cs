using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Realtime.Storage;

/// <summary>
/// 会话成员（支撑广播模式：多个用户监听同一会话；未读游标定位）。
/// </summary>
[Table(Name = "chat_rt_conversation_member")]
[Index("ix_rt_conv_member_conv_user", "ConversationId,UserId", IsUnique = true)]
public class RtConversationMember : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "conversation_id", IsNullable = false)]
    public long ConversationId { get; set; }

    [Column(Name = "user_id", StringLength = 64, IsNullable = false)]
    public string UserId { get; set; } = "";

    /// <summary>成员角色：owner / member / admin。</summary>
    [Column(Name = "role", StringLength = 16, IsNullable = false, InsertValueSql = "'member'")]
    public string Role { get; set; } = "member";

    [Column(Name = "joined_time", DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime JoinedTime { get; set; }
}
