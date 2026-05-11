using SB.Chat.Models;

namespace SB.Chat.Tests.Models;

public class ChatMediaInfoTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var info = new ChatMediaInfo();

        Assert.Equal(string.Empty, info.FileId);
        Assert.Null(info.FileName);
        Assert.Null(info.FileSize);
        Assert.Null(info.MimeType);
        Assert.Equal(ChatMessageType.Text, info.MediaType); // enum default
    }

    [Fact]
    public void FullInit_SetsAllProperties()
    {
        var info = new ChatMediaInfo
        {
            FileId = "file123",
            FileName = "photo.jpg",
            FileSize = 1024,
            MimeType = "image/jpeg",
            MediaType = ChatMessageType.Image
        };

        Assert.Equal("file123", info.FileId);
        Assert.Equal("photo.jpg", info.FileName);
        Assert.Equal(1024, info.FileSize);
        Assert.Equal("image/jpeg", info.MimeType);
        Assert.Equal(ChatMessageType.Image, info.MediaType);
    }
}
