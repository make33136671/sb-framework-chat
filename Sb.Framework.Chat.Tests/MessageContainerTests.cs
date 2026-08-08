using Sb.Framework.Chat.Context;
using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.Tests;

public class MessageContainerTests
{
    private static RequestMessageText MakeText(long id, string content = "hi")
        => new() { AccountId = "a1", PeerId = "p1", MessageId = id, Content = content };

    [Fact]
    public void Add_KeepsLastCapacity()
    {
        var container = new MessageContainer { Capacity = 3 };
        for (var i = 1; i <= 5; i++) container.Add(MakeText(i));

        var snapshot = container.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(3, snapshot[0].MessageId);
        Assert.Equal(5, snapshot[^1].MessageId);
    }

    [Fact]
    public void MarkWithdrawn_FlagsButKeepsContent()
    {
        var container = new MessageContainer();
        var msg = MakeText(42, "敏感内容");
        container.Add(msg);

        var hit = container.MarkWithdrawn(42);

        Assert.True(hit);
        var snapshot = container.Snapshot();
        Assert.Single(snapshot);
        Assert.True(snapshot[0].IsWithdrawn);
        Assert.Equal("敏感内容", ((RequestMessageText)snapshot[0]).Content);
    }

    [Fact]
    public void MarkWithdrawn_UnknownId_ReturnsFalse()
    {
        var container = new MessageContainer();
        container.Add(MakeText(1));

        Assert.False(container.MarkWithdrawn(999));
    }

    [Fact]
    public void Clear_EmptiesContainer()
    {
        var container = new MessageContainer();
        container.Add(MakeText(1));
        container.Add(MakeText(2));

        container.Clear();

        Assert.Empty(container.Snapshot());
    }
}
