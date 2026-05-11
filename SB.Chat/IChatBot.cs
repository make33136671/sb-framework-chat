using SB.Chat.Models;

namespace SB.Chat;

/// <summary>
/// Chat Bot 连接器接口（Bot 账号）
/// 支持 Polling 和 Webhook 两种模式
/// </summary>
public interface IChatBot : IDisposable
{
    /// <summary>是否已连接</summary>
    bool IsConnected { get; }

    // ---- 连接管理 ----

    Task ConnectAsync();
    Task DisconnectAsync();

    // ---- Polling 模式 ----

    Task StartPollingAsync();
    Task StopPollingAsync();

    // ---- Webhook 模式 ----

    Task SetWebhookAsync(string webhookUrl);
    Task DeleteWebhookAsync();

    /// <summary>
    /// 处理 Webhook 请求（解析 payload 并同步触发事件）
    /// </summary>
    Task ProcessWebhookAsync(string payload);

    // ---- 发送消息 ----

    Task SendTextAsync(string chatId, string text, string? replyToMessageId = null);
    Task SendMenuAsync(string chatId, string text, List<List<MenuButton>> buttons);

    // ---- 编辑消息 ----

    Task EditMessageAsync(string chatId, string messageId, string text, List<List<MenuButton>>? buttons = null);

    // ---- 回调应答 ----

    Task AnswerCallbackAsync(string callbackId, string? text = null, bool showAlert = false);

    // ---- 媒体 ----

    Task<Stream> DownloadMediaAsync(string fileId);
    Task<Stream?> GetUserAvatarAsync(string userId);

    // ---- 事件 ----

    event EventHandler<ChatMessageEvent> OnMessage;
    event EventHandler<ChatCallbackEvent> OnCallback;
    event EventHandler<ChatGroupEvent> OnGroupEvent;
}
