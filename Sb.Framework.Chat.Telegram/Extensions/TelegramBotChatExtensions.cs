using Microsoft.Extensions.DependencyInjection;
using Sb.Framework.Chat.Adapter;

namespace Sb.Framework.Chat.Telegram.Extensions;

/// <summary>
/// Bot 通道装配扩展。
/// </summary>
public static class TelegramBotChatExtensions
{
    /// <summary>
    /// 注册 Bot 通道适配器。Bot token 通过 AccountContainer 创建账号时提供；
    /// 配置走 appsettings "Chat:Bot" 节（webhook 地址/secret）。
    /// </summary>
    public static IServiceCollection AddTelegramBotAdapter(this IServiceCollection services)
    {
        services.AddSingleton<ITelegramAdapter, BotApiAdapter>();
        return services;
    }
}
