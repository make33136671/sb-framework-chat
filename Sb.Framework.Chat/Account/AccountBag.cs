namespace Sb.Framework.Chat.Account;

/// <summary>
/// 账号袋（账号运行期表示）。一个 AccountBag = 一个 Bot 或一个个人号连接。
/// </summary>
public sealed class AccountBag
{
    public string AccountId { get; init; } = "";

    /// <summary>多租户判别字段（商家=商家id / 内部=部门id）。SDK 不解释，业务层用于隔离。</summary>
    public string? OwnerId { get; set; }

    public AccountType Type { get; init; }

    /// <summary>会话认证状态机。</summary>
    public AccountSessionState SessionState { get; set; } = AccountSessionState.Created;

    /// <summary>
    /// 能力标记集：该账号支持哪些能力（commands/callbacks/menus/web_app/payments/
    /// anti_withdrawal/full_client/history_sync 等）。
    /// 供业务层权限判断（"该租户能用支付吗"），SDK 自身不限制。
    /// </summary>
    public HashSet<string> Capabilities { get; } = new();

    /// <summary>最后活跃时间（UTC）。</summary>
    public DateTime LastActiveTime { get; set; } = SB.Framework.Core.Util.AppTime.UtcNow;

    /// <summary>最近一次连接/认证失败原因（供业务层展示真实错误，如 api_hash 无效）。</summary>
    public string? LastError { get; set; }

    /// <summary>凭证（运行期内存持有，实现可插拔，不直接暴露明文）。</summary>
    public AccountCredential Credential { get; set; } = new();
}

/// <summary>账号状态变更事件参数。</summary>
public sealed class AccountStateChangedEventArgs : EventArgs
{
    public string AccountId { get; init; } = "";

    public AccountSessionState OldState { get; init; }

    public AccountSessionState NewState { get; init; }

    public AccountType AccountType { get; init; }
}
