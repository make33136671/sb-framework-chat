using Sb.Framework.Chat.Account;
using Sb.Framework.Chat.Context;
using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.MessageHandler;

/// <summary>
/// MessageHandler 选项。
/// </summary>
public sealed class TelegramMessageHandlerOptions
{
    /// <summary>是否自动持久化入站消息到 chat_message（防撤回根基）。默认 true。</summary>
    public bool AutoPersist { get; set; } = true;

    /// <summary>是否自动下载图片媒体（撤回前落盘，防撤回保留）。默认 true。</summary>
    public bool AutoDownloadImage { get; set; } = true;
}

/// <summary>
/// 统一消息处理器基类（参考盛派 MessageHandler）。
/// 业务继承此类，override <c>OnXxxRequestAsync</c> 按消息类型处理；
/// 同一套 Handler 同时服务 Bot 与个人号，业务不感知通道。
/// </summary>
/// <typeparam name="TContext">会话上下文类型</typeparam>
public abstract class TelegramMessageHandler<TContext> where TContext : ChatContext, new()
{
    protected TContext Context { get; }
    protected TelegramMessageHandlerOptions Options { get; }

    protected TelegramMessageHandler(TContext context, TelegramMessageHandlerOptions? options = null)
    {
        Context = context;
        Options = options ?? new TelegramMessageHandlerOptions();
    }

    /// <summary>
    /// 主入口：对一条入站消息执行处理流程。
    /// 流程：OnExecuting → 按类型分发 OnXxx → OnExecuted → 返回响应。
    /// </summary>
    public async Task<TelegramResponseMessageBase?> RunAsync(
        TelegramRequestMessageBase request,
        CancellationToken cancellationToken = default)
    {
        Context.LastActiveTime = SB.Framework.Core.Util.AppTime.UtcNow;
        Context.Messages.Add(request);

        // 管道前置钩子（中间件式扩展）
        await OnExecutingAsync(Context, request, cancellationToken).ConfigureAwait(false);

        var response = await DispatchAsync(request, cancellationToken).ConfigureAwait(false);

        // 管道后置钩子
        await OnExecutedAsync(Context, request, response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    /// <summary>按消息类型分发到对应 OnXxx。未识别 → OnUnknownRequestAsync。</summary>
    protected virtual async Task<TelegramResponseMessageBase?> DispatchAsync(
        TelegramRequestMessageBase request, CancellationToken cancellationToken)
    {
        return request switch
        {
            RequestMessageText text => await OnTextRequestAsync(text, cancellationToken).ConfigureAwait(false),
            RequestMessageImage image => await OnImageRequestAsync(image, cancellationToken).ConfigureAwait(false),
            RequestMessageCallback callback => await OnCallbackRequestAsync(callback, cancellationToken).ConfigureAwait(false),
            RequestMessageGroupEvent groupEvent => await OnGroupEventRequestAsync(groupEvent, cancellationToken).ConfigureAwait(false),
            RequestMessageService service => await OnServiceRequestAsync(service, cancellationToken).ConfigureAwait(false),
            _ => await OnUnknownRequestAsync(request, cancellationToken).ConfigureAwait(false)
        };
    }

    // ---- 按类型分发（默认返回 null=不回复）----

    protected virtual Task<TelegramResponseMessageBase?> OnTextRequestAsync(RequestMessageText message, CancellationToken ct)
        => DefaultAsync();

    protected virtual Task<TelegramResponseMessageBase?> OnImageRequestAsync(RequestMessageImage message, CancellationToken ct)
        => DefaultAsync();

    protected virtual Task<TelegramResponseMessageBase?> OnCallbackRequestAsync(RequestMessageCallback message, CancellationToken ct)
        => DefaultAsync();

    protected virtual Task<TelegramResponseMessageBase?> OnGroupEventRequestAsync(RequestMessageGroupEvent message, CancellationToken ct)
        => DefaultAsync();

    /// <summary>服务消息（撤回信号/会话失效等）。防撤回审查可在此处理 withdrawn 标记落库。</summary>
    protected virtual Task<TelegramResponseMessageBase?> OnServiceRequestAsync(RequestMessageService message, CancellationToken ct)
        => DefaultAsync();

    /// <summary>未知/自定义消息类型兜底。可注册自定义 RequestMessage + 对应处理器。</summary>
    protected virtual Task<TelegramResponseMessageBase?> OnUnknownRequestAsync(TelegramRequestMessageBase message, CancellationToken ct)
        => DefaultAsync();

    // ---- 管道钩子（中间件式扩展，默认空实现）----

    protected virtual Task OnExecutingAsync(ChatContext context, TelegramRequestMessageBase request, CancellationToken ct)
        => Task.CompletedTask;

    protected virtual Task OnExecutedAsync(ChatContext context, TelegramRequestMessageBase request, TelegramResponseMessageBase? response, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>默认：不回复（返回 null）。</summary>
    protected Task<TelegramResponseMessageBase?> DefaultAsync()
        => Task.FromResult<TelegramResponseMessageBase?>(null);
}
