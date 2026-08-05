using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Account;

namespace Sb.Framework.Chat.Adapter;

/// <summary>
/// 默认适配器解析器：从 AccountContainer 取账号 → 按 SupportedAccountType 匹配已注册适配器。
/// </summary>
public sealed class TelegramAdapterResolver : ITelegramAdapterResolver
{
    private readonly IEnumerable<ITelegramAdapter> _adapters;
    private readonly ILogger<TelegramAdapterResolver> _logger;

    public TelegramAdapterResolver(
        IEnumerable<ITelegramAdapter> adapters,
        ILogger<TelegramAdapterResolver> logger)
    {
        _adapters = adapters;
        _logger = logger;
    }

    public ITelegramAdapter? Resolve(string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return null;
        var account = AccountContainer.Get(accountId);
        if (account == null)
        {
            _logger.LogWarning("账号不存在: {AccountId}", accountId);
            return null;
        }
        return _adapters.FirstOrDefault(a => a.SupportedAccountType == account.Type);
    }
}
