using SB.Chat.Models;
using SB.Chat.Telegram;

namespace SB.Chat.Tests.Telegram;

public class TelegramBotConnectorTests
{
    [Fact]
    public void Constructor_NullToken_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TelegramBotConnector(null!));
    }

    [Fact]
    public void Constructor_ValidToken_NotConnected()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        Assert.False(bot.IsConnected);
    }

    [Fact]
    public async Task SendTextAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.SendTextAsync("123", "hello"));
    }

    [Fact]
    public async Task SendMenuAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.SendMenuAsync("123", "text", new()));
    }

    [Fact]
    public async Task EditMessageAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.EditMessageAsync("123", "1", "text"));
    }

    [Fact]
    public async Task AnswerCallbackAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.AnswerCallbackAsync("cb1"));
    }

    [Fact]
    public async Task DownloadMediaAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.DownloadMediaAsync("file1"));
    }

    [Fact]
    public async Task GetUserAvatarAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.GetUserAvatarAsync("123"));
    }

    [Fact]
    public async Task ProcessWebhookAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.ProcessWebhookAsync("{}"));
    }

    [Fact]
    public async Task StartPollingAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.StartPollingAsync());
    }

    [Fact]
    public async Task SetWebhookAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.SetWebhookAsync("https://example.com/webhook"));
    }

    [Fact]
    public async Task DeleteWebhookAsync_NotConnected_Throws()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => bot.DeleteWebhookAsync());
    }

    [Fact]
    public async Task StopPollingAsync_WhenNotPolling_DoesNotThrow()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await bot.StopPollingAsync(); // should not throw
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
    {
        using var bot = new TelegramBotConnector("dummy-token");
        await bot.DisconnectAsync();
        Assert.False(bot.IsConnected);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var bot = new TelegramBotConnector("dummy-token");
        bot.Dispose();
        bot.Dispose(); // second dispose should be safe
    }

    [Fact]
    public void IChatBot_Interface_IsImplemented()
    {
        using var bot = new TelegramBotConnector("token");
        Assert.IsAssignableFrom<IChatBot>(bot);
        Assert.IsAssignableFrom<IDisposable>(bot);
    }
}
