namespace Sb.Framework.Chat.Model;

/// <summary>
/// 统一响应消息基类（出站）。业务 MessageHandler 返回此类型，由通道适配器翻译为通道原生发送调用。
/// </summary>
public abstract class TelegramResponseMessageBase
{
    /// <summary>目标会话方 id。</summary>
    public string? PeerId { get; set; }

    /// <summary>被回复消息 id（可选，回复引用）。</summary>
    public string? ReplyToMessageId { get; set; }
}

/// <summary>文本回复。</summary>
public sealed class ResponseMessageText : TelegramResponseMessageBase
{
    public string Content { get; set; } = "";
}

/// <summary>图片回复（Phase 1）。</summary>
public sealed class ResponseMessageImage : TelegramResponseMessageBase
{
    public ImageMedia Media { get; set; } = null!;
}

/// <summary>回调应答（Bot 通道，弹窗提示）。</summary>
public sealed class ResponseMessageCallbackAnswer : TelegramResponseMessageBase
{
    public string? Text { get; set; }

    public bool ShowAlert { get; set; }
}

/// <summary>请求消息类型枚举（分发用）。</summary>
public enum RequestMessageType
{
    Text,
    Image,
    Video,
    Voice,
    File,
    Sticker,
    Callback,
    GroupEvent,
    Service,
    Unknown
}
