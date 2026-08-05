using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat;

/// <summary>
/// 统一通知推送服务：业务系统一行式主动发送（订单通知/告警/业务消息）。
/// 经发送队列（ISendQueue）路由到账号对应适配器，自动限速/重试/保序。
/// </summary>
public interface INotificationService
{
    /// <summary>发送文本消息。</summary>
    /// <param name="accountId">目标账号（Bot / 个人号）</param>
    /// <param name="peerId">目标会话方</param>
    /// <param name="content">文本内容</param>
    /// <param name="replyToMessageId">回复引用消息 id（可选）</param>
    /// <returns>发送成功后的通道消息 id（可在 ResponseMessage 中获得，null=未发送）</returns>
    Task<string?> SendTextAsync(
        string accountId,
        string peerId,
        string content,
        string? replyToMessageId = null,
        CancellationToken ct = default);

    /// <summary>发送图片消息（Phase 1）。</summary>
    Task<string?> SendImageAsync(
        string accountId,
        string peerId,
        ImageMedia media,
        string? caption = null,
        CancellationToken ct = default);
}
