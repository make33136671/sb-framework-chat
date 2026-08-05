using Microsoft.Extensions.DependencyInjection;
using Sb.Framework.Chat.Adapter;

namespace Sb.Framework.Chat.Telegram.MtProto.Extensions;

/// <summary>
/// 个人号（MTProto）通道装配扩展。
/// </summary>
public static class TelegramMtProtoChatExtensions
{
    /// <summary>
    /// 注册个人号 MTProto 适配器。需实现并注册 <c>IAuthProvider</c> 提供验证码/2FA 输入；
    /// 配置走 appsettings "Chat:MtProto" 节。
    /// </summary>
    public static IServiceCollection AddTelegramMtProtoAdapter(this IServiceCollection services)
    {
        services.AddSingleton<ITelegramAdapter, MtProtoAdapter>();
        return services;
    }
}
