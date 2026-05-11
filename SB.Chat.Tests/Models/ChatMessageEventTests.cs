using SB.Chat.Models;

namespace SB.Chat.Tests.Models;

public class ChatMessageEventTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var evt = new ChatMessageEvent();

        Assert.Equal(string.Empty, evt.ChatId);
        Assert.Equal(string.Empty, evt.MessageId);
        Assert.Equal(string.Empty, evt.SenderId);
        Assert.Equal(string.Empty, evt.SenderName);
        Assert.Null(evt.ChatTitle);
        Assert.Equal(string.Empty, evt.Content);
        Assert.Equal(ChatMessageType.Text, evt.MessageType);
        Assert.Null(evt.ReplyToMessageId);
        Assert.False(evt.IsPrivateChat);
        Assert.Null(evt.RawData);
        Assert.NotNull(evt.Media);
        Assert.Empty(evt.Media);
    }

    [Fact]
    public void Media_InitializedAsEmptyList()
    {
        var evt = new ChatMessageEvent();
        evt.Media.Add(new ChatMediaInfo { FileId = "f1" });

        Assert.Single(evt.Media);
        Assert.Equal("f1", evt.Media[0].FileId);
    }
}
