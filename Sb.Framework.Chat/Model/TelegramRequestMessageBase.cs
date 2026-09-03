namespace Sb.Framework.Chat.Model;

/// <summary>
/// 消息方向。出站消息（Direction=Outgoing）是防撤回的关键——业务员自己发的违规消息也能被同步捕获。
/// </summary>
public enum MessageDirection
{
    Incoming,
    Outgoing
}

/// <summary>
/// 统一请求消息基类（双通道 Bot/个人号 归一化后的消息模型）。
/// 每个字段都是通道无关的，业务层据此处理消息，不感知消息来自 Bot API 还是 MTProto。
/// </summary>
public abstract class TelegramRequestMessageBase
{
    /// <summary>账号 id（哪个 Bot/个人号收到此消息）。多租户场景下配合 OwnerId 定位租户。</summary>
    public string AccountId { get; set; } = "";

    /// <summary>会话方 id（客户/群）。注意：MessageId 需与 PeerId 组合才能在 MTProto 通道唯一定位。</summary>
    public string PeerId { get; set; } = "";

    /// <summary>通道内消息 id（与 PeerId 组合定位）。</summary>
    public long MessageId { get; set; }

    /// <summary>消息方向。</summary>
    public MessageDirection Direction { get; set; }

    /// <summary>消息时间（UTC）。</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>撤回标记：收到即入库（IsWithdrawn=false），delete 更新到达时置 true，内容保留。</summary>
    public bool IsWithdrawn { get; set; }

    /// <summary>被回复消息的 id（如有）。</summary>
    public string? ReplyToMessageId { get; set; }

    /// <summary>发送者 id（群聊/私聊中消息来源；空=系统/未知）。</summary>
    public string SenderId { get; set; } = "";

    /// <summary>发送者显示名（用户=姓名，频道=频道名；群聊消息头展示用）。</summary>
    public string SenderName { get; set; } = "";

    /// <summary>发送者头像本地相对 URL（群聊展示用，无则空）。</summary>
    public string? SenderAvatar { get; set; }

    /// <summary>是否提及了当前账号（@自己），列表与消息气泡需明显标记。</summary>
    public bool IsMention { get; set; }

    /// <summary>是否直接回复（引用）了当前账号的消息（群聊提醒用）。</summary>
    public bool IsReplyToMine { get; set; }
}

/// <summary>文本消息。</summary>
public sealed class RequestMessageText : TelegramRequestMessageBase
{
    public string Content { get; set; } = "";
}

/// <summary>图片消息。</summary>
public sealed class RequestMessageImage : TelegramRequestMessageBase
{
    public ImageMedia? Media { get; set; }

    /// <summary>图片说明文字（如有）。</summary>
    public string? Caption { get; set; }
}

/// <summary>
/// 文件消息（视频/语音/普通文件/贴纸等非图片媒体）。
/// 防撤回关键：媒体到达时自动下载，撤回后仍可访问。
/// </summary>
public sealed class RequestMessageFile : TelegramRequestMessageBase
{
    /// <summary>媒体类型分类：Video / Voice / File / Sticker。</summary>
    public string FileCategory { get; set; } = "File";

    /// <summary>媒体元数据（含 LocalUrl 防撤回路径）。</summary>
    public MediaBase? Media { get; set; }

    /// <summary>附带文字（文件的 caption / 视频说明）。</summary>
    public string? Caption { get; set; }
}

/// <summary>内联按钮回调（Bot 通道）。</summary>
public sealed class RequestMessageCallback : TelegramRequestMessageBase
{
    public string CallbackData { get; set; } = "";

    /// <summary>回调 id（用于 AnswerCallbackAsync 应答）。</summary>
    public string? CallbackId { get; set; }
}

/// <summary>群组事件。</summary>
public sealed class RequestMessageGroupEvent : TelegramRequestMessageBase
{
    public GroupEventType EventType { get; set; }
}

/// <summary>群事件类型。</summary>
public enum GroupEventType
{
    BotAdded,
    BotRemoved,
    MemberJoined,
    MemberLeft
}

/// <summary>服务消息类型（控制/信号类，非用户消息）。</summary>
public enum ServiceMessageType
{
    /// <summary>消息撤回信号：携带被撤回的消息 id 列表（防撤回=内容已保留，仅标记）。</summary>
    MessagesWithdrawn,

    /// <summary>账号会话失效（手机登出/异地登录等），需业务层重新授权。</summary>
    SessionInvalidated
}

/// <summary>
/// 服务消息（控制信号）。撤回信号（MessagesWithdrawn）用于防撤回：内容已入库，此处仅标记 withdrawn。
/// </summary>
public sealed class RequestMessageService : TelegramRequestMessageBase
{
    public ServiceMessageType ServiceType { get; set; }

    /// <summary>被撤回/受影响的通道消息 id 列表。</summary>
    public List<long> MessageIds { get; set; } = new();
}
