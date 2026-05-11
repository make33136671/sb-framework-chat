using SB.Chat.Models;

namespace SB.Chat.Tests.Models;

public class ChatGroupEventTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var evt = new ChatGroupEvent();

        Assert.Equal(ChatGroupEventType.BotAdded, evt.EventType); // enum default = 0
        Assert.Equal(string.Empty, evt.ChatId);
        Assert.Null(evt.ChatTitle);
    }

    [Theory]
    [InlineData(ChatGroupEventType.BotAdded)]
    [InlineData(ChatGroupEventType.BotRemoved)]
    [InlineData(ChatGroupEventType.MemberJoined)]
    [InlineData(ChatGroupEventType.MemberLeft)]
    public void EventType_AllValues(ChatGroupEventType type)
    {
        var evt = new ChatGroupEvent { EventType = type };
        Assert.Equal(type, evt.EventType);
    }
}
