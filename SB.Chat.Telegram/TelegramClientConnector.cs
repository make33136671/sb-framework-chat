using SB.Chat.Models;
using TL;

namespace SB.Chat.Telegram;

/// <summary>
/// Telegram 个人号连接器（基于 WTelegramClient / MTProto）
/// 用于被动采集群组消息
/// </summary>
public class TelegramClientConnector : IChatClient
{
    private readonly string _apiId;
    private readonly string _apiHash;
    private readonly string _phone;
    private readonly string _sessionPath;
    private WTelegram.Client? _client;
    private bool _disposed;

    public TelegramClientConnector(string apiId, string apiHash, string phone, string sessionPath)
    {
        _apiId = apiId ?? throw new ArgumentNullException(nameof(apiId));
        _apiHash = apiHash ?? throw new ArgumentNullException(nameof(apiHash));
        _phone = phone ?? throw new ArgumentNullException(nameof(phone));
        _sessionPath = sessionPath ?? throw new ArgumentNullException(nameof(sessionPath));
    }

    public event EventHandler<ChatMessageEvent>? OnMessage;

    public async Task ConnectAsync()
    {
        // 确保 session 目录存在
        var dir = Path.GetDirectoryName(_sessionPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _client = new WTelegram.Client(ConfigProvider);
        _client.OnUpdates += HandleUpdates;

        await _client.LoginUserIfNeeded();
    }

    public Task DisconnectAsync()
    {
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    public async Task<Stream> DownloadMediaAsync(string fileId)
    {
        if (_client == null)
            throw new InvalidOperationException("Client 未连接");

        // WTelegramClient 使用 InputFileLocation 下载
        // fileId 在此上下文中可能是序列化的文件引用
        // 简化实现：尝试解析为 document/photo 引用并下载
        var ms = new MemoryStream();

        // 通过 fileId 构建 InputDocumentFileLocation
        if (long.TryParse(fileId, out var id))
        {
            var location = new InputDocumentFileLocation
            {
                id = id,
                access_hash = 0, // 需要完整引用时由调用方传入
                file_reference = Array.Empty<byte>()
            };
            await _client.DownloadFileAsync(location, ms);
            ms.Position = 0;
        }

        return ms;
    }

    // ---- 内部 ----

    private string? ConfigProvider(string what)
    {
        return what switch
        {
            "api_id" => _apiId,
            "api_hash" => _apiHash,
            "phone_number" => _phone,
            "session_pathname" => _sessionPath,
            "verification_code" => null, // 需要交互式输入时返回 null
            "password" => null,          // 2FA 密码
            _ => null
        };
    }

    private Task HandleUpdates(UpdatesBase updates)
    {
        foreach (var update in updates.UpdateList)
        {
            if (update is UpdateNewMessage { message: TL.Message msg })
            {
                ProcessMessage(msg, updates.Users);
            }
            else if (update is UpdateNewChannelMessage { message: TL.Message channelMsg })
            {
                ProcessMessage(channelMsg, updates.Users);
            }
        }
        return Task.CompletedTask;
    }

    private void ProcessMessage(TL.Message msg, Dictionary<long, User> users)
    {
        if ((msg.flags & TL.Message.Flags.out_) != 0) return; // 忽略自己发的消息

        var sender = msg.from_id is PeerUser pu && users.TryGetValue(pu.user_id, out var user) ? user : null;
        var chatId = msg.peer_id switch
        {
            PeerChat pc => pc.chat_id.ToString(),
            PeerChannel pch => pch.channel_id.ToString(),
            PeerUser pu2 => pu2.user_id.ToString(),
            _ => ""
        };

        var chatMsg = new ChatMessageEvent
        {
            ChatId = chatId,
            MessageId = msg.id.ToString(),
            SenderId = sender?.id.ToString() ?? "",
            SenderName = BuildSenderName(sender),
            Content = msg.message ?? "",
            MessageType = DetectMessageType(msg),
            ReplyToMessageId = msg.reply_to is MessageReplyHeader reply ? reply.reply_to_msg_id.ToString() : null,
            IsPrivateChat = msg.peer_id is PeerUser,
            RawData = msg,
            Media = ExtractMedia(msg)
        };

        OnMessage?.Invoke(this, chatMsg);
    }

    private static string BuildSenderName(User? user)
    {
        if (user == null) return "";
        var name = user.first_name ?? "";
        if (!string.IsNullOrEmpty(user.last_name))
            name += " " + user.last_name;
        return string.IsNullOrWhiteSpace(name) ? user.username ?? "" : name;
    }

    private static Models.ChatMessageType DetectMessageType(TL.Message msg)
    {
        return msg.media switch
        {
            MessageMediaPhoto => Models.ChatMessageType.Image,
            MessageMediaDocument { document: Document doc } when doc.mime_type?.StartsWith("audio/") == true
                => Models.ChatMessageType.Audio,
            MessageMediaDocument { document: Document doc } when doc.mime_type?.StartsWith("video/") == true
                => Models.ChatMessageType.Video,
            MessageMediaDocument => Models.ChatMessageType.File,
            _ => Models.ChatMessageType.Text
        };
    }

    private static List<ChatMediaInfo> ExtractMedia(TL.Message msg)
    {
        var list = new List<ChatMediaInfo>();

        switch (msg.media)
        {
            case MessageMediaPhoto { photo: Photo photo }:
                list.Add(new ChatMediaInfo
                {
                    FileId = photo.id.ToString(),
                    FileSize = photo.sizes?.OfType<PhotoSize>().LastOrDefault()?.size,
                    MimeType = "image/jpeg",
                    MediaType = Models.ChatMessageType.Image
                });
                break;

            case MessageMediaDocument { document: Document doc }:
                var mediaType = doc.mime_type switch
                {
                    string m when m.StartsWith("audio/") => Models.ChatMessageType.Audio,
                    string m when m.StartsWith("video/") => Models.ChatMessageType.Video,
                    string m when m.StartsWith("image/") => Models.ChatMessageType.Image,
                    _ => Models.ChatMessageType.File
                };
                list.Add(new ChatMediaInfo
                {
                    FileId = doc.id.ToString(),
                    FileName = doc.Filename,
                    FileSize = doc.size,
                    MimeType = doc.mime_type,
                    MediaType = mediaType
                });
                break;
        }

        return list;
    }

    // ---- Dispose ----

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }
}
