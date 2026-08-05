using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Storage;

/// <summary>
/// 更新游标（断点续传）。每个账号一条，记录历史补拉 / Update 流的进度。
/// </summary>
[Table(Name = "chat_sync_state")]
[Index("ix_chat_sync_state_account", "AccountId", true)]
public class ChatSyncState : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "account_id", StringLength = 64, IsNullable = false)]
    public string AccountId { get; set; } = "";

    /// <summary>更新游标（通道特定语义：Bot offset / MTProto pts+date+qts）。</summary>
    [Column(Name = "update_cursor", StringLength = 200, IsNullable = true)]
    public string? UpdateCursor { get; set; }

    [Column(Name = "last_sync_time", DbType = "timestamp", IsNullable = true)]
    public DateTime? LastSyncTime { get; set; }
}
