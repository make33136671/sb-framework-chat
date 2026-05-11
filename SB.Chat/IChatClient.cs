using SB.Chat.Models;

namespace SB.Chat;

/// <summary>
/// Chat Client 连接器接口（个人号 / MTProto）
/// </summary>
public interface IChatClient : IDisposable
{
    Task ConnectAsync();
    Task DisconnectAsync();
    Task<Stream> DownloadMediaAsync(string fileId);

    event EventHandler<ChatMessageEvent> OnMessage;
}
