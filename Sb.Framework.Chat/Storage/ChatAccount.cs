using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Storage;

/// <summary>
/// 账号登记（Bot / 个人号）。多租户判别字段 OwnerId 由业务层填。
/// </summary>
[Table(Name = "chat_account")]
[Index("ix_chat_account_owner", "OwnerId")]
public class ChatAccount : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>账号 id（业务级唯一，对应 AccountBag.AccountId）。</summary>
    [Column(Name = "account_id", StringLength = 64, IsNullable = false)]
    public string AccountId { get; set; } = "";

    /// <summary>多租户判别：商家平台=商家id；内部系统=部门id。SDK 不解释，业务层隔离用。</summary>
    [Column(Name = "owner_id", StringLength = 64, IsNullable = true)]
    public string? OwnerId { get; set; }

    /// <summary>账号类型：Bot / Personal。</summary>
    [Column(Name = "account_type", StringLength = 16, IsNullable = false)]
    public string AccountType { get; set; } = "";

    /// <summary>账号显示名。</summary>
    [Column(Name = "name", StringLength = 100, IsNullable = true)]
    public string? Name { get; set; }

    /// <summary>会话状态（Created/AwaitingCode/Awaiting2FA/Ready/Disconnected/Invalidated）。</summary>
    [Column(Name = "state", StringLength = 24, IsNullable = false)]
    public string State { get; set; } = "";

    /// <summary>能力标记（逗号分隔）。</summary>
    [Column(Name = "capabilities", StringLength = 500, IsNullable = true)]
    public string? Capabilities { get; set; }

    /// <summary>最后活跃时间（UTC）。</summary>
    [Column(Name = "last_active", DbType = "timestamp", IsNullable = true)]
    public DateTime? LastActive { get; set; }

    [Column(Name = "created_time", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedTime { get; set; }

    [Column(Name = "updated_time", DbType = "timestamp", InsertValueSql = "now()")]
    public DateTime UpdatedTime { get; set; }
}
