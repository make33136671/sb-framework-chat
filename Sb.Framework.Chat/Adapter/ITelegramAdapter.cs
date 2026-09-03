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
}
