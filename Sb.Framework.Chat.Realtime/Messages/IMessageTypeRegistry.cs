using System.Collections.Concurrent;

namespace Sb.Framework.Chat.Realtime.Messages;

/// <summary>
/// 消息类型注册表：管理不同 contentType 对应的渲染器与处理器。
/// </summary>
public interface IMessageTypeRegistry
{
    /// <summary>注册一个消息类型及其渲染器和处理器。</summary>
    void Register(string typeName, IMessageRenderer renderer, IMessageHandler handler);

    /// <summary>尝试解析消息类型，获取对应的渲染器和处理器。</summary>
    bool TryResolve(string typeName, out IMessageRenderer? renderer, out IMessageHandler? handler);

    /// <summary>判断指定类型是否已注册。</summary>
    bool IsRegistered(string typeName);
}

/// <summary>
/// 消息渲染器：将消息渲染为可展示的文本/HTML。
/// </summary>
public interface IMessageRenderer
{
    /// <summary>渲染消息内容。</summary>
    Task<string> RenderAsync(RealtimeMessage message);
}

/// <summary>
/// 消息处理器：消息投递后的业务处理逻辑（如敏感词、自动回复等）。
/// </summary>
public interface IMessageHandler
{
    /// <summary>处理消息。</summary>
    Task HandleAsync(RealtimeMessage message, CancellationToken ct);
}

/// <summary>
/// 默认消息类型注册表实现（线程安全）。
/// </summary>
public class DefaultMessageTypeRegistry : IMessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, (IMessageRenderer Renderer, IMessageHandler Handler)> _registry = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public void Register(string typeName, IMessageRenderer renderer, IMessageHandler handler)
    {
        if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException(nameof(typeName));
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        lock (_lock)
        {
            _registry[typeName] = (renderer, handler);
        }
    }

    /// <inheritdoc />
    public bool TryResolve(string typeName, out IMessageRenderer? renderer, out IMessageHandler? handler)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            renderer = null;
            handler = null;
            return false;
        }

        if (_registry.TryGetValue(typeName, out var entry))
        {
            renderer = entry.Renderer;
            handler = entry.Handler;
            return true;
        }

        renderer = null;
        handler = null;
        return false;
    }

    /// <inheritdoc />
    public bool IsRegistered(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        return _registry.ContainsKey(typeName);
    }
}
