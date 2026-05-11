namespace SB.Chat.Models;

public class ChatMessageEvent
{
    public string ChatId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string? ChatTitle { get; set; }
    public string Content { get; set; } = string.Empty;
    public ChatMessageType MessageType { get; set; } = ChatMessageType.Text;
    public string? ReplyToMessageId { get; set; }
    public bool IsPrivateChat { get; set; }
    public object? RawData { get; set; }
    public List<ChatMediaInfo> Media { get; set; } = new();
}
