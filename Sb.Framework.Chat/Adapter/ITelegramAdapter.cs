using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.Adapter;

/// <summary>
/// 通道适配器接口（SDK 内部，不直接暴露给业务）。
/// 每个适配器把一种通道协议（Bot API / MTProto）的原始更新归一化为统一消息模型。
/// </summary>
public interface ITelegramAdapter
{
    /// <summary>本适配器支持的账号类型。</summary>
    AccountType SupportedAccountType { get; }

    /// <summary>连接账号（建立长连接 / 注册 webhook / 登录会话）。</summary>
    Task ConnectAsync(AccountBag account, CancellationToken ct);

    /// <summary>
    /// 启动二维码登录（WTelegramClient.LoginWithQRCode）。
    /// 二维码 token 通过 onQrToken 持续推送（登录期间 token 可能自动刷新），
    /// 扫码确认成功后自动落盘会话并进入 Ready 状态。
    /// </summary>
    Task StartQrLoginAsync(AccountBag account, Action<string>? onQrToken, CancellationToken ct);

    /// <summary>拉取账号的真实会话列表（dialog 同步，含标题/类型/最后消息时间）。</summary>
    Task<IReadOnlyList<DialogInfo>> SyncDialogsAsync(AccountBag account, CancellationToken ct);

    /// <summary>拉取指定会话的最近历史消息（归一化消息，供业务层落库）。</summary>
    Task<IReadOnlyList<TelegramRequestMessageBase>> FetchHistoryAsync(AccountBag account, string peerId, int limit, CancellationToken ct);

    /// <summary>断开账号连接。</summary>
    Task DisconnectAsync(AccountBag account);

    /// <summary>拉取归一化后的入站消息流（实时 Update + 历史补拉）。</summary>
    IAsyncEnumerable<TelegramRequestMessageBase> PullAsync(CancellationToken ct);

    /// <summary>发送出站响应消息（翻译为通道原生发送调用）。</summary>
    Task SendAsync(TelegramResponseMessageBase response, CancellationToken ct);

    /// <summary>下载媒体字节（Bot: 按 fileId；MTProto: 按 InputFileLocation）。</summary>
    Task<Stream> DownloadMediaAsync(MediaReference media, CancellationToken ct);

    /// <summary>能力探测：该适配器（账号）是否支持某能力（commands/payments/anti_withdrawal 等）。</summary>
    bool Supports(string capability);

    // ---- 扩展能力（默认不支持，MTProto 适配器实现；业务层按需调用，失败不影响主流程） ----

    /// <summary>下载登录账号自己的头像到本地 wwwroot（返回相对 URL；无头像/不支持返回 null）。</summary>
    Task<string?> DownloadSelfAvatarAsync(AccountBag account, CancellationToken ct)
        => Task.FromResult<string?>(null);

    /// <summary>获取 peer 资料详情（用户：姓名/用户名/手机号/在线状态；群/频道：标题/成员数/简介）。</summary>
    Task<PeerDetail?> GetPeerDetailAsync(AccountBag account, string peerId, CancellationToken ct)
        => Task.FromResult<PeerDetail?>(null);

    /// <summary>获取群/频道成员列表（offset 分页，按最近活跃；不支持/非群返回空）。</summary>
    Task<IReadOnlyList<PeerMember>> GetPeerMembersAsync(AccountBag account, string peerId, int offset, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PeerMember>>([]);

    /// <summary>批量获取用户 peer 在线状态（缓存未命中的 peer 被跳过，不出现在结果中）。</summary>
    Task<IReadOnlyList<PeerPresence>> GetUsersPresenceAsync(AccountBag account, IEnumerable<string> peerIds, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PeerPresence>>([]);
}
