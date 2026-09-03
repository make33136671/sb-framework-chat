using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Realtime.Options;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace Sb.Framework.Chat.Realtime.Filters;

/// <summary>
/// 连接频率限制 Hub 过滤器：限制单 IP 和单用户每分钟的连接次数，防止恶意连接轰炸。
/// </summary>
public class ConnectionRateLimitFilter : IHubFilter
{
    private readonly RateLimitOptions _options;
    private readonly ILogger<ConnectionRateLimitFilter> _logger;

    // IP -> 最近一分钟的连接时间戳
    private readonly ConcurrentDictionary<string, List<DateTime>> _ipTimestamps = new();
    // 用户 ID -> 最近一分钟的连接时间戳
    private readonly ConcurrentDictionary<string, List<DateTime>> _userTimestamps = new();

    public ConnectionRateLimitFilter(RealtimeOptions options, ILogger<ConnectionRateLimitFilter> logger)
    {
        _options = options.ConnectionRateLimit;
        _logger = logger;
    }

    /// <summary>
    /// 连接建立时的频率限制检查。
    /// </summary>
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var httpContext = context.Context.GetHttpContext();
        var ip = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userId = GetUserId(context);

        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-1);

        // 检查 IP 频率限制
        var ipList = _ipTimestamps.GetOrAdd(ip, _ => new List<DateTime>());
        lock (ipList)
        {
            // 清理过期记录
            ipList.RemoveAll(t => t < cutoff);

            if (ipList.Count >= _options.MaxConnectionsPerMinute)
            {
                _logger.LogWarning("Realtime 连接频率超限（IP）: IP={Ip}, Count={Count}, Limit={Limit}",
                    ip, ipList.Count, _options.MaxConnectionsPerMinute);
                context.Context.Abort();
                return;
            }

            ipList.Add(now);
        }

        // 检查用户频率限制（未登录用户跳过）
        if (!string.IsNullOrEmpty(userId))
        {
            var userList = _userTimestamps.GetOrAdd(userId, _ => new List<DateTime>());
            lock (userList)
            {
                // 清理过期记录
                userList.RemoveAll(t => t < cutoff);

                if (userList.Count >= _options.MaxConnectionsPerUserMinute)
                {
                    _logger.LogWarning("Realtime 连接频率超限（用户）: UserId={UserId}, Count={Count}, Limit={Limit}",
                        userId, userList.Count, _options.MaxConnectionsPerUserMinute);
                    context.Context.Abort();
                    return;
                }

                userList.Add(now);
            }
        }

        await next(context);
    }

    /// <summary>
    /// 从 Hub 上下文中获取用户 ID（优先 JWT Id 声明，其次 UserIdentifier）。
    /// </summary>
    private static string? GetUserId(HubLifetimeContext context)
    {
        var idClaim = context.Context.User?.FindFirstValue("Id");
        if (!string.IsNullOrEmpty(idClaim)) return idClaim;
        return context.Context.UserIdentifier;
    }

    /// <summary>
    /// Hub 方法调用拦截（透传，不做频率限制）。
    /// </summary>
    public async Task InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, Task> next)
    {
        await next(invocationContext);
    }

    /// <summary>
    /// 连接断开处理（透传）。
    /// </summary>
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await next(context);
    }
}
