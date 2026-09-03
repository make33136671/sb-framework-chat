namespace Sb.Framework.Chat.Realtime.Messages;

/// <summary>
/// 消息状态枚举（8 态）。
/// </summary>
public enum MessageStatus
{
    /// <summary>发送中。</summary>
    Sending = 1,

    /// <summary>已发送（默认状态）。</summary>
    Sent = 2,

    /// <summary>已送达。</summary>
    Delivered = 3,

    /// <summary>已读。</summary>
    Read = 4,

    /// <summary>发送失败。</summary>
    Failed = 5,

    /// <summary>已撤回。</summary>
    Withdrawn = 6,

    /// <summary>已编辑。</summary>
    Edited = 7,

    /// <summary>已删除（软删除）。</summary>
    Deleted = 8,
}
