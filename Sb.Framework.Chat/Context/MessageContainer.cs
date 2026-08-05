using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.Context;

/// <summary>
/// 消息容器：会话上下文内最近 N 条消息的有界缓冲。
/// <b>防撤回语义</b>：收到即 Add 入容器；delete 更新到达时调用 MarkWithdrawn——只标记，不移除内容。
/// 内存有界仅作热数据，完整历史以 DB 为准（chat_message 表，撤回标记持久化）。
/// </summary>
public class MessageContainer
{
    private readonly object _lock = new();
    private readonly LinkedList<TelegramRequestMessageBase> _items = new();

    /// <summary>内存容量上限（默认 50）。超出按最旧优先淘汰。</summary>
    public int Capacity { get; init; } = 50;

    /// <summary>加入一条消息（收到即调用）。</summary>
    public void Add(TelegramRequestMessageBase message)
    {
        if (message == null) return;
        lock (_lock)
        {
            _items.AddLast(message);
            while (_items.Count > Capacity && _items.First != null)
                _items.RemoveFirst();
        }
    }

    /// <summary>
    /// 标记某消息已撤回（内容保留，不移除）。用于防撤回——审查时仍可见原文。
    /// </summary>
    /// <param name="messageId">被撤回的消息 id</param>
    /// <returns>true=命中并标记；false=容器中无此消息（可能已淘汰或本不在此会话）</returns>
    public bool MarkWithdrawn(long messageId)
    {
        lock (_lock)
        {
            var node = _items.First;
            while (node != null)
            {
                if (node.Value.MessageId == messageId)
                {
                    node.Value.IsWithdrawn = true;
                    return true;
                }
                node = node.Next;
            }
        }
        return false;
    }

    /// <summary>当前快照（按时间顺序）。</summary>
    public IReadOnlyList<TelegramRequestMessageBase> Snapshot()
    {
        lock (_lock) return _items.ToList();
    }

    /// <summary>清空（会话关闭/重置用）。</summary>
    public void Clear()
    {
        lock (_lock) _items.Clear();
    }
}
