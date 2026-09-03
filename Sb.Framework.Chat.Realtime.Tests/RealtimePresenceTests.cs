using Microsoft.Extensions.Logging.Abstractions;
using Sb.Framework.Chat.Realtime.Messages;
using Sb.Framework.Chat.Realtime.Options;
using Sb.Framework.Chat.Realtime.Presence;

namespace Sb.Framework.Chat.Realtime.Tests;

public class RealtimePresenceTests
{
    private static PresenceService Create(int timeoutSeconds = 60)
    {
        var options = new RealtimeOptions { HeartbeatTimeout = TimeSpan.FromSeconds(timeoutSeconds) };
        return new PresenceService(options, NullLogger<PresenceService>.Instance);
    }

    [Fact]
    public void MarkOnline_ThenIsOnline_True()
    {
        var svc = Create();
        svc.MarkOnline("u1", "c1");
        Assert.True(svc.IsOnline("u1"));
    }

    [Fact]
    public void UnknownUser_IsOffline()
    {
        var svc = Create();
        Assert.False(svc.IsOnline("nobody"));
    }

    [Fact]
    public void MarkOffline_LastConnection_GoesOffline()
    {
        var svc = Create();
        svc.MarkOnline("u1", "c1");
        svc.MarkOnline("u1", "c2");
        svc.MarkOffline("u1", "c1");
        Assert.True(svc.IsOnline("u1")); // 还有一条连接

        svc.MarkOffline("u1", "c2");
        Assert.False(svc.IsOnline("u1"));
    }

    [Fact]
    public void HeartbeatTimeout_MarksOffline()
    {
        var svc = Create(timeoutSeconds: 1);
        svc.MarkOnline("u1", "c1");
        Assert.True(svc.IsOnline("u1"));

        Thread.Sleep(1200);
        Assert.False(svc.IsOnline("u1"));
    }

    [Fact]
    public void BatchQuery_ReturnsMap()
    {
        var svc = Create();
        svc.MarkOnline("u1", "c1");

        var map = svc.IsOnline(new[] { "u1", "u2" });
        Assert.True(map["u1"]);
        Assert.False(map["u2"]);
    }
}

public class SystemMessageTypeTests
{
    [Theory]
    [InlineData("sys.session_init", true)]
    [InlineData("sys.welcome", true)]
    [InlineData("text", false)]
    [InlineData("", false)]
    public void IsSystem_DetectsPrefix(string contentType, bool expected)
        => Assert.Equal(expected, SystemMessageType.IsSystem(contentType));
}
