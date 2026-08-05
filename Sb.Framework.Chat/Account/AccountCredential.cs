namespace Sb.Framework.Chat.Account;

/// <summary>
/// 账号凭证（实现可插拔，不直接暴露明文）。
/// Bot 通道：Token；个人号通道：MTProto session 数据（加密存储）。
/// 具体凭据内容由通道适配器解释，SDK 核心只持有引用。
/// </summary>
public sealed class AccountCredential
{
    /// <summary>凭据类型标识（bot_token / mtproto_session 等），由适配器约定。</summary>
    public string Kind { get; set; } = "";

    /// <summary>凭据主体（token / 加密 session 字符串）。运行期内存持有，不应明文落日志。</summary>
    public string Value { get; set; } = "";

    /// <summary>附加参数（如 apiId/apiHash、phone 等）。</summary>
    public Dictionary<string, string> Extra { get; set; } = new();
}

/// <summary>
/// 账号选项（创建账号时传入）。
/// </summary>
public sealed class AccountOptions
{
    public string AccountId { get; set; } = "";

    /// <summary>多租户判别：商家平台=商家id；内部系统=部门id。SDK 不解释，仅透传。</summary>
    public string? OwnerId { get; set; }

    public AccountType Type { get; set; }

    public AccountCredential Credential { get; set; } = new();
}
