using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Adapter;
using SB.Framework.Core.Util;

namespace Sb.Framework.Chat.Services;

/// <summary>
/// 默认发送队列：Channel 有界队列 + 后台消费者。
/// - 按账号分桶限速（RateLimiterUtil）
/// - 失败重试（RetryUtil，指数退避）
/// - 同 peer 保序（Channel 单消费者顺序）
/// </summary>
public sealed class DefaultSendQueue : BackgroundService, ISendQueue
{
    private readonly Channel<QueuedSendRequest> _channel;
    private readonly ITelegramAdapterResolver _resolver;
    private readonly ILogger<DefaultSendQueue> _logger;
    private readonly TelegramChatOptions _options;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public DefaultSendQueue(
        ITelegramAdapterResolver resolver,
        ILogger<DefaultSendQueue> logger,
        TelegramChatOptions options)
    {
        _resolver = resolver;
        _logger = logger;
        _options = options;
        // 有界队列，防止无限积压
        _channel = Channel.CreateBounded<QueuedSendRequest>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
    }

    public int PendingCount => _channel.Reader.Count;

    public async Task EnqueueAsync(QueuedSendRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        await _channel.Writer.WriteAsync(request, ct).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // 让 HostedService 链立即返回

        await foreach (var request in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SendWithRateLimitAndRetryAsync(request, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 单个请求失败不阻塞队列；逐条重试已在上层完成
                _logger.LogError(ex, "发送队列消费失败: Account={AccountId}", request.AccountId);
            }
        }
    }

    private async Task SendWithRateLimitAndRetryAsync(QueuedSendRequest request, CancellationToken ct)
    {
        var adapter = _resolver.Resolve(request.AccountId);
        if (adapter == null)
        {
            _logger.LogWarning("发送跳过：无适配器或账号不存在 Account={AccountId}", request.AccountId);
            return;
        }

        // 按账号限速（窗口与限额取自配置，Bot 默认 30/s）
        await RateLimiterUtil.WaitAcquireAsync(
            $"telegram:send:{request.AccountId}",
            limit: 30,
            window: TimeSpan.FromSeconds(1),
            ct).ConfigureAwait(false);

        // 同 peer 保序：整个消费循环单线程，天然保序；再加锁防止并发触发重试乱序
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await RetryUtil.ExecuteAsync(async (attempt, token) =>
            {
                await adapter.SendAsync(request.Message, token).ConfigureAwait(false);
            }, new RetryOptions
            {
                MaxRetries = _options.MtProto.Reconnect.MaxRetries,
                BaseDelay = TimeSpan.FromMilliseconds(_options.MtProto.Reconnect.BaseBackoffMs),
                MaxDelay = TimeSpan.FromMilliseconds(_options.MtProto.Reconnect.MaxBackoffMs),
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
