using SB.Framework.Models;

namespace Sb.Framework.Chat.Storage;

/// <summary>
/// Chat 模块装配（IModuleInitializer）：声明 6 张 chat_* 表，随框架 DatabaseSeeder 自动建表。
/// 不声明菜单/系统设置/种子（SDK 无自带 UI 菜单；业务系统各自加菜单）。
/// </summary>
public class ChatModuleInitializer : IModuleInitializer
{
    public string ModuleName => "Chat";

    /// <summary>模块标识，用于 Modules:Disabled 黑名单。</summary>
    public string ModuleKey => "chat";

    /// <summary>初始化顺序：排在框架(0-50)之后，feature(字典序)之后。</summary>
    public int InitOrder => 100;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(ChatAccount),
        typeof(ChatSession),
        typeof(ChatPeer),
        typeof(ChatMessage),
        typeof(ChatMedia),
        typeof(ChatSyncState),
    };
}
