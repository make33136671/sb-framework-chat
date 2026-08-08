using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Adapter;
using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.Tests;

public class MockTelegramAdapterTests
{
    [Fact]
    public async Task Connect_Then_Pull_ReceivesPushedMessage()
    {
        var adapter = new MockTelegramAdapter(AccountType.Personal);
        var bag = new AccountBag { AccountId = "a1", Type = AccountType.Personal };

        await adapter.ConnectAsync(bag, CancellationToken.None);

        adapter.Push(new RequestMessageText { AccountId = "a1", PeerId = "p1", MessageId = 1, Content = "hi" });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = new List<TelegramRequestMessageBase>();
        await foreach (var msg in adapter.PullAsync(cts.Token))
        {
            received.Add(msg);
            break;   // 只取第一条即可，避免等流结束
        }

        Assert.Single(received);
        Assert.Equal("hi", ((RequestMessageText)received[0]).Content);
    }

    [Fact]
    public async Task SendAsync_RecordsToSentList()
    {
        var adapter = new MockTelegramAdapter(AccountType.Bot);
        var bag = new AccountBag { AccountId = "b1", Type = AccountType.Bot };
        await adapter.ConnectAsync(bag, CancellationToken.None);

        await adapter.SendAsync(new ResponseMessageText { PeerId = "p9", Content = "out" }, CancellationToken.None);

        Assert.Single(adapter.Sent);
        Assert.Equal("out", ((ResponseMessageText)adapter.Sent[0]).Content);
    }

    [Fact]
    public async Task Disconnect_SetsNotConnected()
    {
        var adapter = new MockTelegramAdapter(AccountType.Personal);
        var bag = new AccountBag { AccountId = "a1", Type = AccountType.Personal };
        await adapter.ConnectAsync(bag, CancellationToken.None);
        Assert.True(adapter.IsConnected);

        await adapter.DisconnectAsync(bag);
        Assert.False(adapter.IsConnected);
    }
}
