using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sb.Framework.Chat.Realtime.Auth;
using Sb.Framework.Chat.Realtime.Conversations;
using Sb.Framework.Chat.Realtime.Endpoints;
using Sb.Framework.Chat.Realtime.Events;
using Sb.Framework.Chat.Realtime.Filters;
using Sb.Framework.Chat.Realtime.Hub;
using Sb.Framework.Chat.Realtime.Media;
using Sb.Framework.Chat.Realtime.Messages;
using Sb.Framework.Chat.Realtime.Options;
using Sb.Framework.Chat.Realtime.Presence;
using Sb.Framework.Chat.Realtime.ReadState;
using Sb.Framework.Chat.Realtime.Services;

namespace Sb.Framework.Chat.Realtime.Extensions;

/// <summary>
/// Realtime 模块装配扩展（业务系统 Program.cs 调用）。
/// 用法：AddSbRealtime() 注册服务 → UseSbRealtime() 映射 Hub 端点。
/// </summary>
public static class SbRealtimeExtensions
{
    /// <summary>
    /// 装配 Realtime 模块：配置绑定 + SignalR + 默认服务（在线/消息/会话/已读/媒体）。
    /// 业务系统可按需替换 IRealtimeUserProvider / IRealtimeAuthProvider / IRealtimeEventSink / IMediaStorageProvider。
    /// </summary>
    /// <param name="services">服务容器</param>
    /// <param name="configuration">应用配置（读取 "Chat:Realtime" 节）</param>
    /// <param name="configure">显式配置（优先级高于 appsettings）</param>
    public static IServiceCollection AddSbRealtime(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<RealtimeOptions>? configure = null)
    {
        var options = new RealtimeOptions();

        if (configuration != null)
        {
            var section = configuration.GetSection("Chat:Realtime");
            if (section.Exists())
                section.Bind(options);
        }

        configure?.Invoke(options);
        services.AddSingleton(options);

        // 在线状态 + 消息投递 + 消息 + 会话 + 已读 + 媒体
        services.AddSingleton<IPresenceService, PresenceService>();
        services.AddSingleton<IMessageDeliveryService, MessageDeliveryService>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddSingleton<IReadStateService, ReadStateService>();
        services.AddSingleton<IMediaService, MediaService>();

        // 消息类型注册表
        services.AddSingleton<IMessageTypeRegistry, DefaultMessageTypeRegistry>();

        // 通信保障：事件总线 + 用户级广播
        services.AddSingleton<IRealtimeEventBus, DefaultRealtimeEventBus>();
        services.AddSingleton<IConnectionBroadcastService, ConnectionBroadcastService>();

        // 可插桩接口：默认实现，业务系统按需覆盖
        services.AddSingleton<IRealtimeUserProvider, DefaultRealtimeUserProvider>();
        services.AddSingleton<IRealtimeAuthProvider, DefaultRealtimeAuthProvider>();
        services.AddSingleton<IRealtimeEventSink, NullRealtimeEventSink>();

        // SignalR
        services.AddSignalR();

        // 连接频率限制过滤器（全局作用于所有 Hub）
        services.AddSingleton<IHubFilter, ConnectionRateLimitFilter>();

        // 心跳清理后台服务
        services.AddHostedService<HeartbeatCleanupService>();

        return services;
    }

    /// <summary>
    /// 映射 Realtime Hub 端点 + 健康检查端点（app.MapControllers 之后调用）。
    /// 默认路径 /hubs/realtime，可在 Options.WebSocketPath 配置。
    /// </summary>
    public static WebApplication UseSbRealtime(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<RealtimeOptions>();
        app.MapHub<RealtimeHub>(options.WebSocketPath);
        app.MapRealtimeHealth();
        return app;
    }
}
