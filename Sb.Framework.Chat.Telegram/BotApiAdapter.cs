using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Adapter;
using Sb.Framework.Chat.Model;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotClient = Telegram.Bot.TelegramBotClient;

namespace Sb.Framework.Chat.Telegram;

/// <summary>
/// Bot 通道适配器（Telegram Bot API，Telegram.Bot）。webhook / polling 接收，归一化为统一消息模型。
/// 一个适配器管理该类型下的全部 Bot 账号。
/// </summary>
public sealed class BotApiAdapter : ITelegramAdapter, IDisposable
{
    private readonly TelegramChatOptions _options;
    private readonly ILogger<BotApiAdapter> _logger;
    private readonly ConcurrentDictionary<string, AccountConnection> _connections = new();
    private bool _disposed;

    public AccountType SupportedAccountType => AccountType.Bot;

    private sealed class AccountConnection
    {
        public ITelegramBotClient Client = null!;
        public System.Threading.Channels.Channel<TelegramRequestMessageBase> Inbox =
            System.Threading.Channels.Channel.CreateUnbounded<TelegramRequestMessageBase>();
        public CancellationTokenSource Cts = new();
    }

    public BotApiAdapter(
        TelegramChatOptions options,
        ILogger<BotApiAdapter> logger)
    {
        _options = options;
        _logger = logger;
    }

    // ---- ITelegramAdapter ----

    public Task ConnectAsync(AccountBag account, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (_connections.ContainsKey(account.AccountId)) return Task.CompletedTask;

        var token = account.Credential.Value;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException($"Bot 账号 {account.AccountId} 缺少 token");

        var conn = new AccountConnection { Client = new TelegramBotClient(token) };
        if (!_connections.TryAdd(account.AccountId, conn)) return Task.CompletedTask;

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
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Disconnected);
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TelegramRequestMessageBase> PullAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
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
            ResponseMessageText text => SendTextAsync(conn, text),
            ResponseMessageImage image => SendImageAsync(conn, image),
            ResponseMessageCallbackAnswer answer => conn.Client.AnswerCallbackQuery(
                answer.PeerId ?? "", answer.Text, answer.ShowAlert, cancellationToken: ct),
            _ => throw new NotSupportedException($"Bot 通道暂不支持响应类型 {response.GetType().Name}")
        };
    }

    public async Task<Stream> DownloadMediaAsync(MediaReference media, CancellationToken ct)
    {
        if (!_connections.TryGetValue(media.AccountId, out var conn))
            throw new InvalidOperationException($"账号未连接: {media.AccountId}");
        var ms = new MemoryStream();
        await conn.Client.GetInfoAndDownloadFile(media.MediaId, ms, cancellationToken: ct).ConfigureAwait(false);
        ms.Position = 0;
        return ms;
    }

    public bool Supports(string capability) => capability switch
    {
        "commands" => true,
        "callbacks" => true,
        "menus" => true,
        "web_app" => true,
        "payments" => true,
        "send_text" => true,
        "send_image" => true,
        "media_download" => true,
        _ => false
    };

    // ---- 内部 ----

    private static string ResolveAccountIdFor(TelegramResponseMessageBase response)
        => response.PeerId?.Split(':', 2)[0] ?? "";

    private async Task ConnectInternalAsync(AccountBag account, AccountConnection conn, CancellationToken ct)
    {
        try
        {
            var me = await conn.Client.GetMe(ct).ConfigureAwait(false);
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Ready);
            _logger.LogInformation("Bot 连接成功: {AccountId} @ {Bot}", account.AccountId, me.Username ?? me.Id.ToString());

            // 默认 polling；若配置了 WebhookBaseUrl 则注册 webhook
            if (!string.IsNullOrEmpty(_options.Bot.WebhookBaseUrl))
            {
                var url = $"{_options.Bot.WebhookBaseUrl.TrimEnd('/')}{_options.Bot.PathPrefix}/{account.Credential.Value}";
                await conn.Client.SetWebhook(url, secretToken: _options.Bot.WebhookSecret, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                conn.Client.StartReceiving(
                    (bot, update, token) => { ProcessUpdate(account.AccountId, conn, update); return Task.CompletedTask; },
                    (bot, ex, source, token) => { _logger.LogWarning(ex, "Bot 轮询错误: {AccountId} src={Source}", account.AccountId, source); return Task.CompletedTask; },
                    new ReceiverOptions { AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery, UpdateType.MyChatMember, UpdateType.ChatMember } },
                    conn.Cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot 连接失败: {AccountId}", account.AccountId);
            AccountContainer.UpdateState(account.AccountId, AccountSessionState.Invalidated);
        }
    }

    /// <summary>Webhook 入口：外部（TelegramWebhookController）调用，把 Update 归一化后入队。</summary>
    public Task ProcessWebhookAsync(string accountId, Update update, CancellationToken ct = default)
    {
        if (_connections.TryGetValue(accountId, out var conn))
            ProcessUpdate(accountId, conn, update);
        return Task.CompletedTask;
    }

    private void ProcessUpdate(string accountId, AccountConnection conn, Update update)
    {
        if (update.MyChatMember != null)
        {
            ProcessMyChatMember(accountId, conn, update.MyChatMember);
            return;
        }
        if (update.CallbackQuery != null)
        {
            ProcessCallbackQuery(accountId, conn, update.CallbackQuery);
            return;
        }
        if (update.Message != null)
        {
            ProcessMessage(accountId, conn, update.Message);
        }
    }

    private void ProcessMessage(string accountId, AccountConnection conn, Message message)
    {
        var peerId = message.Chat.Id.ToString();
        var ts = DateTime.SpecifyKind(message.Date, DateTimeKind.Utc);

        TelegramRequestMessageBase? unified = null;
        if (message.Photo is { Length: > 0 })
        {
            var largest = message.Photo[^1];
            unified = new RequestMessageImage
            {
                AccountId = accountId,
                PeerId = peerId,
                MessageId = message.MessageId,
                Direction = MessageDirection.Incoming,
                Timestamp = ts,
                Caption = message.Caption,
                ReplyToMessageId = message.ReplyToMessage?.MessageId.ToString(),
                Media = new ImageMedia
                {
                    MediaId = largest.FileId,
                    Size = largest.FileSize ?? 0,
                    MimeType = "image/jpeg",
                },
            };
        }
        else if (!string.IsNullOrEmpty(message.Text) || !string.IsNullOrEmpty(message.Caption))
        {
            unified = new RequestMessageText
            {
                AccountId = accountId,
                PeerId = peerId,
                MessageId = message.MessageId,
                Direction = MessageDirection.Incoming,
                Timestamp = ts,
                Content = message.Text ?? message.Caption ?? "",
                ReplyToMessageId = message.ReplyToMessage?.MessageId.ToString(),
            };
        }

        if (unified != null)
            conn.Inbox.Writer.TryWrite(unified);
    }

    private void ProcessCallbackQuery(string accountId, AccountConnection conn, CallbackQuery callback)
    {
        conn.Inbox.Writer.TryWrite(new RequestMessageCallback
        {
            AccountId = accountId,
            PeerId = callback.Message?.Chat.Id.ToString() ?? "",
            MessageId = callback.Message?.MessageId ?? 0,
            Direction = MessageDirection.Incoming,
            Timestamp = SB.Framework.Core.Util.AppTime.UtcNow,
            CallbackData = callback.Data ?? "",
            CallbackId = callback.Id,
        });
    }

    private void ProcessMyChatMember(string accountId, AccountConnection conn, ChatMemberUpdated memberUpdate)
    {
        var newStatus = memberUpdate.NewChatMember.Status;
        var oldStatus = memberUpdate.OldChatMember.Status;
        GroupEventType? eventType = null;

        if (newStatus == ChatMemberStatus.Member && oldStatus is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
            eventType = GroupEventType.BotAdded;
        else if (newStatus is ChatMemberStatus.Left or ChatMemberStatus.Kicked && oldStatus == ChatMemberStatus.Member)
            eventType = GroupEventType.BotRemoved;

        if (eventType.HasValue)
        {
            conn.Inbox.Writer.TryWrite(new RequestMessageGroupEvent
            {
                AccountId = accountId,
                PeerId = memberUpdate.Chat.Id.ToString(),
                MessageId = 0,
                Direction = MessageDirection.Incoming,
                Timestamp = SB.Framework.Core.Util.AppTime.UtcNow,
                EventType = eventType.Value,
            });
        }
    }

    private async Task SendTextAsync(AccountConnection conn, ResponseMessageText text)
    {
        var (_, peerId) = SplitPeer(text.PeerId ?? "");
        ReplyParameters? replyParams = null;
        if (!string.IsNullOrEmpty(text.ReplyToMessageId) && int.TryParse(text.ReplyToMessageId, out var replyId))
            replyParams = new ReplyParameters { MessageId = replyId };
        await conn.Client.SendMessage(peerId, text.Content, parseMode: ParseMode.Html, replyParameters: replyParams)
            .ConfigureAwait(false);
    }

    private async Task SendImageAsync(AccountConnection conn, ResponseMessageImage image)
    {
        var (_, peerId) = SplitPeer(image.PeerId ?? "");
        if (image.Media?.LocalUrl == null)
            throw new InvalidOperationException("图片回复需提供本地文件路径(LocalUrl)");
        var fullPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", image.Media.LocalUrl.TrimStart('/'));
        await using var fs = File.OpenRead(fullPath);
        await conn.Client.SendPhoto(peerId, new InputFileStream(fs)).ConfigureAwait(false);
    }

    private static (string AccountId, string PeerId) SplitPeer(string peerId)
    {
        var idx = peerId.IndexOf(':');
        return idx >= 0
            ? (peerId[..idx], peerId[(idx + 1)..])
            : (peerId, "");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var conn in _connections.Values)
        {
            conn.Cts.Cancel();
            conn.Client = null!;
        }
        _connections.Clear();
    }
}
