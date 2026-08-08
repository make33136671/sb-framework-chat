using Sb.Framework.Chat.Context;
using Sb.Framework.Chat.MessageHandler;
using Sb.Framework.Chat.Model;

namespace Sb.Framework.Chat.Tests;

public class TelegramMessageHandlerTests
{
    private sealed class EchoHandler : TelegramMessageHandler<ChatContext>
    {
        public int TextCount, ImageCount, UnknownCount, ExecutingCount, ExecutedCount;

        public EchoHandler(ChatContext ctx) : base(ctx) { }

        protected override Task<TelegramResponseMessageBase?> OnTextRequestAsync(RequestMessageText message, CancellationToken ct)
        {
            TextCount++;
            return Task.FromResult<TelegramResponseMessageBase?>(new ResponseMessageText { Content = $"echo:{message.Content}" });
        }

        protected override Task<TelegramResponseMessageBase?> OnImageRequestAsync(RequestMessageImage message, CancellationToken ct)
        {
            ImageCount++;
            return DefaultAsync();
        }

        protected override Task<TelegramResponseMessageBase?> OnUnknownRequestAsync(TelegramRequestMessageBase message, CancellationToken ct)
        {
            UnknownCount++;
            return DefaultAsync();
        }

        protected override Task OnExecutingAsync(ChatContext context, TelegramRequestMessageBase request, CancellationToken ct)
        {
            ExecutingCount++;
            return Task.CompletedTask;
        }

        protected override Task OnExecutedAsync(ChatContext context, TelegramRequestMessageBase request, TelegramResponseMessageBase? response, CancellationToken ct)
        {
            ExecutedCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RunAsync_Text_DispatchesAndReturnsEcho()
    {
        var ctx = new ChatContext { AccountId = "a1", PeerId = "p1" };
        var handler = new EchoHandler(ctx);
        var msg = new RequestMessageText { AccountId = "a1", PeerId = "p1", MessageId = 1, Content = "hello" };

        var response = await handler.RunAsync(msg);

        Assert.IsType<ResponseMessageText>(response);
        Assert.Equal("echo:hello", ((ResponseMessageText)response!).Content);
        Assert.Equal(1, handler.TextCount);
    }

    [Fact]
    public async Task RunAsync_AddsToContextAndRunsHooks()
    {
        var ctx = new ChatContext { AccountId = "a1", PeerId = "p1" };
        var handler = new EchoHandler(ctx);
        var msg = new RequestMessageText { AccountId = "a1", PeerId = "p1", MessageId = 7, Content = "x" };

        await handler.RunAsync(msg);

        Assert.Equal(1, handler.ExecutingCount);
        Assert.Equal(1, handler.ExecutedCount);
        Assert.Contains(ctx.Messages.Snapshot(), m => m.MessageId == 7);
    }

    [Fact]
    public async Task RunAsync_Image_DispatchesToImage_NoReply()
    {
        var ctx = new ChatContext();
        var handler = new EchoHandler(ctx);
        var msg = new RequestMessageImage { AccountId = "a1", PeerId = "p1", MessageId = 2, Media = new ImageMedia() };

        var response = await handler.RunAsync(msg);

        Assert.Null(response);
        Assert.Equal(1, handler.ImageCount);
    }

    [Fact]
    public async Task RunAsync_UnknownType_FallsBackToUnknown()
    {
        // 自定义未注册消息类型：既有 switch 无对应分支 → OnUnknownRequestAsync 兜底
        var ctx = new ChatContext();
        var handler = new EchoHandler(ctx);
        var custom = new CustomRequestMessage { AccountId = "a1", PeerId = "p1", MessageId = 4 };

        var response = await handler.RunAsync(custom);

        Assert.Null(response);
        Assert.Equal(1, handler.UnknownCount);
    }

    /// <summary>自定义消息类型（SDK 未内置，应落入未知兜底）。</summary>
    private sealed class CustomRequestMessage : TelegramRequestMessageBase
    {
    }
}
