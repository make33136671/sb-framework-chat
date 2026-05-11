using System.Text.Json;
using SB.Chat.Models;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using ChatMenuButton = SB.Chat.Models.MenuButton;

namespace SB.Chat.Telegram;

/// <summary>
/// Telegram Bot 连接器（基于 Telegram.Bot 官方 SDK）
/// 支持 Polling 和 Webhook 两种模式
/// </summary>
public class TelegramBotConnector : IChatBot
{
    private readonly string _token;
    private TelegramBotClient? _botClient;
    private CancellationTokenSource? _pollingCts;
    private bool _disposed;

    public bool IsConnected { get; private set; }

    public TelegramBotConnector(string token)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
    }

    // ---- 连接管理 ----

    public async Task ConnectAsync()
    {
        _botClient = new TelegramBotClient(_token);
        await _botClient.GetMe();
        IsConnected = true;
    }

    public async Task DisconnectAsync()
    {
        await StopPollingAsync();
        IsConnected = false;
    }

    // ---- Polling ----

    public Task StartPollingAsync()
    {
        EnsureConnected();
        _pollingCts?.Cancel();
        _pollingCts = new CancellationTokenSource();

        _botClient!.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: new ReceiverOptions
            {
                AllowedUpdates = new[]
                {
                    UpdateType.Message, UpdateType.CallbackQuery,
                    UpdateType.MyChatMember, UpdateType.ChatMember
                }
            },
            cancellationToken: _pollingCts.Token);

        return Task.CompletedTask;
    }

    public Task StopPollingAsync()
    {
        if (_pollingCts is { IsCancellationRequested: false })
        {
            _pollingCts.Cancel();
            _pollingCts.Dispose();
            _pollingCts = null;
        }
        return Task.CompletedTask;
    }

    // ---- Webhook ----

    public async Task SetWebhookAsync(string webhookUrl)
    {
        EnsureConnected();
        await _botClient!.SetWebhook(webhookUrl);
    }

    public async Task DeleteWebhookAsync()
    {
        EnsureConnected();
        await _botClient!.DeleteWebhook();
    }

    public Task ProcessWebhookAsync(string payload)
    {
        EnsureConnected();
        var update = JsonSerializer.Deserialize<Update>(payload, JsonBotAPI.Options);
        if (update != null)
            ProcessUpdate(update);
        return Task.CompletedTask;
    }

    // ---- 发送消息 ----

    public async Task SendTextAsync(string chatId, string text, string? replyToMessageId = null)
    {
        EnsureConnected();
        ReplyParameters? replyParams = null;
        if (!string.IsNullOrEmpty(replyToMessageId) && int.TryParse(replyToMessageId, out var replyId))
            replyParams = new ReplyParameters { MessageId = replyId };

        await _botClient!.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyParameters: replyParams);
    }

    public async Task SendMenuAsync(string chatId, string text, List<List<ChatMenuButton>> buttons)
    {
        EnsureConnected();
        var markup = BuildInlineKeyboard(buttons);
        await _botClient!.SendMessage(chatId, text,
            parseMode: ParseMode.Html,
            replyMarkup: markup);
    }

    // ---- 编辑消息 ----

    public async Task EditMessageAsync(string chatId, string messageId, string text,
        List<List<ChatMenuButton>>? buttons = null)
    {
        EnsureConnected();
        if (!int.TryParse(messageId, out var msgId))
            throw new ArgumentException("Invalid messageId", nameof(messageId));

        var markup = buttons != null ? BuildInlineKeyboard(buttons) : null;
        await _botClient!.EditMessageText(chatId, msgId, text,
            parseMode: ParseMode.Html,
            replyMarkup: markup);
    }

    // ---- 回调应答 ----

    public async Task AnswerCallbackAsync(string callbackId, string? text = null, bool showAlert = false)
    {
        EnsureConnected();
        await _botClient!.AnswerCallbackQuery(callbackId, text, showAlert);
    }

    // ---- 媒体下载 ----

    public async Task<Stream> DownloadMediaAsync(string fileId)
    {
        EnsureConnected();
        var ms = new MemoryStream();
        await _botClient!.GetInfoAndDownloadFile(fileId, ms);
        ms.Position = 0;
        return ms;
    }

    public async Task<Stream?> GetUserAvatarAsync(string userId)
    {
        EnsureConnected();
        if (!long.TryParse(userId, out var uid)) return null;

        var photos = await _botClient!.GetUserProfilePhotos(uid, limit: 1);
        if (photos.TotalCount == 0 || photos.Photos.Length == 0) return null;

        var photo = photos.Photos[0].Last(); // 最大尺寸
        var ms = new MemoryStream();
        await _botClient!.GetInfoAndDownloadFile(photo.FileId, ms);
        ms.Position = 0;
        return ms;
    }

    // ---- 事件 ----

    public event EventHandler<ChatMessageEvent>? OnMessage;
    public event EventHandler<ChatCallbackEvent>? OnCallback;
    public event EventHandler<ChatGroupEvent>? OnGroupEvent;

    // ---- 内部：Update 处理 ----

    private Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        ProcessUpdate(update);
        return Task.CompletedTask;
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception ex,
        HandleErrorSource source, CancellationToken ct)
    {
        Console.WriteLine($"[TelegramBot] Polling error ({source}): {ex.Message}");
        return Task.CompletedTask;
    }

    private void ProcessUpdate(Update update)
    {
        // MyChatMember: Bot 被加入/移除群组
        if (update.MyChatMember != null)
        {
            ProcessMyChatMember(update.MyChatMember);
            return;
        }

        // CallbackQuery
        if (update.CallbackQuery != null)
        {
            ProcessCallbackQuery(update.CallbackQuery);
            return;
        }

        // Message
        if (update.Message != null)
        {
            ProcessMessage(update.Message);
        }
    }

    private void ProcessMyChatMember(ChatMemberUpdated memberUpdate)
    {
        var newStatus = memberUpdate.NewChatMember.Status;
        var oldStatus = memberUpdate.OldChatMember.Status;

        if (newStatus == ChatMemberStatus.Member && oldStatus is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
        {
            OnGroupEvent?.Invoke(this, new ChatGroupEvent
            {
                EventType = ChatGroupEventType.BotAdded,
                ChatId = memberUpdate.Chat.Id.ToString(),
                ChatTitle = memberUpdate.Chat.Title
            });
        }
        else if (newStatus is ChatMemberStatus.Left or ChatMemberStatus.Kicked && oldStatus == ChatMemberStatus.Member)
        {
            OnGroupEvent?.Invoke(this, new ChatGroupEvent
            {
                EventType = ChatGroupEventType.BotRemoved,
                ChatId = memberUpdate.Chat.Id.ToString(),
                ChatTitle = memberUpdate.Chat.Title
            });
        }
    }

    private void ProcessCallbackQuery(CallbackQuery callback)
    {
        OnCallback?.Invoke(this, new ChatCallbackEvent
        {
            CallbackId = callback.Id,
            ChatId = callback.Message?.Chat.Id.ToString() ?? "",
            MessageId = callback.Message?.MessageId.ToString() ?? "",
            UserId = callback.From.Id.ToString(),
            CallbackData = callback.Data ?? ""
        });
    }

    private void ProcessMessage(Message message)
    {
        var chatMsg = new ChatMessageEvent
        {
            ChatId = message.Chat.Id.ToString(),
            MessageId = message.MessageId.ToString(),
            SenderId = message.From?.Id.ToString() ?? "",
            SenderName = BuildSenderName(message.From),
            ChatTitle = message.Chat.Title,
            Content = message.Text ?? message.Caption ?? "",
            MessageType = DetectMessageType(message),
            ReplyToMessageId = message.ReplyToMessage?.MessageId.ToString(),
            IsPrivateChat = message.Chat.Type == ChatType.Private,
            RawData = message,
            Media = ExtractMedia(message)
        };

        OnMessage?.Invoke(this, chatMsg);
    }

    // ---- 辅助方法 ----

    private static string BuildSenderName(User? user)
    {
        if (user == null) return "";
        var name = user.FirstName ?? "";
        if (!string.IsNullOrEmpty(user.LastName))
            name += " " + user.LastName;
        return string.IsNullOrWhiteSpace(name) ? user.Username ?? "" : name;
    }

    private static Models.ChatMessageType DetectMessageType(Message message)
    {
        if (message.Sticker != null) return Models.ChatMessageType.Sticker;
        if (message.Animation != null) return Models.ChatMessageType.Video; // GIF/动画
        if (message.Photo != null) return Models.ChatMessageType.Image;
        if (message.Video != null || message.VideoNote != null) return Models.ChatMessageType.Video;
        if (message.Audio != null || message.Voice != null) return Models.ChatMessageType.Audio;
        if (message.Document != null) return Models.ChatMessageType.File;
        return Models.ChatMessageType.Text;
    }

    private static List<ChatMediaInfo> ExtractMedia(Message message)
    {
        var list = new List<ChatMediaInfo>();

        if (message.Photo is { Length: > 0 })
        {
            var largest = message.Photo.Last();
            list.Add(new ChatMediaInfo
            {
                FileId = largest.FileId,
                FileSize = largest.FileSize,
                MimeType = "image/jpeg",
                MediaType = Models.ChatMessageType.Image
            });
        }

        if (message.Animation != null)
        {
            list.Add(new ChatMediaInfo
            {
                FileId = message.Animation.FileId,
                FileName = message.Animation.FileName,
                FileSize = message.Animation.FileSize,
                MimeType = message.Animation.MimeType ?? "video/mp4",
                MediaType = Models.ChatMessageType.Video
            });
        }
        else if (message.Document != null)
        {
            list.Add(new ChatMediaInfo
            {
                FileId = message.Document.FileId,
                FileName = message.Document.FileName,
                FileSize = message.Document.FileSize,
                MimeType = message.Document.MimeType,
                MediaType = Models.ChatMessageType.File
            });
        }

        if (message.Audio != null)
        {
            list.Add(new ChatMediaInfo
            {
                FileId = message.Audio.FileId,
                FileName = message.Audio.FileName,
                FileSize = message.Audio.FileSize,
                MimeType = message.Audio.MimeType ?? "audio/mpeg",
                MediaType = Models.ChatMessageType.Audio
            });
        }

        if (message.Voice != null)
        {
            list.Add(new ChatMediaInfo
            {
                FileId = message.Voice.FileId,
                FileSize = message.Voice.FileSize,
                MimeType = message.Voice.MimeType ?? "audio/ogg",
                MediaType = Models.ChatMessageType.Audio
            });
        }

        if (message.Video != null)
        {
            list.Add(new ChatMediaInfo
            {
                FileId = message.Video.FileId,
                FileName = message.Video.FileName,
                FileSize = message.Video.FileSize,
                MimeType = message.Video.MimeType ?? "video/mp4",
                MediaType = Models.ChatMessageType.Video
            });
        }

        if (message.VideoNote != null)
        {
            list.Add(new ChatMediaInfo
            {
                FileId = message.VideoNote.FileId,
                FileSize = message.VideoNote.FileSize,
                MimeType = "video/mp4",
                MediaType = Models.ChatMessageType.Video
            });
        }

        if (message.Sticker != null)
        {
            var mime = message.Sticker.IsAnimated ? "application/x-tgsticker"
                     : message.Sticker.IsVideo ? "video/webm"
                     : "image/webp";
            list.Add(new ChatMediaInfo
            {
                FileId = message.Sticker.FileId,
                FileSize = message.Sticker.FileSize,
                MimeType = mime,
                MediaType = Models.ChatMessageType.Sticker
            });
        }

        return list;
    }

    private static InlineKeyboardMarkup BuildInlineKeyboard(List<List<ChatMenuButton>> buttons)
    {
        var rows = buttons.Select(row =>
            row.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData))
        );
        return new InlineKeyboardMarkup(rows);
    }

    private void EnsureConnected()
    {
        if (_botClient == null || !IsConnected)
            throw new InvalidOperationException("Bot 未连接，请先调用 ConnectAsync()");
    }

    // ---- Dispose ----

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollingCts?.Cancel();
        _pollingCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
