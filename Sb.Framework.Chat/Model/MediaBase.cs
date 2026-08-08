namespace Sb.Framework.Chat.Model;

/// <summary>
/// 媒体基类。媒体类型用注册表式扩展（后续加视频/语音/文件不改核心模型）。
/// </summary>
public abstract class MediaBase
{
    /// <summary>通道内媒体 id（Bot fileId / MTProto document+access_hash 序列化）。</summary>
    public string MediaId { get; set; } = "";

    public long Size { get; set; }

    public string? MimeType { get; set; }

    /// <summary>本地落盘后的相对 URL（防撤回：撤回前已下载，内容保留）。</summary>
    public string? LocalUrl { get; set; }

    /// <summary>原始文件名（Document 属性，照片无）。</summary>
    public string? FileName { get; set; }
}

/// <summary>图片媒体。</summary>
public sealed class ImageMedia : MediaBase { }

/// <summary>视频媒体。</summary>
public sealed class VideoMedia : MediaBase { }

/// <summary>语音媒体。</summary>
public sealed class VoiceMedia : MediaBase { }

/// <summary>文件媒体。</summary>
public sealed class FileMedia : MediaBase { }

/// <summary>贴纸媒体。</summary>
public sealed class StickerMedia : MediaBase { }

/// <summary>
/// 媒体下载引用（适配器据此下载字节，抽象 Bot fileId 与 MTProto InputFileLocation 的差异）。
/// </summary>
public sealed class MediaReference
{
    public string AccountId { get; set; } = "";

    /// <summary>通道特定的下载参数（Bot: fileId；MTProto: document 序列化）。</summary>
    public string MediaId { get; set; } = "";

    public string? Extra { get; set; }
}
