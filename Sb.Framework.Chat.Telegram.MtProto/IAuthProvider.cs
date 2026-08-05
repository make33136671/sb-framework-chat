namespace Sb.Framework.Chat.Telegram.MtProto;

/// <summary>
/// 认证输入提供者（业务实现具体交互方式，SDK 覆盖所有登录方式）。
/// WTelegramClient 的 ConfigProvider 是同步回调，实现方通过阻塞等待 async 结果（TCS）桥接。
/// </summary>
public interface IAuthProvider
{
    /// <summary>获取短信/App 验证码（业务从网页/短信渠道拿）。返回 null=取消登录。</summary>
    Task<string?> GetVerificationCodeAsync(string accountId, CancellationToken ct);

    /// <summary>获取二步验证（2FA）密码。账号未开 2FA 时不会被调用。</summary>
    Task<string?> GetTwoFactorPasswordAsync(string accountId, CancellationToken ct);

    /// <summary>导入已有会话串（Telethon/pyrogram 兼容），或返回 null 走全新登录。</summary>
    Task<string?> TryImportSessionAsync(string accountId, CancellationToken ct);
}
