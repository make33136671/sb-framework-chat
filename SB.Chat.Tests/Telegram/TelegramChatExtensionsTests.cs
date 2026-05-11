using Microsoft.Extensions.DependencyInjection;
using SB.Chat.Telegram.Extensions;

namespace SB.Chat.Tests.Telegram;

public class TelegramChatExtensionsTests
{
    [Fact]
    public void AddTelegramChat_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        var result = services.AddTelegramChat();

        Assert.Same(services, result);
    }

    [Fact]
    public void AddTelegramChat_CanBeCalledMultipleTimes()
    {
        var services = new ServiceCollection();
        services.AddTelegramChat();
        services.AddTelegramChat(); // should not throw
    }
}
