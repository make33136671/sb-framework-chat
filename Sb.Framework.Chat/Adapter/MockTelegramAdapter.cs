using System.Threading.Channels;
using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.Adapter;

/// <summary>
/// Mock 适配器（开发/测试用）：内存模拟 Bot/个人号收发，离线开发与单测不触网。
/// 用法：AddTelegramMockAdapter() 注册；测试中手动 Push 模拟入站消息。
/// </summary>
public sealed class MockTelegramAdapter : ITelegramAdapter
{
    private readonly Channel<TelegramRequestMessageBase> _inbox = Channel.CreateUnbounded<TelegramRequestMessageBase>();
    private readonly List<TelegramResponseMessageBase> _sent = new();
    private readonly object _lock = new();
    private bool _connected;

    public AccountType SupportedAccountType { get; }

    public bool IsConnected => _connected;

    /// <summary>已发送的出站消息（测试断言用）。</summary>
    public IReadOnlyList<TelegramResponseMessageBase> Sent
    {
        get { lock (_lock) return _sent.ToList(); }
    }

    public MockTelegramAdapter(AccountType accountType = AccountType.Bot)
    {
        SupportedAccountType = accountType;
    }

    /// <summary>手动推送一条模拟入站消息（测试用），会被 PullAsync 拉取。</summary>
    public void Push(TelegramRequestMessageBase message)
        => _inbox.Writer.TryWrite(message);

    public Task ConnectAsync(AccountBag account, CancellationToken ct)
    {
        _connected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(AccountBag account)
    {
        _connected = false;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TelegramRequestMessageBase> PullAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var msg in _inbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return msg;
    }

    public Task SendAsync(TelegramResponseMessageBase response, CancellationToken ct)
    {
        lock (_lock) _sent.Add(response);
        return Task.CompletedTask;
    }

    public Task<Stream> DownloadMediaAsync(MediaReference media, CancellationToken ct)
    {
        // Mock：返回空流
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public bool Supports(string capability) => capability switch
    {
        "send_text" => true,
        "send_image" => true,
        _ => false
    };
}
