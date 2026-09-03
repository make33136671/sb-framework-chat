using SB.Framework.Models;

namespace Sb.Framework.Chat.Realtime.Storage;

/// <summary>
/// Realtime 模块装配（IModuleInitializer）：声明 5 张 chat_rt_* 表，随框架 DatabaseSeeder 自动建表。
/// 不声明菜单/系统设置/种子（实时通讯 SDK 无自带 UI 菜单；业务系统各自加菜单）。
/// </summary>
public class RealtimeModuleInitializer : IModuleInitializer
{
    public string ModuleName => "Realtime";

    /// <summary>模块标识，用于 Modules:Disabled 黑名单。</summary>
    public string ModuleKey => "realtime";

    /// <summary>初始化顺序：排在框架(0-50)之后，feature(字典序)之后，与 Chat 并列。</summary>
    public int InitOrder => 100;

    public Type[] GetEntityTypes() => new[]
    {
        typeof(RtConversation),
        typeof(RtConversationMember),
        typeof(RtMessage),
        typeof(RtReadState),
        typeof(RtMedia),
    };
}
