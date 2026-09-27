using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Adapter;
using Sb.Framework.Chat.Model;
using TL;
// 别名消除与 TL.Channel 的命名冲突。
using Channel = System.Threading.Channels.Channel;

namespace Sb.Framework.Chat.Telegram.MtProto;

/// <summary>
/// 个人号通道适配器（MTProto，WTelegramClient）。
/// 会话生命周期 / 认证流程 / Update 流 / 历史补拉 / 防撤回 / 媒体下载。
/// 一个适配器管理该类型下的全部个人号账号（单机数百可扛）。
/// </summary>
/// <remarks>
/// 注意：WTelegramClient 依赖真实凭据与网络，本适配器需真实账号验证。
/// 认证输入（验证码/2FA/导入会话）由业务实现 <see cref="IAuthProvider"/> 提供。
/// </remarks>
public sealed class MtProtoAdapter : ITelegramAdapter, IDisposable
{
    private readonly TelegramChatOptions _options;
    private readonly IAuthProvider _authProvider;
    private readonly ILogger<MtProtoAdapter> _logger;
    private readonly IMediaDownloadPolicyProvider? _mediaPolicy;
    private readonly ConcurrentDictionary<string, AccountConnection> _connections = new();
    private bool _disposed;


    public AccountType SupportedAccountType => AccountType.Personal;

    public MtProtoAdapter(
        TelegramChatOptions options,
        IAuthProvider authProvider,
        ILogger<MtProtoAdapter> logger,
        IMediaDownloadPolicyProvider? mediaPolicy = null)
    {
        _options = options;
        _authProvider = authProvider;
        _logger = logger;
        _mediaPolicy = mediaPolicy;
    }

/// <summary>单个账号的连接：WTelegramClient + 入站消息通道 + 文件定位表。</summary>
    private sealed class AccountConnection
    {
        public WTelegram.Client Client = null!;
        public Channel<TelegramRequestMessageBase> Inbox = Channel.CreateUnbounded<TelegramRequestMessageBase>();
        public CancellationTokenSource Cts = new();
// 防撤回：消息 id → 文件定位（撤回前已可下载的引用缓存）
        public ConcurrentDictionary<string, InputFileLocationBase> FileLocations = new();
        // 代理隧道 TcpClient（使用代理时持有，随连接一起释放）
        public TcpClient? ProxyTcpClient;
// 会话同步缓存：peerId → InputPeer（含 access_hash，历史拉取用）
        public ConcurrentDictionary<string, InputPeer> PeerInputs = new();
        // 发送者头像缓存: senderId → 本地 URL（群聊消息展示用）
        public ConcurrentDictionary<string, string?> SenderAvatars = new();
        public byte[]? SessionSnapshot;
    }

    // ---- ITelegramAdapter ----

    public Task ConnectAsync(AccountBag account, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (_connections.ContainsKey(account.AccountId))
            return Task.CompletedTask;

        var conn = new AccountConnection();
        if (!_connections.TryAdd(account.AccountId, conn))
            return Task.CompletedTask;

        AccountContainer.UpdateState(account.AccountId, AccountSessionState.Created);
        _ = ConnectInternalAsync(account, conn, ct);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(AccountBag account)
    {
        if (account == null) return Task.CompletedTask;
        if (_connections.TryRemove(account.AccountId, out var conn))
        {
            conn.Cts.Cancel();
            _ = DisposeClientAsync(conn.Client, account.AccountId);
            conn.ProxyTcpClient?.Dispose();
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Disconnected);
        }
        return Task.CompletedTask;
    }

/// <summary>有界等待客户端释放（Dispose 内部 ResetAsync 可能长时间阻塞，不能同步卡死调用方）。</summary>
    private async Task DisposeClientAsync(WTelegram.Client? client, string accountId)
    {
        if (client == null) return;
        try
        {
            var dispose = client.DisposeAsync().AsTask();
            if (await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false) != dispose)
            {
                _logger.LogWarning("客户端释放超时（3s），文件锁由后续重试兜底: account={AccountId}", accountId);
            }
        }
        catch (Exception ex)
        {
_logger.LogDebug(ex, "客户端释放异常: account={AccountId}", accountId);
        }
    }

    /// <summary>
/// 启动二维码登录（WTelegramClient.LoginWithQRCode）。
/// 二维码 token 通过 onQrToken 持续推送（登录期间 token 可能自动刷新）；
/// 扫码确认成功后自动落盘会话文件并进入 Ready 状态。
    /// </summary>
    public async Task StartQrLoginAsync(AccountBag account, Action<string>? onQrToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (_connections.ContainsKey(account.AccountId))
            return;

        var conn = new AccountConnection();
        if (!_connections.TryAdd(account.AccountId, conn))
            return;
        AccountContainer.UpdateState(account.AccountId, AccountSessionState.Created);

        try
        {
            var sessionDir = Path.Combine(AppContext.BaseDirectory, "chat_sessions");
            Directory.CreateDirectory(sessionDir);
            var sessionPath = Path.Combine(sessionDir, $"{account.AccountId}.session");

            conn.Client = await CreateClientAsync(account, conn, sessionPath, ct).ConfigureAwait(false);

            // 二维码登录总时长上限（5 分钟）：防止 DC 迁移链路异常时二维码无限轮换、用户反复扫失效码
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            _logger.LogInformation("启动二维码登录: account={AccountId}", account.AccountId);
            var user = await conn.Client.LoginWithQRCode(
                    token => onQrToken?.Invoke(token),
                    null, false, linkedCts.Token)
                .ConfigureAwait(false);

            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Ready);
            _logger.LogInformation("二维码登录成功: {AccountId} @ {User}",
                account.AccountId, SecurityMask(user.username ?? user.first_name ?? user.id.ToString()));
            CaptureSessionSnapshot(account.AccountId, conn, sessionPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 外部取消（如服务停止）：不写认证错误，仅清理
            DisposeConnection(account.AccountId, conn);
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Invalidated);
        }
        catch (Exception ex)
        {
            var isTimeout = ex is OperationCanceledException or TimeoutException;
            _logger.LogError(ex, "二维码登录失败: {AccountId}", account.AccountId);
            account.LastError = isTimeout
                ? "二维码登录超时（二维码已失效），请返回重新添加账号"
                : FriendlyConnectError(ex);
            DisposeConnection(account.AccountId, conn);
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Invalidated);
        }
    }
public async IAsyncEnumerable<TelegramRequestMessageBase> PullAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
// 合并所有账号的入站消息。
        while (!ct.IsCancellationRequested)
        {
            var snapshot = _connections.Values.ToArray();
            var tasks = snapshot
                .Where(c => !c.Cts.IsCancellationRequested)
                .Select(c => c.Inbox.Reader.WaitToReadAsync(ct).AsTask())
                .ToArray();

            if (tasks.Length == 0)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                continue;
            }

            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            var conn = snapshot[Array.IndexOf(tasks, completed)];
            if (conn.Inbox.Reader.TryRead(out var message))
                yield return message;
        }
    }

    public Task SendAsync(TelegramResponseMessageBase response, CancellationToken ct)
    {
        var accountId = ResolveAccountIdFor(response);
        if (!_connections.TryGetValue(accountId, out var conn))
throw new InvalidOperationException($"账号未连接: {accountId}");

        return response switch
        {
            ResponseMessageText text => SendTextAsync(conn, accountId, text, ct),
            ResponseMessageImage image => SendImageAsync(conn, accountId, image, ct),
            _ => throw new NotSupportedException($"个人号通道暂不支持响应类型 {response.GetType().Name}")
        };
    }

    public Task<Stream> DownloadMediaAsync(MediaReference media, CancellationToken ct)
    {
        if (!_connections.TryGetValue(media.AccountId, out var conn))
throw new InvalidOperationException($"账号未连接: {media.AccountId}");

// MediaId 记录为消息 id，从文件定位表取 InputFileLocationBase
        if (!conn.FileLocations.TryGetValue(media.MediaId, out var location))
            throw new InvalidOperationException($"鏈壘鍒板獟浣撳畾浣? {media.MediaId}");

        var ms = new MemoryStream();
// dcId=0 简化：真实文件需按 TLConfig 提供正确的 DC id，此处在真实账号验证时校验。
        return conn.Client.DownloadFileAsync(location, ms, 0, 0, null).ContinueWith(t =>
        {
            ms.Position = 0;
            return (Stream)ms;
        }, ct);
    }

    public bool Supports(string capability) => capability switch
    {
        "full_client" => true,
        "anti_withdrawal" => true,
        "history_sync" => true,
        "media_download" => true,
        "send_text" => true,
        "send_image" => true,
        _ => false
    };

    // ---- 会话同步 / 历史拉取 ----

    /// <summary>
/// 登录成功后立即捕获会话快照（内存权威，防 AUTH_RESTART 清空文件）。
/// 业务层 AddFinalize 用此快照落库，保证 DB 里始终是已登录态。
/// 快照格式 = SessionStore 文件头 8 字节, position 归一化为 8) + 加密会话块。
/// 注意：不能用 File.ReadAllBytes —— SessionStore 独占锁着文件句柄(FileShare.None)，
/// 必须通过句柄本身反射读取；且必须带上文件头，否则恢复时 SessionStore 会把
/// 加密块前 8 字节误读为 position/length（负数 position 直接抛异常）。
    /// </summary>
    private void CaptureSessionSnapshot(string accountId, AccountConnection conn, string sessionPath)
    {
        try
        {
            var clientType = conn.Client.GetType();
            var sessionField = clientType.GetField("_session",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var session = sessionField?.GetValue(conn.Client);
            if (session == null) return;

// 1) 反射调用内存 Session.Save() 写入最新状态。
            var saveMethod = session.GetType().GetMethod("Save",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            saveMethod?.Invoke(session, null);

// 2) 从 SessionStore 句柄直接读最新块（header: position[4] + length[4]）。
            var store = session.GetType().GetField("_store",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(session);
            if (store is FileStream fs)
            {
                var headerField = fs.GetType().GetField("_header",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var header = (byte[]?)headerField?.GetValue(fs) ?? [];
                var position = BitConverter.ToInt32(header, 0);
                var length = BitConverter.ToInt32(header, 4);
// 注意：SessionStore 重写了 Length（返回当前块长而非物理文件长），不能拿它校验。
                if (length is > 0 and < 4 * 1024 * 1024)
                {
                    fs.Seek(position, SeekOrigin.Begin);
                    var block = new byte[length];
                    var off = 0;
                    while (off < length)
                    {
                        var n = fs.Read(block, off, length - off);
                        if (n == 0) break;
                        off += n;
                    }
                    if (off != length)
                    {
_logger.LogWarning("会话快照读取不完整: account={AccountId} got={Got} want={Want}", accountId, off, length);
                        return;
                    }
                    // 蹇収 = 褰掍竴鍖栨枃浠跺ご(position=8) + 鍔犲瘑鍧?
                    var snapshot = new byte[8 + length];
                    BinaryPrimitives.WriteInt32LittleEndian(snapshot.AsSpan(0), 8);
                    BinaryPrimitives.WriteInt32LittleEndian(snapshot.AsSpan(4), length);
                    Buffer.BlockCopy(block, 0, snapshot, 8, length);
                    conn.SessionSnapshot = snapshot;
                    _logger.LogInformation("宸叉崟鑾风櫥褰曚細璇濆揩鐓? account={AccountId} bytes={Len}", accountId, snapshot.Length);
                    return;
                }
            }
            _logger.LogWarning("会话快照读取失败（无有效块）: account={AccountId}", accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "鎹曡幏浼氳瘽蹇収澶辫触: account={AccountId}", accountId);
        }
    }

    /// <summary>
/// 强制把内存中的会话状态写盘（供业务层在认证完成后捕获凭据）。
/// 必要性：WTelegramClient 仅在部分路径保存会话文件，且收到 500 AUTH_RESTART 后
/// 会主动把 UserId 清零并保持 —— 文件里可能不是最新已登录状态，内存才是权威。
    /// </summary>
    public void FlushSessionToFile(AccountBag account)
    {
        if (account == null || !_connections.TryGetValue(account.AccountId, out var conn) || conn.Client == null)
            return;
        try
        {
            var clientType = conn.Client.GetType();
            var sessionField = clientType.GetField("_session",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var session = sessionField?.GetValue(conn.Client);
            var saveMethod = session?.GetType().GetMethod("Save",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            saveMethod?.Invoke(session, null);
            _logger.LogInformation("宸插己鍒跺啓鐩樹細璇? account={AccountId}", account.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "寮哄埗鍐欑洏浼氳瘽澶辫触: account={AccountId}", account.AccountId);
        }
    }

/// <summary>获取登录完成瞬间的会话快照（供 AddFinalize 落库；无则 null）。</summary>
    public byte[]? GetSessionSnapshot(string accountId)
    {
        return _connections.TryGetValue(accountId, out var conn) ? conn.SessionSnapshot : null;
    }

/// <summary>拉取真实会话列表（messages.getAllDialogs），并缓存 InputPeer 供历史拉取。</summary>
    public async Task<IReadOnlyList<DialogInfo>> SyncDialogsAsync(AccountBag account, CancellationToken ct)
    {
        if (account == null || !_connections.TryGetValue(account.AccountId, out var conn) || conn.Client == null)
            return [];

        var dialogs = await conn.Client.Messages_GetAllDialogs().ConfigureAwait(false);
        var result = new List<DialogInfo>();
        foreach (var d in dialogs.dialogs)
        {
            if (d is not Dialog dd) continue;
            var peerId = ResolvePeerId(dd.peer);
            if (string.IsNullOrEmpty(peerId)) continue;

            string? title = null;
            InputPeer? input = null;
            IPeerInfo? peerInfo = null;
// 注意：TL.Peer 定义了到 long 的隐式转换，switch 直接匹配会被转换劫持，需转 object
            switch ((object)dd.peer)
            {
                case PeerUser pu when dialogs.users?.TryGetValue(pu.user_id, out var u) == true:
                    title = UserDisplayName(u);
                    input = new InputPeerUser(pu.user_id, u.access_hash);
                    peerInfo = u;
                    break;
                case PeerChat pc when dialogs.chats?.TryGetValue(pc.chat_id, out var c) == true:
                    if (c is TL.Chat chat) { title = chat.title; input = new InputPeerChat(pc.chat_id); peerInfo = chat; }
                    break;
                case PeerChannel pch when dialogs.chats?.TryGetValue(pch.channel_id, out var c2) == true:
                    if (c2 is TL.Channel channel)
                    {
                        title = channel.title;
                        input = new InputPeerChannel(pch.channel_id, channel.access_hash);
                        peerInfo = channel;
                    }
                    break;
            }

            if (input != null)
                conn.PeerInputs[peerId] = input;

            DateTime? lastTime = null;
            if (dd.top_message != 0 && dialogs.messages != null)
            {
                var top = dialogs.messages.FirstOrDefault(m => m is Message msg && msg.id == dd.top_message);
                if (top is Message m) lastTime = DateTime.SpecifyKind(m.date, DateTimeKind.Utc);
                else if (top is MessageService ms) lastTime = DateTime.SpecifyKind(ms.date, DateTimeKind.Utc);
            }

            var avatarUrl = peerInfo == null ? null : await DownloadAvatarAsync(conn, account.AccountId, peerId, peerInfo, ct).ConfigureAwait(false);

            result.Add(new DialogInfo
            {
                PeerId = peerId,
                PeerType = dd.peer switch
                {
                    PeerUser => "User",
                    PeerChat => "Chat",
                    // 超级群组（megagroup）协议上是 Channel，但本质是群聊 → 归为 Chat
                    PeerChannel when peerInfo is TL.Channel ch && ch.flags.HasFlag(TL.Channel.Flags.megagroup) => "Chat",
                    PeerChannel => "Channel",
                    _ => "User",
                },
                Title = title,
                LastMessageTime = lastTime,
                UnreadCount = dd.unread_count,
                AvatarUrl = avatarUrl,
            });
        }
        return result;
    }

/// <summary>下载会话头像到本地 wwwroot（失败跳过，不影响列表）。</summary>
    /// <summary>下载群聊发送者头像（带缓存，避免每条消息重复下载）。</summary>
    private async Task<string?> DownloadSenderAvatarAsync(AccountConnection conn, string accountId, string senderId, User user, CancellationToken ct)
    {
        if (conn.SenderAvatars.TryGetValue(senderId, out var cached)) return cached;
        var url = await DownloadAvatarAsync(conn, accountId, $"u{senderId}", user, ct).ConfigureAwait(false);
        conn.SenderAvatars[senderId] = url;
        return url;
    }

    private static bool IsGroup(Peer peer) => (object)peer is PeerChat or PeerChannel;

    private async Task<string?> DownloadAvatarAsync(AccountConnection conn, string accountId, string peerId,
        IPeerInfo peerInfo, CancellationToken ct)
    {
        try
        {
            var relativeUrl = $"/storage/chat/{accountId}/avatars/{peerId}.jpg";
            var fullPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "storage", "chat", accountId, "avatars", $"{peerId}.jpg");
            if (File.Exists(fullPath))
                return relativeUrl;

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8)); // 头像下载超时，避免拖慢更新流
            var fileType = await conn.Client.DownloadProfilePhotoAsync(peerInfo, fs, false, false).ConfigureAwait(false);
            if (fileType == 0)
            {
                try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { }
                return null;
            }
            _logger.LogDebug("头像已下载: account={AccountId} peer={PeerId}", accountId, peerId);
            return relativeUrl;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "头像下载失败: account={AccountId} peer={PeerId}", accountId, peerId);
            return null;
        }
    }

    /// <summary>
    /// 按 TG 消息 id 定向获取并下载媒体（历史补偿：已入库但缺 localUrl 的媒体消息）。
    /// 返回带 LocalUrl 的归一化消息；无媒体/失败返回 null。
    /// </summary>
    public async Task<TelegramRequestMessageBase?> FetchAndDownloadMediaAsync(
        AccountBag account, string peerId, int messageId, CancellationToken ct = default)
    {
        if (account == null || !_connections.TryGetValue(account.AccountId, out var conn) || conn.Client == null)
            return null;
        try
        {
            var peer = await ResolveInputPeerAsync(conn, peerId, ct).ConfigureAwait(false);
            if (peer == null) return null;
            var freshBase = await conn.Client.GetMessages(peer,
                new InputMessageID[] { new InputMessageID { id = messageId } }).ConfigureAwait(false);
            var msg = freshBase is TL.Messages_Messages fmm
                ? fmm.messages.OfType<Message>().FirstOrDefault(m => m.id == messageId)
                : null;
            if (msg?.media == null) return null;

            var unified = NormalizeMessage(account.AccountId, msg);
            if (unified == null) return null;
            var (mediaCategory, doc) = ClassifyMedia(msg.media);
            if (mediaCategory != null)
                await TryAutoDownloadAsync(account.AccountId, conn, msg, unified, mediaCategory, doc, ct).ConfigureAwait(false);
            return unified;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "媒体补偿下载失败: account={AccountId} peer={PeerId} msg={MsgId}", account.AccountId, peerId, messageId);
            return null;
        }
    }

/// <summary>拉取指定会话的最近历史消息（messages.getHistory）。</summary>
    public async Task<IReadOnlyList<TelegramRequestMessageBase>> FetchHistoryAsync(
        AccountBag account, string peerId, int limit, CancellationToken ct)
    {
        if (account == null || !_connections.TryGetValue(account.AccountId, out var conn) || conn.Client == null)
            return [];

        var peer = await ResolveInputPeerAsync(conn, peerId, ct).ConfigureAwait(false);
        if (peer == null)
        {
_logger.LogWarning("历史拉取失败：未找到 peer {PeerId}（请先同步会话）", peerId);
            return [];
        }

        var history = await TL.SchemaExtensions.Messages_GetHistory(
            conn.Client, peer, 0, default, 0, Math.Clamp(limit, 1, 200), 0, 0, 0).ConfigureAwait(false);
        _logger.LogInformation("getHistory 返回: {AccountId} {PeerId} = {Type}", account.AccountId, peerId, history?.GetType().Name);
        try
        {
            var ids = new List<long>();
            if (history is TL.Messages_Messages mmX) foreach (var m in mmX.messages.OfType<Message>()) ids.Add(m.id);
            else if (history is TL.Messages_ChannelMessages cmX) foreach (var m in cmX.messages.OfType<Message>()) ids.Add(m.id);
            _logger.LogInformation("getHistory ids: {AccountId} {PeerId} count={Count} ids=[{Ids}]",
                account.AccountId, peerId, ids.Count, string.Join(",", ids.Take(15)));
        }
        catch { }

        // 频道/超级群：触发 getChannelDifference 建立/同步该 channel 的消息更新订阅（否则实时 UpdateNewChannelMessage 收不到）
        if (peer is TL.InputPeerChannel ipc)
        {
            try
            {
                var ic = new TL.InputChannel(ipc.channel_id, ipc.access_hash);
                var cd = await conn.Client.Updates_GetChannelDifference(
                    ic, new TL.ChannelMessagesFilter(), 0, 100, true).ConfigureAwait(false);
                var newMsgs = cd switch
                {
                    TL.Updates_ChannelDifference d => d.new_messages?.Length,
                    TL.Updates_ChannelDifferenceTooLong d2 => d2.messages?.Length,
                    _ => null,
                };
                _logger.LogInformation("getChannelDifference: {AccountId} {PeerId} type={Type} newMsgs={Count}",
                    account.AccountId, peerId, cd?.GetType().Name, newMsgs);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("getChannelDifference 失败(pts 不可用,忽略): {AccountId} {PeerId} {Err}",
                    account.AccountId, peerId, ex.Message);
            }
        }

        var result = new List<TelegramRequestMessageBase>();
        MessageBase[] rawMessages;
        IDictionary<long, User>? hUsers = null;
        IDictionary<long, ChatBase>? hChats = null;
        if (history is TL.Messages_Messages mm) { rawMessages = mm.messages; hUsers = mm.users; hChats = mm.chats; }
        else if (history is TL.Messages_ChannelMessages cm) { rawMessages = cm.messages; hUsers = cm.users; hChats = cm.chats; }
        else rawMessages = Array.Empty<MessageBase>();
        foreach (var raw in rawMessages)
        {
            if (raw is not Message msg) continue;
            var unified = NormalizeMessage(account.AccountId, msg, hUsers, hChats);
            if (unified == null) continue;
            // 群聊：下载发送者头像（头像+昵称展示）
            if (unified.SenderId.Length > 0 && IsGroup(msg.peer_id) && hUsers != null
                && long.TryParse(unified.SenderId, out var senderUid) && hUsers.TryGetValue(senderUid, out var senderUser))
            {
                unified.SenderAvatar = await DownloadSenderAvatarAsync(conn, account.AccountId, unified.SenderId, senderUser, ct).ConfigureAwait(false);
            }
            // 历史媒体同步下载（防撤回存档 + 前端可直接播放；遵循群聊/私聊自动下载策略）
            if (msg.media != null)
            {
                var (mediaCategory, doc) = ClassifyMedia(msg.media);
                if (mediaCategory != null)
                    await TryAutoDownloadAsync(account.AccountId, conn, msg, unified, mediaCategory, doc, ct,
                        IsGroup(msg.peer_id) ? "Group" : "Private").ConfigureAwait(false);
            }
            result.Add(unified);
        }
        return result;
    }

    private InputPeer? ResolveInputPeer(AccountConnection conn, string peerId)
    {
        if (conn.PeerInputs.TryGetValue(peerId, out var cached))
            return cached;
        return InputPeerFromId(peerId);   // 兜底：无 access_hash（可能被拒，由业务层提示先同步会话）
    }

    /// <summary>
/// 缓存未命中时实时拉会话列表刷新缓存再解析。
/// 自动同步（含头像下载）较慢，用户可能立即点击会话导致缓存未就绪。
    /// </summary>
    private async Task<InputPeer?> ResolveInputPeerAsync(AccountConnection conn, string peerId, CancellationToken ct)
    {
        if (conn.PeerInputs.TryGetValue(peerId, out var cached))
            return cached;
        try
        {
            var dialogs = await conn.Client.Messages_GetAllDialogs().ConfigureAwait(false);
            PopulatePeerInputs(conn, dialogs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "实时解析 peer 缓存失败: {PeerId}", peerId);
        }
        return conn.PeerInputs.TryGetValue(peerId, out var p) ? p : null;
    }

/// <summary>从 dialogs 响应填充 peer → InputPeer 缓存（含 access_hash）。</summary>
    private static void PopulatePeerInputs(AccountConnection conn, Messages_Dialogs dialogs)
    {
        foreach (var d in dialogs.dialogs)
        {
            if (d is not Dialog dd) continue;
            var peerId = ResolvePeerId(dd.peer);
            if (string.IsNullOrEmpty(peerId)) continue;
            InputPeer? input = null;
            // 注意：TL.Peer 定义了到 long 的隐式转换，switch 直接匹配会被转换劫持，需转 object
            switch ((object)dd.peer)
            {
                case PeerUser pu when dialogs.users?.TryGetValue(pu.user_id, out var u) == true:
                    input = new InputPeerUser(pu.user_id, u.access_hash);
                    break;
                case PeerChat pc when dialogs.chats?.TryGetValue(pc.chat_id, out var c) == true:
                    if (c is TL.Chat) input = new InputPeerChat(pc.chat_id);
                    break;
                case PeerChannel pch when dialogs.chats?.TryGetValue(pch.channel_id, out var c2) == true:
                    if (c2 is TL.Channel channel)
                        input = new InputPeerChannel(pch.channel_id, channel.access_hash);
                    break;
            }
            if (input != null)
                conn.PeerInputs[peerId] = input;
        }
    }

    private static string UserDisplayName(User u)
    {
        var name = string.IsNullOrWhiteSpace(u.first_name) ? "" : u.first_name;
        if (!string.IsNullOrWhiteSpace(u.last_name)) name += u.last_name;
        return string.IsNullOrWhiteSpace(name) ? (u.username ?? $"@{u.id}") : name;
    }

    // ---- 内部实现 ----

    private static string ResolveAccountIdFor(TelegramResponseMessageBase response)
        => response.PeerId?.Split(':', 2)[0] ?? "";

    private async Task ConnectInternalAsync(AccountBag account, AccountConnection conn, CancellationToken ct)
    {
        try
        {
            var sessionDir = Path.Combine(AppContext.BaseDirectory, "chat_sessions");
            Directory.CreateDirectory(sessionDir);
            var sessionPath = Path.Combine(sessionDir, $"{account.AccountId}.session");

            conn.Client = await CreateClientAsync(account, conn, sessionPath, ct).ConfigureAwait(false);

            // 会话恢复 / 登录
            var user = await conn.Client.LoginUserIfNeeded().ConfigureAwait(false);
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Ready);
_logger.LogInformation("个人号登录成功: {AccountId} @ {User}",
                account.AccountId, SecurityMask(user.username ?? user.first_name ?? user.id.ToString()));
            CaptureSessionSnapshot(account.AccountId, conn, sessionPath);

            // 历史补拉占位：完整断点续传由业务层按 ChatSyncState + 上游 API 驱动
_logger.LogDebug("历史补拉由业务层驱动（ChatSyncState） {AccountId}", account.AccountId);
        }
        catch (Exception ex)
        {
_logger.LogError(ex, "个人号连接失败: {AccountId}", account.AccountId);
            account.LastError = FriendlyConnectError(ex);
            DisposeConnection(account.AccountId, conn);
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Invalidated);
        }
    }

    /// <summary>
    /// 将底层网络异常转换为用户可读的友好错误信息（写入 LastError 供上层透出给前端）。
    /// </summary>
    private static string FriendlyConnectError(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SocketException se)
                return FriendlySocketError(se);
        }

        var msg = ex.Message ?? "";
        var lower = msg.ToLowerInvariant();
        if (ex is TimeoutException || lower.Contains("timed out") || lower.Contains("timeout"))
            return "连接 Telegram 服务器超时，请确认系统的网络可以连接 Telegram 服务器，并确保代理配置正确";
        if (lower.Contains("connection") || lower.Contains("proxy") || lower.Contains("network"))
            return "无法连接 Telegram 服务器，请确认系统的网络可以连接 Telegram 服务器，并确保代理配置正确";
        return msg;
    }

    private static string FriendlySocketError(SocketException se) => se.SocketErrorCode switch
    {
        SocketError.TimedOut => "连接 Telegram 服务器超时，请确认系统的网络可以连接 Telegram 服务器，并确保代理配置正确",
        SocketError.ConnectionRefused => "连接被拒绝，请确认系统的网络可以连接 Telegram 服务器，并确保代理配置正确",
        SocketError.HostUnreachable or SocketError.NetworkUnreachable => "网络不可达，请确认系统的网络可以连接 Telegram 服务器，并确保代理配置正确",
        _ => $"网络异常（{se.SocketErrorCode}），请确认系统的网络可以连接 Telegram 服务器，并确保代理配置正确"
    };

/// <summary>释放连接资源并从注册表移除（失败后不留残留句柄/连接）。</summary>
    private void DisposeConnection(string accountId, AccountConnection conn)
    {
        conn.Cts.Cancel();
        _ = DisposeClientAsync(conn.Client, accountId);
        try { conn.ProxyTcpClient?.Dispose(); } catch { }
        _connections.TryRemove(accountId, out _);
    }

/// <summary>创建 WTelegramClient（含代理隧道与认证桥），并挂载 Update 处理。</summary>
    private async Task<WTelegram.Client> CreateClientAsync(AccountBag account, AccountConnection conn,
        string sessionPath, CancellationToken ct)
    {
                // 认证桥：同步 ConfigProvider → 异步 IAuthProvider
        var bridge = new AuthBridge(_authProvider, account.AccountId, _logger);

        // 代理隧道：通过 WTelegramClient 的 TcpHandler 挂钩，所有 DC 连接（含扫码登录的 DC 迁移）
        // 都经由 SOCKS5/HTTP 代理建立。注意：不能把代理流传给 Client 构造函数的 sessionStore 参数。
        var client = new WTelegram.Client(what => bridge.ConfigProvider(what, sessionPath, account, _options));
        if (_options.MtProto.Proxy.Enabled)
        {
            _logger.LogInformation("通过代理连接 Telegram: {Type} {Host}:{Port}",
                _options.MtProto.Proxy.Type, _options.MtProto.Proxy.Host, _options.MtProto.Proxy.Port);
            var proxy = _options.MtProto.Proxy;
            client.TcpHandler = async (host, port) =>
            {
                var tcp = await ProxyTcpClientFactory.CreateAsync(proxy, host, port, ct);
                // 挂到连接上：断开时随连接释放，防止代理 socket 泄漏（DC 迁移会多次创建，旧引用-best effort 释放）
                var old = conn.ProxyTcpClient;
                conn.ProxyTcpClient = tcp;
                try { old?.Dispose(); } catch { }
                return tcp;
            };
        }
client.OnUpdates += updates => HandleUpdatesAsync(account.AccountId, conn, updates);
        // 接入 WTelegramClient 连接日志（断开/重连诊断；Helpers.Log 为全局静态委托）
        WTelegram.Helpers.Log = (lvl, msg) => _logger.Log(lvl <= 1 ? LogLevel.Debug : LogLevel.Information,
            "TG[{AccountId}] {Msg}", account.AccountId, msg);
        return client;
    }

    private async Task HandleUpdatesAsync(string accountId, AccountConnection conn, UpdatesBase updates)
    {
        // 诊断：打印本批更新类型（确认 channel 更新是否到达）
        _logger.LogInformation("Updates 到达: {AccountId} 类型={Types}", accountId,
            string.Join(",", updates.UpdateList.Select(u => u.GetType().Name).Distinct()));
        // 收集本批 updates 中消息 id → 是否自己发送（用于判断"回复了我的消息"）
        var msgOutMap = new Dictionary<long, bool>();
        foreach (var u in updates.UpdateList)
        {
            if (u is UpdateNewMessage unm2 && unm2.message is Message m1) msgOutMap[m1.id] = (m1.flags & Message.Flags.out_) != 0;
            else if (u is UpdateNewChannelMessage uncm2 && uncm2.message is Message m2) msgOutMap[m2.id] = (m2.flags & Message.Flags.out_) != 0;
        }

        foreach (var update in updates.UpdateList)
        {
            try
            {
                if (update is UpdateNewMessage unm && unm.message is Message msg)
                {
                    await ProcessMessageAsync(accountId, conn, msg, updates.Users, msgOutMap).ConfigureAwait(false);
                }
                else if (update is UpdateNewChannelMessage uncm && uncm.message is Message channelMsg)
                {
                    await ProcessMessageAsync(accountId, conn, channelMsg, updates.Users, msgOutMap).ConfigureAwait(false);
                }
                else if (update is UpdateDeleteMessages del)
                {
// 防撤回：内容已入库（收到即存），此处仅发撤回信号（内容保留）。
                    conn.Inbox.Writer.TryWrite(new RequestMessageService
                    {
                        AccountId = accountId,
                        PeerId = "",
                        Direction = MessageDirection.Incoming,
                        Timestamp = SB.Framework.Core.Util.AppTime.UtcNow,
                        ServiceType = ServiceMessageType.MessagesWithdrawn,
                        MessageIds = del.messages?.Select(m => (long)m).ToList() ?? new(),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "澶勭悊 Update 澶辫触: {AccountId}", accountId);
            }
        }
    }

    private async Task ProcessMessageAsync(string accountId, AccountConnection conn, Message msg, IDictionary<long, User> users, Dictionary<long, bool> msgOutMap = null)
    {
        var peerId = ResolvePeerId(msg.peer_id);
        var direction = (msg.flags & Message.Flags.out_) != 0
            ? MessageDirection.Outgoing
            : MessageDirection.Incoming;
        var ts = DateTime.SpecifyKind(msg.date, DateTimeKind.Utc);

        var unified = NormalizeMessage(accountId, msg, users);
        // 是否直接回复（引用）了自己的消息
        if (unified != null && msg.reply_to is MessageReplyHeader r)
        {
            if (msgOutMap != null && msgOutMap.TryGetValue(r.reply_to_msg_id, out var repliedOut))
                unified.IsReplyToMine = repliedOut;
        }
        if (unified == null) return;
        // 群聊：发送者头像后台下载（不阻塞更新流；前端按需加载，缺省时显示占位）
        if (unified.SenderId.Length > 0 && IsGroup(msg.peer_id) && users != null
            && long.TryParse(unified.SenderId, out var senderUid) && users.TryGetValue(senderUid, out var senderUser))
        {
            if (conn.SenderAvatars.TryGetValue(unified.SenderId, out var cachedAvatar) && !string.IsNullOrEmpty(cachedAvatar))
            {
                unified.SenderAvatar = cachedAvatar;
            }
            else
            {
                unified.SenderAvatar = await DownloadSenderAvatarAsync(conn, accountId, unified.SenderId, senderUser, conn.Cts.Token).ConfigureAwait(false);
            }
        }

// 防撤回核心：媒体到达时立即下载（在推入 inbox 之前完成，确保撤回前已落盘）
        if (msg.media != null)
        {
            var (mediaCategory, doc) = ClassifyMedia(msg.media);
            if (mediaCategory != null)
                await TryAutoDownloadAsync(accountId, conn, msg, unified, mediaCategory, doc, conn.Cts.Token,
                    IsGroup(msg.peer_id) ? "Group" : "Private").ConfigureAwait(false);
        }

        CacheFileLocation(accountId, conn, msg);
        conn.Inbox.Writer.TryWrite(unified);
    }

/// <summary>将 TL Message 归一化为统一消息模型（更新流与历史拉取共用）。</summary>
    private static TelegramRequestMessageBase? NormalizeMessage(string accountId, Message msg,
        IDictionary<long, User>? users = null, IDictionary<long, ChatBase>? chats = null)
    {
        var peerId = ResolvePeerId(msg.peer_id);
        var direction = (msg.flags & Message.Flags.out_) != 0
            ? MessageDirection.Outgoing
            : MessageDirection.Incoming;
        var ts = DateTime.SpecifyKind(msg.date, DateTimeKind.Utc);
        var (senderId, senderName) = ResolveSender(msg, users, chats);

        var (mediaCategory, doc) = ClassifyMedia(msg.media);

        TelegramRequestMessageBase? unified;
        if (mediaCategory == "image")
        {
            unified = ToImageMessage(accountId, peerId, msg, ts, direction, msg.media as MessageMediaPhoto, doc);
        }
        else if (mediaCategory is "video" or "voice" or "file" or "sticker" && doc is not null)
        {
            unified = ToFileMessage(accountId, peerId, msg, ts, direction, mediaCategory, doc);
        }
        else if (string.IsNullOrEmpty(msg.message))
        {
            unified = null;
        }
        else
        {
            unified = new RequestMessageText
            {
                AccountId = accountId,
                PeerId = peerId,
                MessageId = msg.id,
                Direction = direction,
                Timestamp = ts,
                Content = msg.message ?? "",
                ReplyToMessageId = msg.reply_to is MessageReplyHeader r ? r.reply_to_msg_id.ToString() : null,
            };
        }

        if (unified != null)
        if (unified != null)
        {
            unified.SenderId = senderId;
            unified.SenderName = senderName;
            unified.IsMention = (msg.flags & Message.Flags.mentioned) != 0;
        }
        return unified;
    }

    private static (string Id, string Name) ResolveSender(Message msg,
        IDictionary<long, User>? users, IDictionary<long, ChatBase>? chats)
    {
        if (msg.from_id is PeerUser fromUser && users?.TryGetValue(fromUser.user_id, out var u) == true)
            return (fromUser.user_id.ToString(), UserDisplayName(u));
        if (msg.from_id is PeerChannel fromCh && chats?.TryGetValue(fromCh.channel_id, out var cb) == true)
            return (fromCh.channel_id.ToString(), (cb as TL.Channel)?.title ?? "");
        if (msg.peer_id is PeerUser pu && users?.TryGetValue(pu.user_id, out var u2) == true)
            return (pu.user_id.ToString(), UserDisplayName(u2));
        return ("", "");
    }

/// <summary>分类 Telegram 媒体类型到 TgVault 统一分类。</summary>
    private static (string? category, Document? doc) ClassifyMedia(MessageMedia? media)
    {
        return media switch
        {
            MessageMediaPhoto => ("image", null),
            MessageMediaDocument { document: Document d } when d.mime_type?.StartsWith("image/") == true
                => ("image", d),
            MessageMediaDocument { document: Document d } when d.mime_type?.StartsWith("video/") == true
                => ("video", d),
            MessageMediaDocument { document: Document d } when d.mime_type?.StartsWith("audio/") == true
                => ("voice", d),
            MessageMediaDocument { document: Document d } when IsSticker(d)
                => ("sticker", d),
            MessageMediaDocument { document: Document d }
                => ("file", d),
            _ => (null, null),
        };
    }

    private static bool IsSticker(Document d)
        => d.attributes?.Any(a => a is DocumentAttributeSticker) == true;

    private static TelegramRequestMessageBase? ToImageMessage(
        string accountId, string peerId, Message msg, DateTime ts, MessageDirection direction,
        MessageMediaPhoto? photo, Document? doc = null)
    {
        var p = photo?.photo as Photo;
        return new RequestMessageImage
        {
            AccountId = accountId,
            PeerId = peerId,
            MessageId = msg.id,
            Direction = direction,
            Timestamp = ts,
            Caption = msg.message,
            ReplyToMessageId = msg.reply_to is MessageReplyHeader r ? r.reply_to_msg_id.ToString() : null,
            Media = new ImageMedia
            {
                MediaId = msg.id.ToString(),
                Size = p?.sizes?.OfType<PhotoSize>().LastOrDefault()?.size ?? doc?.size ?? 0,
                MimeType = photo != null ? "image/jpeg" : (doc?.mime_type ?? "image"),
                FileName = GetDocumentFilename(doc),
            },
        };
    }

    private static TelegramRequestMessageBase ToFileMessage(
        string accountId, string peerId, Message msg, DateTime ts, MessageDirection direction,
        string category, Document doc)
    {
        MediaBase media = category switch
        {
            "video" => new VideoMedia(),
            "voice" => new VoiceMedia(),
            "sticker" => new StickerMedia(),
            _ => new FileMedia(),
        };
        media.MediaId = msg.id.ToString();
        media.Size = doc.size;
        media.MimeType = doc.mime_type ?? "application/octet-stream";
        media.FileName = GetDocumentFilename(doc);

        return new RequestMessageFile
        {
            AccountId = accountId,
            PeerId = peerId,
            MessageId = msg.id,
            Direction = direction,
            Timestamp = ts,
            FileCategory = category,
            Caption = msg.message,
            ReplyToMessageId = msg.reply_to is MessageReplyHeader r ? r.reply_to_msg_id.ToString() : null,
            Media = media,
        };
    }

/// <summary>从 Document.attributes 提取原始文件名。</summary>
    private static string? GetDocumentFilename(Document? doc)
    {
        if (doc?.attributes == null) return null;
        var attr = doc.attributes.OfType<DocumentAttributeFilename>().FirstOrDefault();
        return attr?.file_name;
    }

/// <summary>缓存消息媒体定位（防撤回：撤回前下载用）。</summary>
    private void CacheFileLocation(string accountId, AccountConnection conn, Message msg)
    {
        try
        {
            InputFileLocationBase? location = msg.media switch
            {
                MessageMediaPhoto { photo: Photo p } => new InputPhotoFileLocation
                {
                    id = p.id,
                    access_hash = p.access_hash,
                    file_reference = p.file_reference,
                },
                MessageMediaDocument { document: Document d } => new InputDocumentFileLocation
                {
                    id = d.id,
                    access_hash = d.access_hash,
                    file_reference = d.file_reference,
                },
                _ => null,
            };
            if (location != null)
                conn.FileLocations[msg.id.ToString()] = location;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "缓存文件定位失败: {AccountId} msg={Id}", accountId, msg.id);
        }
    }

    // ---- 防撤回：入站媒体自动下载 ----

/// <summary>根据配置判断是否应自动下载该媒体。</summary>
    private bool ShouldAutoDownload(string accountId, string category, Document? doc, string? peerType = null)
    {
        var cfg = _options.MtProto.AutoDownload;
        if (!cfg.Enabled) return false;

        // 群聊/私聊自动下载策略（宿主动态控制；无提供者时群聊默认不自动下载）
        if (_mediaPolicy != null)
        {
            if (peerType != null && !_mediaPolicy.AllowAutoDownload(accountId, peerType, category))
                return false;
        }
        else if (peerType == "Group")
        {
            _logger.LogDebug("跳过自动下载：群聊默认手动下载（未配置策略提供者）");
            return false;
        }

        var allowed = category switch
        {
            "image" => cfg.DownloadImages,
            "video" => cfg.DownloadVideos,
            "voice" => cfg.DownloadVoice,
            "file" => cfg.DownloadFiles,
            "sticker" => cfg.DownloadStickers,
            _ => false,
        };
        if (!allowed) return false;

// 文件大小限制（doc.size <= 0 时视为未知，不拦截）
        var maxBytes = cfg.MaxFileSizeMB * 1024L * 1024L;
        if (doc?.size > 0 && doc.size > maxBytes)
        {
_logger.LogDebug("跳过自动下载：文件超限 {Size} > {Max}MB", doc.size, cfg.MaxFileSizeMB);
            return false;
        }

// 扩展名过滤
        var ext = GetFileExtension(doc);
        if (!string.IsNullOrEmpty(ext))
        {
            if (cfg.BlockedExtensions.Count > 0
                && cfg.BlockedExtensions.Any(b => string.Equals(b.Trim(), ext, StringComparison.OrdinalIgnoreCase)))
            {
_logger.LogDebug("跳过自动下载：扩展名被禁用 {Ext}", ext);
                return false;
            }
            if (cfg.AllowedExtensions.Count > 0
                && !cfg.AllowedExtensions.Any(a => string.Equals(a.Trim(), ext, StringComparison.OrdinalIgnoreCase)))
            {
_logger.LogDebug("跳过自动下载：扩展名不在白名单 {Ext}", ext);
                return false;
            }
        }

        return true;
    }

    private static string GetFileExtension(Document? doc)
    {
        if (doc?.mime_type == null) return "";
        var idx = doc.mime_type.IndexOf('/');
        return idx >= 0 ? $".{doc.mime_type[(idx + 1)..]}" : "";
    }

    /// <summary>
/// 下载入站媒体到服务器本地，设置 LocalUrl（防撤回：撤回后仍可访问）。
/// 下载失败不阻塞消息流转（消息照常入库，LocalUrl 为空表示未捕获）。
    /// </summary>
    private async Task TryAutoDownloadAsync(
        string accountId, AccountConnection conn, Message msg, TelegramRequestMessageBase unified,
        string category, Document? doc, CancellationToken ct = default, string? peerType = null)
    {
        if (!ShouldAutoDownload(accountId, category, doc, peerType)) return;

        var fileName = ResolveOutputFileName(msg.id, category, doc);
        var relativeUrl = $"/storage/chat/{accountId}/{fileName}";
        var fullPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "storage", "chat", accountId, fileName);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var fileType = await DownloadMediaAsync(conn, msg, fs, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(fileType))
                {
                    _logger.LogWarning("media download returned empty type: account={AccountId} msg={MsgId}", accountId, msg.id);
                    break;
                }
                fs.Flush();
                SetLocalUrl(unified, relativeUrl);
                _logger.LogInformation("auto download ok: account={AccountId} msg={MsgId} type={Type} url={Url}",
                    accountId, msg.id, category, relativeUrl);
                return;
            }
            catch (TL.RpcException ex) when (attempt == 0 && ex.Message == "FILE_REFERENCE_EXPIRED")
            {
                _logger.LogDebug("file reference expired, refreshing: account={AccountId} msg={MsgId}", accountId, msg.id);
                try
                {
                    var refreshPeer = await ResolveInputPeerAsync(conn, ResolvePeerId(msg.peer_id), ct).ConfigureAwait(false);
                    if (refreshPeer == null) break;
                    var freshBase = await conn.Client.GetMessages(refreshPeer,
                        new InputMessageID[] { new InputMessageID { id = msg.id } }).ConfigureAwait(false);
                    var freshMsg = freshBase is TL.Messages_Messages fmm
                        ? fmm.messages.OfType<Message>().FirstOrDefault(m => m.id == msg.id)
                        : null;
                    if (freshMsg?.media != null)
                    {
                        msg.media = freshMsg.media;
                        continue;
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "auto download failed: account={AccountId} msg={MsgId} type={Type}",
                    accountId, msg.id, category);
                break;
            }
        }
        try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { }
    }

    private static async Task<string?> DownloadMediaAsync(
        AccountConnection conn, Message msg, Stream output, CancellationToken ct)
    {
        switch (msg.media)
        {
            case MessageMediaPhoto mp when mp.photo is Photo p:
                var photoSize = p.sizes?.OfType<PhotoSize>().LastOrDefault();
                var ft = await conn.Client.DownloadFileAsync(p, output, photoSize).ConfigureAwait(false);
                return ft.ToString();
            case MessageMediaDocument md when md.document is Document d:
                return await conn.Client.DownloadFileAsync(d, output).ConfigureAwait(false);
            default:
                return null;
        }
    }

    private static string ResolveOutputFileName(long msgId, string category, Document? doc)
    {
        var rawName = GetDocumentFilename(doc);
        var ext = Path.GetExtension(rawName);
        if (string.IsNullOrEmpty(ext))
            ext = GetFileExtension(doc);

        var safeName = string.IsNullOrEmpty(rawName) ? category : Path.GetFileNameWithoutExtension(rawName);
// 清理非法文件名字符
        var invalid = Path.GetInvalidFileNameChars();
        safeName = new string(safeName.Where(c => !invalid.Contains(c)).ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = category;

        return $"{msgId}_{safeName}{ext}";
    }

    private static void SetLocalUrl(TelegramRequestMessageBase unified, string relativeUrl)
    {
        switch (unified)
        {
            case RequestMessageImage img when img.Media != null:
                img.Media.LocalUrl = relativeUrl;
                break;
            case RequestMessageFile f when f.Media != null:
                f.Media.LocalUrl = relativeUrl;
                break;
        }
    }

    private static string ResolvePeerId(object peer) => peer switch
    {
        PeerUser pu => pu.user_id.ToString(),
        PeerChat pc => pc.chat_id.ToString(),
        PeerChannel pch => pch.channel_id.ToString(),
        _ => ""
    };

    private static string SecurityMask(string value)
        => value.Length <= 8 ? new string('*', value.Length) : value[..2] + "****" + value[^2..];

    private async Task SendTextAsync(AccountConnection conn, string accountId, ResponseMessageText text, CancellationToken ct)
    {
        var (_, peerId) = SplitPeer(text.PeerId ?? "");
        var sent = await conn.Client.SendMessageAsync(
            ResolveSendPeer(conn, peerId),
            text.Content,
            null, 0, null, default, default).ConfigureAwait(false);
        await ProcessSentMessageAsync(accountId, conn, sent).ConfigureAwait(false);
    }

    private async Task SendImageAsync(AccountConnection conn, string accountId, ResponseMessageImage image, CancellationToken ct)
    {
        var (_, peerId) = SplitPeer(image.PeerId ?? "");
        if (image.Media?.LocalUrl == null)
            throw new InvalidOperationException("image reply requires LocalUrl");

        var fullPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", image.Media.LocalUrl.TrimStart('/'));
        await using var fs = File.OpenRead(fullPath);
        var inputFile = await conn.Client.UploadFileAsync(fs, image.Media.LocalUrl, null).ConfigureAwait(false);
        var sent = await conn.Client.SendMediaAsync(
            ResolveSendPeer(conn, peerId),
            "",
            inputFile,
            null, 0, null, default).ConfigureAwait(false);
        await ProcessSentMessageAsync(accountId, conn, sent).ConfigureAwait(false);
    }

    /// <summary>
    /// 发送成功后处理 Telegram 确认回执：服务端不会再向同一会话推送自己发出的消息
    /// （SendMessageAsync 的 RPC 回执是唯一来源），不处理会导致本端发送的消息不落库/不广播，
    /// 前端仅剩乐观气泡，刷新或历史同步后“只显示对方的消息”。复用 ProcessMessageAsync 走
    /// 归一化 → inbox → 落库 → 广播 的完整链路；失败不影响发送结果（消息已发出）。
    /// </summary>
    private async Task ProcessSentMessageAsync(string accountId, AccountConnection conn, MessageBase? sent)
    {
        if (sent is not Message msg) return;
        try
        {
            await ProcessMessageAsync(accountId, conn, msg, null).ConfigureAwait(false);
            _logger.LogInformation("发送回执已回流: account={AccountId} peer={PeerId} msg={MsgId}",
                accountId, msg.peer_id, msg.id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "发送回执处理失败（消息已发出）: account={AccountId} msg={MsgId}", accountId, msg.id);
        }
    }

    private static InputPeer ResolveSendPeer(AccountConnection conn, string peerId)
    {
        if (conn.PeerInputs.TryGetValue(peerId, out var cached))
            return cached;
        return InputPeerFromId(peerId);
    }

    private static (string AccountId, string PeerId) SplitPeer(string peerId)
    {
        var idx = peerId.IndexOf(':');
        return idx >= 0
            ? (peerId[..idx], peerId[(idx + 1)..])
            : (peerId, "");
    }

    /// <summary>
/// 按字符串 peer id 构造 InputPeer。
/// ⚠️ 简化实现：直接按 user id 处理（access_hash=0 仅对已知 peer 可用）；
/// 真实场景需按 peer 解析 InputPeer（ResolveUsername / 存储映射）在业务层完善。
    /// </summary>
    private static InputPeer InputPeerFromId(string peerId)
    {
        if (long.TryParse(peerId, out var id) && id != 0)
            return new InputPeerUser(id, 0);
        throw new InvalidOperationException($"鏃犳硶瑙ｆ瀽 peer: '{peerId}'");
    }

    // ---- 扩展能力（资料/成员/在线状态） ----

    /// <summary>下载登录账号自己的头像（账号资料展示用；强制刷新：删除旧文件重新下载）。</summary>
    public async Task<string?> DownloadSelfAvatarAsync(AccountBag account, CancellationToken ct)
    {
        if (account == null || !_connections.TryGetValue(account.AccountId, out var conn) || conn.Client == null)
            return null;
        try
        {
            var users = await conn.Client.Users_GetUsers([new InputUserSelf()]).ConfigureAwait(false);
            if (users?.FirstOrDefault(u => u is User) is not User me) return null;
            // 强制刷新：TG 头像文件已存在时 DownloadAvatarAsync 会跳过，先删旧文件保证换头像后能更新
            var oldPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "storage", "chat", account.AccountId, "avatars", "self.jpg");
            try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
            return await DownloadAvatarAsync(conn, account.AccountId, "self", me, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "下载自身头像失败: {AccountId}", account.AccountId);
            return null;
        }
    }

    /// <summary>获取 peer 资料详情（用户/基础群/超级群与频道）。</summary>
    public async Task<PeerDetail?> GetPeerDetailAsync(AccountBag account, string peerId, CancellationToken ct)
    {
        if (account == null || !_connections.TryGetValue(account.AccountId, out var conn) || conn.Client == null)
            return null;
        var input = await ResolveInputPeerAsync(conn, peerId, ct).ConfigureAwait(false);
        if (input == null) return null;

        try
        {
            switch (input)
            {
                case InputPeerUser ipu:
                {
                    var users = await conn.Client.Users_GetUsers([new InputUser(ipu.user_id, ipu.access_hash)]).ConfigureAwait(false);
                    User? user = null;
                    if (users != null) { foreach (var x in users) { if (x is User uu) { user = uu; break; } } }
                    if (user == null) return null;
                    var (online, _, lastSeenText) = DescribeUserStatus(user);
                    var avatar = await DownloadAvatarAsync(conn, account.AccountId, peerId, user, ct).ConfigureAwait(false);
                    return new PeerDetail
                    {
                        PeerId = peerId,
                        PeerType = "User",
                        Name = UserDisplayName(user),
                        Username = user.username,
                        Phone = string.IsNullOrEmpty(user.phone) ? null : user.phone,
                        AvatarUrl = avatar,
                        IsOnline = online,
                        LastSeenText = lastSeenText,
                    };
                }
                case InputPeerChannel ipc:
                {
                    var full = await conn.Client.Channels_GetFullChannel(new InputChannel(ipc.channel_id, ipc.access_hash)).ConfigureAwait(false);
                    TL.Channel? channel = null;
                    if (full?.chats != null && full.chats.TryGetValue(ipc.channel_id, out var c) && c is TL.Channel ch)
                        channel = ch;
                    var avatar = channel == null ? null : await DownloadAvatarAsync(conn, account.AccountId, peerId, channel, ct).ConfigureAwait(false);
                    var channelFull = full?.full_chat as ChannelFull;
                    return new PeerDetail
                    {
                        PeerId = peerId,
                        PeerType = channel != null && channel.flags.HasFlag(TL.Channel.Flags.megagroup) ? "Chat" : "Channel",
                        Name = channel?.title,
                        Username = channel?.username,
                        AvatarUrl = avatar,
                        MembersCount = channelFull?.participants_count,
                        Bio = string.IsNullOrEmpty(channelFull?.about) ? null : channelFull.about,
                    };
                }
                case InputPeerChat ipchat:
                {
                    var full = await conn.Client.Messages_GetFullChat(ipchat.chat_id).ConfigureAwait(false);
                    TL.Chat? chat = null;
                    if (full?.chats != null && full.chats.TryGetValue(ipchat.chat_id, out var c2) && c2 is TL.Chat ch2)
                        chat = ch2;
                    var avatar = chat == null ? null : await DownloadAvatarAsync(conn, account.AccountId, peerId, chat, ct).ConfigureAwait(false);
                    var chatFull = full?.full_chat as ChatFull;
                    var membersCount = chatFull?.participants is ChatParticipants cps ? cps.participants.Count() : 0;
                    return new PeerDetail
                    {
                        PeerId = peerId,
                        PeerType = "Chat",
                        Name = chat?.title,
                        AvatarUrl = avatar,
                        MembersCount = membersCount > 0 ? membersCount : null,
                        Bio = string.IsNullOrEmpty(chatFull?.about) ? null : chatFull.about,
                    };
                }
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取 peer 资料失败: account={AccountId} peer={PeerId}", account.AccountId, peerId);
            return null;
        }
    }

    /// <summary>获取群/频道成员列表（offset 分页；基础群走 GetFullChat 全量，超级群/频道走 GetParticipants 分页）。</summary>
    public async Task<IReadOnlyList<PeerMember>> GetPeerMembersAsync(AccountBag account, string peerId, int offset, int limit, CancellationToken ct)
    {
        if (account == null || !_connections.TryGetValue(account.AccountId, out var conn) || conn.Client == null)
            return [];
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        var input = await ResolveInputPeerAsync(conn, peerId, ct).ConfigureAwait(false);
        if (input == null) return [];

        var list = new List<PeerMember>();
        try
        {
            switch (input)
            {
                case InputPeerChannel ipc:
                {
                    var parts = await conn.Client.Channels_GetParticipants(
                        new InputChannel(ipc.channel_id, ipc.access_hash),
                        new ChannelParticipantsRecent(), offset, limit, 0).ConfigureAwait(false);
                    if (parts?.users == null) return list;
                    foreach (var p in parts.participants ?? [])
                    {
                        var (userId, role) = DescribeChannelParticipant(p);
                        if (userId == null) continue;
                        parts.users.TryGetValue(long.Parse(userId), out var u);
                        list.Add(ToPeerMember(userId, u, role));
                    }
                    return list;
                }
                case InputPeerChat ipchat:
                {
                    var full = await conn.Client.Messages_GetFullChat(ipchat.chat_id).ConfigureAwait(false);
                    if (full?.full_chat is ChatFull cf && cf.participants is ChatParticipants cps)
                    {
                        foreach (var p in cps.participants ?? [])
                        {
                            var (userId, role) = DescribeChatParticipant(p);
                            if (userId == null) continue;
                            User? u = null;
                            if (full.users != null && full.users.TryGetValue(long.Parse(userId), out var found))
                                u = found;
                            list.Add(ToPeerMember(userId, u, role));
                        }
                    }
                    return list;
                }
                default:
                    return list;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取群成员失败: account={AccountId} peer={PeerId}", account.AccountId, peerId);
            return list;
        }
    }

    /// <summary>批量获取用户 peer 在线状态（仅缓存已命中的 peer；不触发全量 dialog 刷新）。</summary>
    public async Task<IReadOnlyList<PeerPresence>> GetUsersPresenceAsync(AccountBag account, IEnumerable<string> peerIds, CancellationToken ct)
    {
        if (account == null || peerIds == null || !_connections.TryGetValue(account.AccountId, out var conn) || conn.Client == null)
            return [];

        var wanted = new Dictionary<string, InputUser>();
        foreach (var peerId in peerIds.Distinct())
        {
            if (conn.PeerInputs.TryGetValue(peerId, out var p) && p is InputPeerUser ipu)
                wanted[peerId] = new InputUser(ipu.user_id, ipu.access_hash);
        }
        if (wanted.Count == 0) return [];

        try
        {
            var users = await conn.Client.Users_GetUsers(wanted.Values.ToArray()).ConfigureAwait(false);
            var byId = (users ?? []).OfType<User>().ToDictionary(u => u.id);
            var result = new List<PeerPresence>();
            foreach (var (peerId, inputUser) in wanted)
            {
                if (!byId.TryGetValue(inputUser.user_id, out var u)) continue;
                var (online, lastSeenUtc, lastSeenText) = DescribeUserStatus(u);
                result.Add(new PeerPresence { PeerId = peerId, IsOnline = online, LastSeenUtc = lastSeenUtc, LastSeenText = lastSeenText });
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "批量获取在线状态失败: account={AccountId} count={Count}", account.AccountId, wanted.Count);
            return [];
        }
    }

    /// <summary>解析 TG 用户在线状态（在线/离线精确时间/模糊时段）。</summary>
    private static (bool? Online, DateTime? LastSeenUtc, string? LastSeenText) DescribeUserStatus(User u)
        => u.status switch
        {
            UserStatusOnline => (true, null, null),
            UserStatusOffline off when off.was_online > 0 =>
                (false, DateTimeOffset.FromUnixTimeSeconds(off.was_online).UtcDateTime, null),
            UserStatusOffline => (false, null, null),
            UserStatusRecently => (false, null, "最近上线"),
            UserStatusLastWeek => (false, null, "一周内上线"),
            UserStatusLastMonth => (false, null, "一月内上线"),
            _ => (null, null, null),
        };

    private static (string? UserId, string Role) DescribeChannelParticipant(ChannelParticipantBase p) => p switch
    {
        ChannelParticipantCreator c => (c.user_id.ToString(), "owner"),
        ChannelParticipantAdmin a => (a.user_id.ToString(), "admin"),
        ChannelParticipantSelf s => (s.user_id.ToString(), "member"),
        ChannelParticipant cp => (cp.user_id.ToString(), "member"),
        ChannelParticipantBanned b when b.peer is PeerUser pu => (pu.user_id.ToString(), "member"),
        _ => (null, "member"),
    };

    private static (string? UserId, string Role) DescribeChatParticipant(ChatParticipantBase p) => p switch
    {
        ChatParticipantCreator c => (c.user_id.ToString(), "owner"),
        ChatParticipantAdmin a => (a.user_id.ToString(), "admin"),
        ChatParticipant cp => (cp.user_id.ToString(), "member"),
        _ => (null, "member"),
    };

    private static PeerMember ToPeerMember(string userId, User? u, string role)
    {
        var (online, _, lastSeenText) = u == null ? ((bool?)null, null, null) : DescribeUserStatus(u);
        return new PeerMember
        {
            UserId = userId,
            Name = u == null ? $"用户 {userId}" : UserDisplayName(u),
            Username = u?.username,
            IsOnline = online == true,
            LastSeenText = lastSeenText,
            Role = role,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var conn in _connections.Values)
        {
            conn.Cts.Cancel();
            conn.Client?.Dispose();
            conn.ProxyTcpClient?.Dispose();
        }
        _connections.Clear();
    }
}



