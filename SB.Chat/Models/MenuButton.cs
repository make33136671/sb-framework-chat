namespace SB.Chat.Models;

public class MenuButton(string text, string callbackData)
{
    public string Text { get; set; } = text;
    public string CallbackData { get; set; } = callbackData;
}
