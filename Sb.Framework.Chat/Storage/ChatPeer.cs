using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Storage;

/// <summary>
/// 会话方（客户/群）。每个账号下的每个 PeerId 一条。
/// </summary>
[Table(Name = "chat_peer")]
[Index("ix_chat_peer_account_peer", "AccountId,PeerId", true)]
public class ChatPeer : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "account_id", StringLength = 64, IsNullable = false)]
    public string AccountId { get; set; } = "";

    /// <summary>会话方 id（客户/群）。</summary>
    [Column(Name = "peer_id", StringLength = 64, IsNullable = false)]
    public string PeerId { get; set; } = "";

    /// <summary>会话方类型：user / chat / channel。</summary>
    [Column(Name = "peer_type", StringLength = 16, IsNullable = true)]
    public string? PeerType { get; set; }

    [Column(Name = "title", StringLength = 200, IsNullable = true)]
    public string? Title { get; set; }

    [Column(Name = "avatar", StringLength = 500, IsNullable = true)]
    public string? Avatar { get; set; }

    /// <summary>最后消息时间（UTC）。</summary>
    [Column(Name = "last_message_time", DbType = "timestamp", IsNullable = true)]
    public DateTime? LastMessageTime { get; set; }

    [Column(Name = "created_time", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedTime { get; set; }
}
