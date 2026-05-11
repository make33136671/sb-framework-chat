namespace SB.Chat.Models;

public class ChatCallbackEvent
{
    public string CallbackId { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string CallbackData { get; set; } = string.Empty;
}
