namespace SB.Chat.Models;

public class ChatMediaInfo
{
    public string FileId { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public ChatMessageType MediaType { get; set; }
}
