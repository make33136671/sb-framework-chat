using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Storage;

/// <summary>
/// 会话凭证（个人号 MTProto session 等）。**加密存储**，泄露即账号失守。
/// </summary>
[Table(Name = "chat_session")]
[Index("ix_chat_session_account", "AccountId")]
public class ChatSession : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "account_id", StringLength = 64, IsNullable = false)]
    public string AccountId { get; set; } = "";

    /// <summary>凭证类型：mtproto_session / bot_token 等。</summary>
    [Column(Name = "provider", StringLength = 32, IsNullable = false)]
    public string Provider { get; set; } = "";

    /// <summary>加密后的会话数据（明文不入库）。</summary>
    [Column(Name = "encrypted_session", StringLength = -2, IsNullable = true)]
    public string? EncryptedSession { get; set; }

    /// <summary>认证状态机当前态。</summary>
    [Column(Name = "auth_state", StringLength = 24, IsNullable = false)]
    public string AuthState { get; set; } = "";

    /// <summary>手机号（个人号登录用，可空）。</summary>
    [Column(Name = "phone", StringLength = 32, IsNullable = true)]
    public string? Phone { get; set; }

    [Column(Name = "created_time", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedTime { get; set; }

    [Column(Name = "updated_time", DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime UpdatedTime { get; set; }
}
