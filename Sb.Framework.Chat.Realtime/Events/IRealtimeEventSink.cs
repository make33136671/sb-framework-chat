namespace Sb.Framework.Chat.Realtime.Events;

using Sb.Framework.Chat.Realtime.Messages;

/// <summary>
/// 业务事件钩子：实时通讯模块在关键动作前后回调，供业务系统注入敏感词过滤 / 审计 / 自动回复等。
/// 未实现时用 NullRealtimeEventSink 兜底（无副作用）。
/// </summary>
public interface IRealtimeEventSink
{
    /// <summary>
    /// 发送前回调。可修改消息、拒绝发送（返回 null）。
    /// </summary>
    RealtimeMessage? OnBeforeSend(RealtimeMessage message);

    /// <summary>发送成功回调（消息已落库并投递）。</summary>
    void OnMessageDelivered(RealtimeMessage message);

    /// <summary>会话成员加入/离开回调。</summary>
    void OnMembershipChanged(long conversationId, string userId, bool joined);
}

/// <summary>空实现：什么都不做。</summary>
public sealed class NullRealtimeEventSink : IRealtimeEventSink
{
    public RealtimeMessage? OnBeforeSend(RealtimeMessage message) => message;
    public void OnMessageDelivered(RealtimeMessage message) { }
    public void OnMembershipChanged(long conversationId, string userId, bool joined) { }
}
