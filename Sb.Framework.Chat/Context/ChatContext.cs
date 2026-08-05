using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.Context;

/// <summary>
/// 会话上下文（按 AccountId + PeerId 维度）。承载最近消息容器与业务状态。
/// 防撤回的内存载体也在此：MessageContainer 删除只标记、不移除。
/// </summary>
public class ChatContext
{
    public string AccountId { get; init; } = "";

    public string PeerId { get; init; } = "";

    /// <summary>最近消息容器（有界，完整数据在 DB）。防撤回的内存层载体。</summary>
    public MessageContainer Messages { get; } = new();

    /// <summary>业务自定义状态（多步会话流程用，如菜单选择中间态）。SDK 不解释其内容。</summary>
    public object? SessionData { get; set; }

    /// <summary>最后活跃时间（UTC）。</summary>
    public DateTime LastActiveTime { get; set; } = SB.Framework.Core.Util.AppTime.UtcNow;
}
