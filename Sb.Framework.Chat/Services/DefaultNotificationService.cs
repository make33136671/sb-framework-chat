using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Adapter;
using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.Services;

/// <summary>
/// 默认通知推送服务：构造统一响应消息并入发送队列。
/// 业务系统一行式主动推送（订单通知/告警等），限速/重试/保序由队列负责。
/// </summary>
public sealed class DefaultNotificationService : INotificationService
{
    private readonly ISendQueue _queue;
    private readonly ITelegramAdapterResolver _resolver;
    private readonly ILogger<DefaultNotificationService> _logger;

    public DefaultNotificationService(
        ISendQueue queue,
        ITelegramAdapterResolver resolver,
        ILogger<DefaultNotificationService> logger)
    {
        _queue = queue;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<string?> SendTextAsync(
        string accountId,
        string peerId,
        string content,
        string? replyToMessageId = null,
        CancellationToken ct = default)
    {
        if (_resolver.Resolve(accountId) == null)
        {
            _logger.LogWarning("通知发送跳过：无适配器或账号不存在 Account={AccountId}", accountId);
            return null;
        }

        var response = new ResponseMessageText
        {
            PeerId = $"{accountId}:{peerId}",
            ReplyToMessageId = replyToMessageId,
            Content = content,
        };

        await _queue.EnqueueAsync(new QueuedSendRequest
        {
            AccountId = accountId,
            PeerId = $"{accountId}:{peerId}",
            Message = response,
        }, ct).ConfigureAwait(false);

        return peerId; // 通道消息 id 由适配器异步回填（简化：返回 peerId 表示已入队）
    }

    public async Task<string?> SendImageAsync(
        string accountId,
        string peerId,
        ImageMedia media,
        string? caption = null,
        CancellationToken ct = default)
    {
        if (_resolver.Resolve(accountId) == null)
        {
            _logger.LogWarning("通知发送跳过：无适配器或账号不存在 Account={AccountId}", accountId);
            return null;
        }

        var response = new ResponseMessageImage
        {
            PeerId = $"{accountId}:{peerId}",
            Media = media,
        };

        await _queue.EnqueueAsync(new QueuedSendRequest
        {
            AccountId = accountId,
            PeerId = $"{accountId}:{peerId}",
            Message = response,
        }, ct).ConfigureAwait(false);

        return peerId;
    }
}
