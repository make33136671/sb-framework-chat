using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Account;

namespace Sb.Framework.Chat.Telegram.MtProto;

/// <summary>
/// WTelegramClient 认证桥：其 ConfigProvider 是同步回调，这里阻塞桥接异步 IAuthProvider。
/// </summary>
internal sealed class AuthBridge
{
    private readonly IAuthProvider _authProvider;
    private readonly string _accountId;
    private readonly ILogger _logger;

    public AuthBridge(IAuthProvider authProvider, string accountId, ILogger logger)
    {
        _authProvider = authProvider;
        _accountId = accountId;
        _logger = logger;
    }

    public string? ConfigProvider(string what, string sessionPath, AccountBag account, TelegramChatOptions options)
    {
        switch (what)
        {
            case "api_id":
                return options.MtProto.ApiId;
            case "api_hash":
                return options.MtProto.ApiHash;
            case "phone_number":
                return account.Credential.Extra.GetValueOrDefault("phone") ?? "";
            case "session_pathname":
                return sessionPath;
            case "verification_code":
                return AwaitAuth(ct => _authProvider.GetVerificationCodeAsync(_accountId, ct));
            case "password":
                return AwaitAuth(ct => _authProvider.GetTwoFactorPasswordAsync(_accountId, ct));
            default:
                return null;
        }
    }

    /// <summary>同步阻塞等待异步认证输入（WTelegramClient 要求同步返回值）。超时 5 分钟视为取消。</summary>
    private string? AwaitAuth(Func<CancellationToken, Task<string?>> getter)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            return getter(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("认证输入超时/取消: {AccountId}", _accountId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "认证输入失败: {AccountId}", _accountId);
            return null;
        }
    }
}
