using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Storage;

/// <summary>
/// 媒体文件（撤回前已落盘，防撤回保留）。
/// </summary>
[Table(Name = "chat_media")]
[Index("ix_chat_media_message", "AccountId,MessageId")]
public class ChatMedia : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    [Column(Name = "account_id", StringLength = 64, IsNullable = false)]
    public string AccountId { get; set; } = "";

    /// <summary>关联消息 id（chat_message.id）。</summary>
    [Column(Name = "message_id", IsNullable = false)]
    public int MessageId { get; set; }

    [Column(Name = "media_type", StringLength = 16, IsNullable = false)]
    public string MediaType { get; set; } = "";

    /// <summary>本地落盘相对路径。</summary>
    [Column(Name = "local_path", StringLength = 500, IsNullable = false)]
    public string LocalPath { get; set; } = "";

    /// <summary>通道内文件引用（Bot fileId / MTProto document 序列化，可能过期，需撤回前下载）。</summary>
    [Column(Name = "file_ref", StringLength = 500, IsNullable = true)]
    public string? FileRef { get; set; }

    [Column(Name = "mime_type", StringLength = 64, IsNullable = true)]
    public string? MimeType { get; set; }

    [Column(Name = "size", IsNullable = false)]
    public long Size { get; set; }

    /// <summary>MD5（去重 / 完整性校验）。</summary>
    [Column(Name = "md5", StringLength = 32, IsNullable = true)]
    public string? Md5 { get; set; }

    [Column(Name = "created_time", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedTime { get; set; }
}
