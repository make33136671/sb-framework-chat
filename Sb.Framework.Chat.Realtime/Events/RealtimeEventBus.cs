namespace Sb.Framework.Chat.Realtime.Events;

/// <summary>
/// 连接生命周期事件类型。
/// </summary>
public enum RealtimeConnectionEventType
{
    /// <summary>正在连接。</summary>
    Connecting,

    /// <summary>已连接。</summary>
    Connected,

    /// <summary>已断开。</summary>
    Disconnected,

    /// <summary>正在重连。</summary>
    Reconnecting,

    /// <summary>已重连。</summary>
    Reconnected,

    /// <summary>熔断器打开（拒绝新连接）。</summary>
    CircuitOpen,
}

/// <summary>
/// 连接生命周期事件。
/// </summary>
public sealed class RealtimeConnectionEvent
{
    /// <summary>用户标识。</summary>
    public string UserId { get; set; } = "";

    /// <summary>连接标识。</summary>
    public string ConnectionId { get; set; } = "";

    /// <summary>事件类型。</summary>
    public RealtimeConnectionEventType Type { get; set; }

    /// <summary>关联异常（如断开原因、重连失败原因等）。</summary>
    public Exception? Exception { get; set; }

    /// <summary>事件发生时间（UTC）。</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 实时连接事件总线：发布与订阅连接生命周期事件。
/// </summary>
public interface IRealtimeEventBus
{
    /// <summary>连接生命周期事件（所有订阅者都会收到）。</summary>
    event Action<RealtimeConnectionEvent>? OnConnectionEvent;

    /// <summary>发布一个连接事件。</summary>
    void Publish(RealtimeConnectionEvent evt);
}

/// <summary>
/// 默认事件总线实现：线程安全发布，同步调用所有订阅者。
/// </summary>
public class DefaultRealtimeEventBus : IRealtimeEventBus
{
    private readonly object _lock = new();

    public event Action<RealtimeConnectionEvent>? OnConnectionEvent;

    public void Publish(RealtimeConnectionEvent evt)
    {
        Action<RealtimeConnectionEvent>? handler;
        lock (_lock)
        {
            handler = OnConnectionEvent;
        }
        handler?.Invoke(evt);
    }
}
