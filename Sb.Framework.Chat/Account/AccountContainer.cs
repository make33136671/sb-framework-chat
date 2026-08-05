using SB.Framework.Core.Util;

namespace Sb.Framework.Chat.Account;

/// <summary>
/// 账号容器：进程级账号登记表（参考盛派 Container 模式）。
/// 管理 Bot/个人号账号的创建、查询、移除与状态变更通知。
/// ≤100 业务员规模下单机内存活跃连接 + DB 持久化即可，无需分布式会话管理。
/// </summary>
public static class AccountContainer
{
    private static readonly Dictionary<string, AccountBag> _accounts = new();
    private static readonly object _lock = new();

    /// <summary>创建或获取已存在的账号。</summary>
    public static AccountBag GetOrCreate(AccountOptions options)
    {
        if (string.IsNullOrEmpty(options.AccountId))
            throw new ArgumentException("AccountId 必填", nameof(options));

        lock (_lock)
        {
            if (_accounts.TryGetValue(options.AccountId, out var existing))
                return existing;

            var bag = new AccountBag
            {
                AccountId = options.AccountId,
                OwnerId = options.OwnerId,
                Type = options.Type,
                SessionState = AccountSessionState.Created,
                Credential = options.Credential
            };
            _accounts[options.AccountId] = bag;
            return bag;
        }
    }

    /// <summary>获取账号（不存在返回 null）。</summary>
    public static AccountBag? Get(string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return null;
        lock (_lock) return _accounts.TryGetValue(accountId, out var bag) ? bag : null;
    }

    /// <summary>按 Owner/Tenant 列出账号。</summary>
    public static IReadOnlyList<AccountBag> GetByOwner(string ownerId)
    {
        lock (_lock) return _accounts.Values.Where(a => a.OwnerId == ownerId).ToList();
    }

    /// <summary>列出全部账号。</summary>
    public static IReadOnlyList<AccountBag> GetAll()
    {
        lock (_lock) return _accounts.Values.ToList();
    }

    /// <summary>移除账号（断开由适配器负责）。</summary>
    public static void Remove(string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return;
        lock (_lock) _accounts.Remove(accountId);
    }

    /// <summary>
    /// 通知状态变更。适配器在连接/断开/失效时调用，业务层订阅做权限/告警。
    /// </summary>
    public static event EventHandler<AccountStateChangedEventArgs>? StateChanged;

    /// <summary>更新状态并触发事件（通道适配器调用）。</summary>
    public static void UpdateState(string accountId, AccountSessionState newState)
    {
        AccountBag? bag;
        AccountSessionState old;
        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out bag)) return;
            old = bag.SessionState;
            if (old == newState) return;
            bag.SessionState = newState;
            bag.LastActiveTime = AppTime.UtcNow;
        }
        StateChanged?.Invoke(null, new AccountStateChangedEventArgs
        {
            AccountId = accountId,
            OldState = old,
            NewState = newState,
            AccountType = bag.Type
        });
    }
}
