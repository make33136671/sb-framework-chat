using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Realtime.Options;
using Sb.Framework.Chat.Realtime.Presence;

namespace Sb.Framework.Chat.Realtime.Services;

/// <summary>
/// 心跳清理后台服务：定期清理过期的在线状态条目。
/// </summary>
public class HeartbeatCleanupService : IHostedService, IDisposable
{
    private readonly IPresenceService _presenceService;
    private readonly RealtimeOptions _options;
    private readonly ILogger<HeartbeatCleanupService> _logger;
    private Timer? _timer;
    private bool _disposed;

    public HeartbeatCleanupService(
        IPresenceService presenceService,
        RealtimeOptions options,
        ILogger<HeartbeatCleanupService> logger)
    {
        _presenceService = presenceService;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HeartbeatCleanupService 启动，清理间隔：{Interval}", _options.HeartbeatInterval);
        _timer = new Timer(DoCleanup, null, _options.HeartbeatInterval, _options.HeartbeatInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HeartbeatCleanupService 停止");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void DoCleanup(object? state)
    {
        try
        {
            var removed = _presenceService.CleanupStale();
            if (removed.Count > 0)
            {
                _logger.LogInformation("心跳清理完成，清理过期用户数：{Count}", removed.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "心跳清理异常");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer?.Dispose();
        _disposed = true;
    }
}
