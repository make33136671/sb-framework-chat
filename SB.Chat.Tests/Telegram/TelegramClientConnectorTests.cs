using SB.Chat.Telegram;

namespace SB.Chat.Tests.Telegram;

public class TelegramClientConnectorTests
{
    [Fact]
    public void Constructor_NullApiId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TelegramClientConnector(null!, "hash", "phone", "session"));
    }

    [Fact]
    public void Constructor_NullApiHash_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TelegramClientConnector("id", null!, "phone", "session"));
    }

    [Fact]
    public void Constructor_NullPhone_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TelegramClientConnector("id", "hash", null!, "session"));
    }

    [Fact]
    public void Constructor_NullSessionPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TelegramClientConnector("id", "hash", "phone", null!));
    }

    [Fact]
    public void Constructor_ValidParams_DoesNotThrow()
    {
        using var client = new TelegramClientConnector("12345", "hash", "+1234567890", "test.session");
        Assert.IsAssignableFrom<IChatClient>(client);
        Assert.IsAssignableFrom<IDisposable>(client);
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
    {
        using var client = new TelegramClientConnector("12345", "hash", "+1234567890", "test.session");
        await client.DisconnectAsync(); // should be safe
    }

    [Fact]
    public async Task DownloadMediaAsync_WhenNotConnected_Throws()
    {
        using var client = new TelegramClientConnector("12345", "hash", "+1234567890", "test.session");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DownloadMediaAsync("file1"));
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var client = new TelegramClientConnector("12345", "hash", "+1234567890", "test.session");
        client.Dispose();
        client.Dispose(); // second dispose safe
    }
}
