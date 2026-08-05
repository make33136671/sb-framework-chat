using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sb.Framework.Chat.Cache;

namespace Sb.Framework.Chat.Extensions;

/// <summary>
/// Telegram SDK 装配扩展（业务系统 Program.cs 调用）。
/// </summary>
public static class TelegramChatExtensions
{
    /// <summary>
    /// 装配 SDK 核心：配置绑定 + 缓存策略注册。
    /// 通道适配器（Bot / MtProto）由业务系统按需调用 AddTelegramBotAdapter / AddTelegramMtProtoAdapter。
    /// </summary>
    /// <param name="services">服务容器</param>
    /// <param name="configure">显式配置（优先级高于 appsettings "Chat" 节）</param>
    /// <param name="configuration">应用配置（读取 "Chat" 节）</param>
    public static IServiceCollection AddTelegramChat(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<TelegramChatOptions>? configure = null)
    {
        var options = new TelegramChatOptions();

        // 1. appsettings "Chat" 节
        if (configuration != null)
        {
            var section = configuration.GetSection("Chat");
            if (section.Exists())
                section.Bind(options);
        }

        // 2. 显式配置覆盖
        configure?.Invoke(options);

        services.AddSingleton(options);

        // 缓存策略：默认内存实现（可替换为桥接框架 Redis ICacheService 的实现）
        services.AddSingleton<IMessageCacheStrategy, MemoryMessageCacheStrategy>();

        return services;
    }
}
