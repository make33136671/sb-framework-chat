using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sb.Framework.Chat.Realtime.Options;
using Sb.Framework.Chat.Realtime.Presence;

namespace Sb.Framework.Chat.Realtime.Endpoints;

/// <summary>
/// 实时连接健康检查端点扩展。
/// </summary>
public static class RealtimeHealthEndpoint
{
    /// <summary>
    /// 映射实时通讯健康检查端点（/health/realtime）。
    /// </summary>
    public static WebApplication MapRealtimeHealth(this WebApplication app)
    {
        app.MapGet("/health/realtime", async (HttpContext context) =>
        {
            var presence = context.RequestServices.GetRequiredService<IPresenceService>();
            var options = context.RequestServices.GetRequiredService<RealtimeOptions>();

            var response = new
            {
                online_users = presence.GetOnlineCount(),
                heartbeat_timeout = options.HeartbeatTimeout.ToString("c"),
                status = "healthy",
                timestamp = DateTime.UtcNow
            };

            await context.Response.WriteAsJsonAsync(response);
        });

        return app;
    }
}
