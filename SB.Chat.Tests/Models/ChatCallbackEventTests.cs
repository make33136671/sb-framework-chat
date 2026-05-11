using SB.Chat.Models;

namespace SB.Chat.Tests.Models;

public class ChatCallbackEventTests
{
    [Fact]
    public void Defaults_AreEmptyStrings()
    {
        var evt = new ChatCallbackEvent();

        Assert.Equal(string.Empty, evt.CallbackId);
        Assert.Equal(string.Empty, evt.ChatId);
        Assert.Equal(string.Empty, evt.MessageId);
        Assert.Equal(string.Empty, evt.UserId);
        Assert.Equal(string.Empty, evt.CallbackData);
    }

    [Fact]
    public void AllProperties_Settable()
    {
        var evt = new ChatCallbackEvent
        {
            CallbackId = "cb1",
            ChatId = "chat1",
            MessageId = "msg1",
            UserId = "user1",
            CallbackData = "menu:action"
        };

        Assert.Equal("cb1", evt.CallbackId);
        Assert.Equal("chat1", evt.ChatId);
        Assert.Equal("msg1", evt.MessageId);
        Assert.Equal("user1", evt.UserId);
        Assert.Equal("menu:action", evt.CallbackData);
    }
}
