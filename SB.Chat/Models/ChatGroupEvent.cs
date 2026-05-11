namespace SB.Chat.Models;

public class ChatGroupEvent
{
    public ChatGroupEventType EventType { get; set; }
    public string ChatId { get; set; } = string.Empty;
    public string? ChatTitle { get; set; }
}
