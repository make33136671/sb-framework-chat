using FreeSql.DataAnnotations;
using SB.Framework.Models;

namespace Sb.Framework.Chat.Realtime.Storage;

/// <summary>
/// 媒体元数据（上传文件/图片/语音等）。
/// </summary>
[Table(Name = "chat_rt_media")]
public class RtMedia : IEntity
{
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>归属方标识。</summary>
    [Column(Name = "owner_id", StringLength = 64, IsNullable = false)]
    public string OwnerId { get; set; } = "";

    [Column(Name = "media_type", StringLength = 16, IsNullable = false)]
    public string MediaType { get; set; } = "file";

    [Column(Name = "url", StringLength = 500, IsNullable = false)]
    public string Url { get; set; } = "";

    [Column(Name = "size", IsNullable = false, InsertValueSql = "0")]
    public long Size { get; set; }

    [Column(Name = "mime", StringLength = 100, IsNullable = true)]
    public string? Mime { get; set; }

    [Column(Name = "created_time", DbType = "timestamp", InsertValueSql = "now()", CanUpdate = false)]
    public DateTime CreatedTime { get; set; }
}
