using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat;

/// <summary>
/// 出站发送队列：按账号分桶限速 + 失败重试 + 同 peer 保序。
/// 防踩 Telegram 频率硬限制（Bot 约 30 msg/s，个人号按账号限额）。
/// 后台消费（框架 BaseBackgroundTask），队列积压纳入健康检查。
/// </summary>
public interface ISendQueue
{
    /// <summary>入队一条出站消息（返回任务完成 = 已入队，不等实际送达）。</summary>
    Task EnqueueAsync(QueuedSendRequest request, CancellationToken ct = default);

    /// <summary>当前队列积压数（健康检查用）。</summary>
    int PendingCount { get; }
}

/// <summary>出站发送请求（统一消息模型，由适配器翻译为通道原生发送）。</summary>
public sealed class QueuedSendRequest
{
    public string AccountId { get; set; } = "";

    public string PeerId { get; set; } = "";

    public TelegramResponseMessageBase Message { get; set; } = null!;

    /// <summary>幂等键（可选，防重复入队/重复发送）。</summary>
    public string? IdempotencyKey { get; set; }
}
