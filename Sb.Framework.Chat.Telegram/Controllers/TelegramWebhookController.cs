using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Adapter;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Sb.Framework.Chat.Telegram.Controllers;

/// <summary>
/// 统一 Webhook 端点：一个端点服务所有 Bot。绝对路由（不经 admin 前缀约定）。
/// 安全：路径含 token 识别账号 + 可选 secret_token 头校验（X-Telegram-Bot-Api-Secret-Token）。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("/telegram/webhook/{token}")]
public class TelegramWebhookController : ControllerBase
{
    private readonly ITelegramAdapterResolver _resolver;
    private readonly TelegramChatOptions _options;

    public TelegramWebhookController(
        ITelegramAdapterResolver resolver,
        TelegramChatOptions options)
    {
        _resolver = resolver;
        _options = options;
    }

    [HttpPost]
    public async Task<IActionResult> Post(string token, CancellationToken ct)
    {
        // 1. secret_token 校验（配置了才启用）
        if (!string.IsNullOrEmpty(_options.Bot.WebhookSecret))
        {
            var header = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            if (!string.Equals(header, _options.Bot.WebhookSecret, StringComparison.Ordinal))
                return Unauthorized();
        }

        // 2. 按 token 识别账号（AccountCredential.Value == bot token）
        var account = AccountContainer.GetAll()
            .FirstOrDefault(a => a.Type == AccountType.Bot && a.Credential.Value == token);
        if (account == null)
            return NotFound();

        // 3. 解析适配器并处理
        var adapter = _resolver.Resolve(account.AccountId) as BotApiAdapter;
        if (adapter == null)
            return NotFound();

        var body = await new StreamReader(Request.Body).ReadToEndAsync(ct).ConfigureAwait(false);
        var update = JsonSerializer.Deserialize<Update>(body, JsonBotAPI.Options);
        if (update == null)
            return BadRequest();

        await adapter.ProcessWebhookAsync(account.AccountId, update, ct).ConfigureAwait(false);
        return Ok();
    }
}
