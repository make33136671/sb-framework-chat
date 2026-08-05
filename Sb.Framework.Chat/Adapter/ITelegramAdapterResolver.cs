namespace Sb.Framework.Chat.Adapter;

/// <summary>
/// 适配器解析器：根据账号类型解析到对应通道适配器（Bot / MtProto / Mock）。
/// </summary>
public interface ITelegramAdapterResolver
{
    /// <summary>按账号 id 解析适配器。账号不存在或无可适配器时返回 null。</summary>
    ITelegramAdapter? Resolve(string accountId);
}
