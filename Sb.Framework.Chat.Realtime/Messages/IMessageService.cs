using FreeSql;

namespace Sb.Framework.Chat.Realtime.Messages;

/// <summary>
/// 消息服务：落库（防撤回）、分配 seq、投递、历史分页、撤回。
/// </summary>
public interface IMessageService
{
    /// <summary>发送消息（业务校验 → 落库 → 投递）。</summary>
    Task<RealtimeMessage?> SendAsync(RealtimeMessage message, CancellationToken ct = default);

    /// <summary>发送系统消息（欢迎语/session_init/转接/离线等）。</summary>
    Task<RealtimeMessage?> SendSystemAsync(long conversationId, string contentType, string? payload,
        string senderId = "system", CancellationToken ct = default);

    /// <summary>历史消息分页（按 seq 倒序往前翻）。</summary>
    Task<IReadOnlyList<RealtimeMessage>> GetHistoryAsync(long conversationId, long beforeSeq = 0, int count = 50,
        CancellationToken ct = default);

    /// <summary>撤回消息（仅标记，内容保留）。</summary>
    [Obsolete("请使用带 userId 参数的 WithdrawAsync 重载。")]
    Task<bool> WithdrawAsync(long conversationId, long seq, CancellationToken ct = default);

    /// <summary>标记消息已送达。</summary>
    Task<bool> MarkDeliveredAsync(long conversationId, string userId, long seq, CancellationToken ct = default);

    /// <summary>撤回消息（带发送者权限校验）。</summary>
    Task<RealtimeMessage?> WithdrawAsync(long conversationId, long seq, string userId, CancellationToken ct = default);

    /// <summary>编辑消息（带发送者权限校验）。</summary>
    Task<RealtimeMessage?> EditAsync(long conversationId, long seq, string userId, string newContent, CancellationToken ct = default);

    /// <summary>删除消息（软删除，带发送者权限校验）。</summary>
    Task<bool> DeleteAsync(long conversationId, long seq, string userId, CancellationToken ct = default);
}
