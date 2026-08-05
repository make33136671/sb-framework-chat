using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sb.Framework.Chat.Account;

namespace Sb.Framework.Chat;

/// <summary>
/// 账号健康检查（接入框架 /health）。汇总各账号连接状态、会话就绪数与失效数。
/// 业务系统：builder.Services.AddHealthChecks().AddCheck&lt;TelegramAccountHealthCheck&gt;("telegram")。
/// </summary>
public sealed class TelegramAccountHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var accounts = AccountContainer.GetAll();
        var ready = accounts.Count(a => a.SessionState == AccountSessionState.Ready);
        var invalidated = accounts.Count(a => a.SessionState == AccountSessionState.Invalidated);
        var pending = accounts.Count - ready - invalidated;

        var data = new Dictionary<string, object>
        {
            ["total"] = accounts.Count,
            ["ready"] = ready,
            ["invalidated"] = invalidated,
            ["pending"] = pending,
        };

        // 任一账号失效 → Degraded（不致命，业务可告警）；全部就绪/空 → Healthy
        var status = invalidated > 0 ? HealthStatus.Degraded : HealthStatus.Healthy;
        var description = invalidated > 0
            ? $"有 {invalidated} 个账号已失效（需重新授权）"
            : $"账号 {ready}/{accounts.Count} 就绪";

        return Task.FromResult(new HealthCheckResult(status, description, data: data));
    }
}
