using SB.Framework.Core.Util;

namespace Sb.Framework.Chat.Realtime.Options;

/// <summary>
/// Realtime 模块配置项（appsettings.json 节：Chat:Realtime）。
/// </summary>
public sealed class RealtimeOptions
{
    /// <summary>SignalR Hub 映射路径。</summary>
    public string WebSocketPath { get; set; } = "/hubs/realtime";

    /// <summary>在线状态心跳判定阈值（超过视为离线）。</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>心跳保活间隔（服务端清理过期连接用）。</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>消息保留天数（超出由业务侧定时任务清理）。0=永久保留。</summary>
    public int MessageRetentionDays { get; set; } = 90;

    /// <summary>历史消息单次拉取上限。</summary>
    public int MaxHistoryBatch { get; set; } = 100;

    /// <summary>重连策略。</summary>
    public RetryOptions Reconnect { get; set; } = new();

    /// <summary>熔断策略。</summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>连接频率限制。</summary>
    public RateLimitOptions ConnectionRateLimit { get; set; } = new();
}

/// <summary>
/// 熔断器配置。
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>失败阈值（连续失败次数达到该值后打开熔断器）。默认 10。</summary>
    public int FailureThreshold { get; set; } = 10;

    /// <summary>熔断重置超时（打开状态持续多久后进入半开状态尝试恢复）。默认 60 秒。</summary>
    public TimeSpan ResetTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>半开状态下成功阈值（连续成功次数达到该值后关闭熔断器）。默认 3。</summary>
    public int SuccessThreshold { get; set; } = 3;
}

/// <summary>
/// 连接频率限制配置。
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>每分钟最大连接数（单 IP）。默认 60。</summary>
    public int MaxConnectionsPerMinute { get; set; } = 60;

    /// <summary>每分钟每用户最大连接数。默认 30。</summary>
    public int MaxConnectionsPerUserMinute { get; set; } = 30;
}
