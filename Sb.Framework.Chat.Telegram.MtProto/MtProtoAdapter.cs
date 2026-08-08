using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Adapter;
using Sb.Framework.Chat.Model;
using TL;
// 别名消除与 TL.Channel 的命名冲突
using Channel = System.Threading.Channels.Channel;

namespace Sb.Framework.Chat.Telegram.MtProto;

/// <summary>
/// 个人号通道适配器（MTProto，WTelegramClient）。
/// 会话生命周期 / 认证流程 / Update 流 / 历史补拉 / 防撤回 / 媒体下载。
/// 一个适配器管理该类型下的全部个人号账号（≤100 单机可扛）。
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
    private readonly ConcurrentDictionary<string, AccountConnection> _connections = new();
    private bool _disposed;

    public AccountType SupportedAccountType => AccountType.Personal;

    public MtProtoAdapter(
        TelegramChatOptions options,
        IAuthProvider authProvider,
        ILogger<MtProtoAdapter> logger)
    {
        _options = options;
        _authProvider = authProvider;
        _logger = logger;
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
            conn.Client?.Dispose();
            conn.ProxyTcpClient?.Dispose();
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Disconnected);
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TelegramRequestMessageBase> PullAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // 合并所有账号的入站消息流
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
            ResponseMessageText text => SendTextAsync(conn, text, ct),
            ResponseMessageImage image => SendImageAsync(conn, image, ct),
            _ => throw new NotSupportedException($"个人号通道暂不支持响应类型 {response.GetType().Name}")
        };
    }

    public Task<Stream> DownloadMediaAsync(MediaReference media, CancellationToken ct)
    {
        if (!_connections.TryGetValue(media.AccountId, out var conn))
            throw new InvalidOperationException($"账号未连接: {media.AccountId}");

        // MediaId 记录为消息 id，从文件定位表取 InputFileLocationBase
        if (!conn.FileLocations.TryGetValue(media.MediaId, out var location))
            throw new InvalidOperationException($"未找到媒体定位: {media.MediaId}");

        var ms = new MemoryStream();
        // dcId=0 简化：真实文件需按 TLConfig 提供正确的 DC id，此处在真实账号验证时校正
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

            // 认证桥：同步 ConfigProvider ← 异步 IAuthProvider
            var bridge = new AuthBridge(_authProvider, account.AccountId, _logger);

            // 代理隧道：配置了代理时通过 SOCKS5/HTTP 隧道连接 Telegram DC
            Stream? proxyStream = null;
            if (_options.MtProto.Proxy.Enabled)
            {
                _logger.LogInformation("通过代理连接 Telegram: {Type} {Host}:{Port}",
                    _options.MtProto.Proxy.Type, _options.MtProto.Proxy.Host, _options.MtProto.Proxy.Port);
                conn.ProxyTcpClient = await ProxyTcpClientFactory.CreateAsync(_options.MtProto.Proxy, ct).ConfigureAwait(false);
                proxyStream = conn.ProxyTcpClient!.GetStream();
            }

            conn.Client = proxyStream != null
                ? new WTelegram.Client(what => bridge.ConfigProvider(what, sessionPath, account, _options), proxyStream)
                : new WTelegram.Client(what => bridge.ConfigProvider(what, sessionPath, account, _options));
            conn.Client.OnUpdates += updates => HandleUpdatesAsync(account.AccountId, conn, updates);

            // 会话恢复 / 登录
            var user = await conn.Client.LoginUserIfNeeded().ConfigureAwait(false);
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Ready);
            _logger.LogInformation("个人号登录成功: {AccountId} @ {User}",
                account.AccountId, SecurityMask(user.username ?? user.first_name ?? user.id.ToString()));

            // 历史补拉占位：完整断点续传由业务层按 ChatSyncState + 上游 API 驱动
            _logger.LogDebug("历史补拉由业务层驱动（ChatSyncState）: {AccountId}", account.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "个人号连接失败: {AccountId}", account.AccountId);
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Invalidated);
        }
    }

    private async Task HandleUpdatesAsync(string accountId, AccountConnection conn, UpdatesBase updates)
    {
        foreach (var update in updates.UpdateList)
        {
            try
            {
                if (update is UpdateNewMessage unm && unm.message is Message msg)
                {
                    await ProcessMessageAsync(accountId, conn, msg, updates.Users).ConfigureAwait(false);
                }
                else if (update is UpdateNewChannelMessage uncm && uncm.message is Message channelMsg)
                {
                    await ProcessMessageAsync(accountId, conn, channelMsg, updates.Users).ConfigureAwait(false);
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
                _logger.LogWarning(ex, "处理 Update 失败: {AccountId}", accountId);
            }
        }
    }

    private async Task ProcessMessageAsync(string accountId, AccountConnection conn, Message msg, IDictionary<long, User> users)
    {
        var peerId = ResolvePeerId(msg.peer_id);
        var direction = (msg.flags & Message.Flags.out_) != 0
            ? MessageDirection.Outgoing
            : MessageDirection.Incoming;
        var ts = DateTime.SpecifyKind(msg.date, DateTimeKind.Utc);

        // 分类媒体
        var (mediaCategory, doc) = ClassifyMedia(msg.media);

        TelegramRequestMessageBase? unified;
        if (mediaCategory == "image")
        {
            unified = ToImageMessage(accountId, peerId, msg, ts, direction, msg.media as MessageMediaPhoto, doc);
        }
        else if (mediaCategory is "video" or "voice" or "file" or "sticker")
        {
            unified = ToFileMessage(accountId, peerId, msg, ts, direction, mediaCategory, doc);
        }
        else if (string.IsNullOrEmpty(msg.message))
        {
            unified = null; // 纯服务消息（无文本无媒体）
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

        if (unified == null) return;

        // 防撤回核心：媒体到达时立即下载（在推入 inbox 之前完成，确保撤回前已落盘）
        if (msg.media != null && mediaCategory != null)
        {
            await TryAutoDownloadAsync(accountId, conn, msg, unified, mediaCategory, doc).ConfigureAwait(false);
        }

        CacheFileLocation(accountId, conn, msg);
        conn.Inbox.Writer.TryWrite(unified);
    }

    /// <summary>分类 Telegram 媒体类型 → TgVault 统一分类。</summary>
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
    private bool ShouldAutoDownload(string category, Document? doc)
    {
        var cfg = _options.MtProto.AutoDownload;
        if (!cfg.Enabled) return false;

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

        // 文件大小限制（doc.size 为 0 时视为未知，不拦截）
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
                _logger.LogDebug("跳过自动下载：扩展名被禁止 {Ext}", ext);
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
        string category, Document? doc)
    {
        if (!ShouldAutoDownload(category, doc)) return;

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
        if (location == null) return;

        var fileName = ResolveOutputFileName(msg.id, category, doc);
        var relativeUrl = $"/storage/chat/{accountId}/{fileName}";
        var fullPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "storage", "chat", accountId, fileName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await conn.Client.DownloadFileAsync(location, fs, 0, 0, null).ConfigureAwait(false);
            fs.Flush();

            SetLocalUrl(unified, relativeUrl);
            _logger.LogInformation("防撤回自动下载成功: account={AccountId} msg={MsgId} type={Type} url={Url}",
                accountId, msg.id, category, relativeUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "防撤回自动下载失败: account={AccountId} msg={MsgId} type={Type}",
                accountId, msg.id, category);
            try { if (File.Exists(fullPath)) File.Delete(fullPath); } catch { }
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

    private Task SendTextAsync(AccountConnection conn, ResponseMessageText text, CancellationToken ct)
    {
        var (_, peerId) = SplitPeer(text.PeerId ?? "");
        return conn.Client.SendMessageAsync(
            InputPeerFromId(peerId),
            text.Content,
            null, 0, null, default, default);
    }

    private async Task SendImageAsync(AccountConnection conn, ResponseMessageImage image, CancellationToken ct)
    {
        var (_, peerId) = SplitPeer(image.PeerId ?? "");
        if (image.Media?.LocalUrl == null)
            throw new InvalidOperationException("图片回复需提供本地文件路径(LocalUrl)");

        var fullPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", image.Media.LocalUrl.TrimStart('/'));
        await using var fs = File.OpenRead(fullPath);
        var inputFile = await conn.Client.UploadFileAsync(fs, image.Media.LocalUrl, null).ConfigureAwait(false);
        await conn.Client.SendMediaAsync(
            InputPeerFromId(peerId),
            "",
            inputFile,
            null, 0, null, default).ConfigureAwait(false);
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
    /// ⚠️ 简化实现：直接当 user id 处理（access_hash=0 仅对已知 peer 可用）；
    /// 真实场景需按 peer 解析 InputPeer（ResolveUsername / 存储映射）在业务层完善。
    /// </summary>
    private static InputPeer InputPeerFromId(string peerId)
    {
        if (long.TryParse(peerId, out var id) && id != 0)
            return new InputPeerUser(id, 0);
        throw new InvalidOperationException($"无法解析 peer: '{peerId}'");
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
