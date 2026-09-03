using FreeSql;
using Sb.Framework.Chat.Realtime.Storage;

namespace Sb.Framework.Chat.Realtime.Media;

/// <summary>媒体元数据 DTO。</summary>
public sealed class RealtimeMedia
{
    public long Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string MediaType { get; set; } = "file";
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string? Mime { get; set; }
}

/// <summary>
/// 媒体服务：登记媒体元数据 / 查询。文件二进制存储由 IMediaStorageProvider 处理（可插拔 OSS 等）。
/// </summary>
public interface IMediaService
{
    /// <summary>登记媒体元数据并返回。</summary>
    Task<RealtimeMedia> RegisterAsync(string ownerId, string mediaType, string url, long size, string? mime,
        CancellationToken ct = default);

    /// <summary>查询媒体。</summary>
    Task<RealtimeMedia?> GetAsync(long id, CancellationToken ct = default);
}

/// <summary>
/// 媒体二进制存储提供器（业务系统可按需替换为 OSS/对象存储）。
/// </summary>
public interface IMediaStorageProvider
{
    /// <summary>生成访问 URL（默认直接落盘路径）。</summary>
    string BuildUrl(string relativePath);
}

public class MediaService : IMediaService
{
    private readonly IFreeSql _fsql;

    public MediaService(IFreeSql fsql)
    {
        _fsql = fsql;
    }

    public async Task<RealtimeMedia> RegisterAsync(string ownerId, string mediaType, string url, long size,
        string? mime, CancellationToken ct = default)
    {
        var entity = new RtMedia
        {
            OwnerId = ownerId,
            MediaType = mediaType,
            Url = url,
            Size = size,
            Mime = mime,
        };
        // PostgreSQL 用 ExecuteInsertedAsync（RETURNING）直接获取自增 Id;
        // MySQL 由 ExecuteInsertedAsync 内部的 LAST_INSERT_ID() 机制处理。
        var inserted = await _fsql.Insert(entity).ExecuteInsertedAsync(ct);
        if (inserted != null && inserted.Count > 0)
        {
            entity.Id = inserted[0].Id;
        }
        return new RealtimeMedia
        {
            Id = entity.Id,
            OwnerId = ownerId,
            MediaType = mediaType,
            Url = url,
            Size = size,
            Mime = mime,
        };
    }

    public async Task<RealtimeMedia?> GetAsync(long id, CancellationToken ct = default)
    {
        var entity = await _fsql.Select<RtMedia>().Where(m => m.Id == id).FirstAsync(ct);
        if (entity == null) return null;
        return new RealtimeMedia
        {
            Id = entity.Id,
            OwnerId = entity.OwnerId,
            MediaType = entity.MediaType,
            Url = entity.Url,
            Size = entity.Size,
            Mime = entity.Mime,
        };
    }
}
