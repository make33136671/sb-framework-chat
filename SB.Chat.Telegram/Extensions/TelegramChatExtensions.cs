using Microsoft.Extensions.DependencyInjection;

namespace SB.Chat.Telegram.Extensions;

/// <summary>
/// Telegram 聊天平台 DI 注册扩展
/// </summary>
public static class TelegramChatExtensions
{
    /// <summary>
    /// 注册 Telegram 聊天平台服务
    /// 注：连接器（TelegramBotConnector / TelegramClientConnector）
    /// 通过 new 直接创建（每个账号凭据不同），不走 DI 容器
    /// </summary>
    public static IServiceCollection AddTelegramChat(this IServiceCollection services)
    {
        return services;
    }
}
