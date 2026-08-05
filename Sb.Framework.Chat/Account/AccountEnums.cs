namespace Sb.Framework.Chat.Account;

/// <summary>账号类型（Bot / 个人号）。</summary>
public enum AccountType
{
    /// <summary>Bot（官方 Bot API，token 认证）</summary>
    Bot,

    /// <summary>个人号（MTProto 协议，手机号+验证码+2FA / 会话认证）</summary>
    Personal
}

/// <summary>
/// 账号会话状态机：覆盖所有登录/认证方式，业务实现阶段再选具体方式。
/// </summary>
public enum AccountSessionState
{
    /// <summary>已创建，未开始认证</summary>
    Created,

    /// <summary>等待验证码（手机号登录流程）</summary>
    AwaitingCode,

    /// <summary>等待二步验证密码</summary>
    Awaiting2FA,

    /// <summary>就绪（已连接，可收发）</summary>
    Ready,

    /// <summary>已断开（网络/手动），将由 RetryUtil 尝试重连</summary>
    Disconnected,

    /// <summary>已失效（手机登出/异地登录/凭证被吊销），需业务层重新授权</summary>
    Invalidated
}
