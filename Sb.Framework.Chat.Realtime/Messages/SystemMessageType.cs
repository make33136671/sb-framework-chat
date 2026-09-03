namespace Sb.Framework.Chat.Realtime.Messages;

/// <summary>
/// 系统消息类型常量。系统消息的 ContentType 以 "sys." 前缀标记（如 sys.session_init / sys.welcome / sys.transfer / sys.offline）。
/// </summary>
public static class SystemMessageType
{
    public const string Prefix = "sys.";

    public const string SessionInit = "sys.session_init";
    public const string Welcome = "sys.welcome";
    public const string Transfer = "sys.transfer";
    public const string Offline = "sys.offline";
    public const string Read = "sys.read";

    public static bool IsSystem(string contentType) =>
        !string.IsNullOrEmpty(contentType) && contentType.StartsWith(Prefix, StringComparison.Ordinal);
}
