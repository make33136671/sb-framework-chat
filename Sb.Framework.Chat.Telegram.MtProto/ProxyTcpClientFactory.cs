using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Sb.Framework.Chat;

namespace Sb.Framework.Chat.Telegram.MtProto;

/// <summary>
/// 代理隧道 TcpClient 工厂：按 TelegramProxyOptions 创建已通过代理握手的 TcpClient。
/// 支持 Socks5（含用户名密码认证）和 HTTP CONNECT。
/// 产生的 TcpClient 直接交给 WTelegramClient 作为底层传输。
/// </summary>
internal static class ProxyTcpClientFactory
{
    /// <summary>Telegram 默认接入 DC（DC2）。</summary>
    public const string DefaultDcHost = "149.154.167.51";
    public const int DefaultDcPort = 443;

    /// <summary>
    /// 按代理配置创建已通过代理握手、连接到指定目标地址的 TcpClient。
    /// 由 WTelegramClient 的 TcpHandler 调用：目标地址是 Telegram DC（含 DC 迁移时的任意 DC 地址）。
    /// 代理未启用时返回 null（交由 WTelegramClient 直连）。
    /// </summary>
    public static async Task<TcpClient?> CreateAsync(TelegramProxyOptions? proxy, string host, int port,
        CancellationToken ct = default)
    {
        if (proxy == null || !proxy.Enabled)
            return null;

        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(proxy.Host, proxy.Port, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            tcp.Dispose();
            throw new InvalidOperationException($"连接代理服务器失败 {proxy.Host}:{proxy.Port}：{ex.Message}", ex);
        }

        var stream = tcp.GetStream();
        var type = proxy.Type.Trim().ToUpperInvariant();
        try
        {
            if (type == "SOCKS5")
                await Socks5HandshakeAsync(stream, proxy, host, port, ct).ConfigureAwait(false);
            else if (type == "HTTP")
                await HttpConnectHandshakeAsync(stream, proxy, host, port, ct).ConfigureAwait(false);
            else
                throw new NotSupportedException($"不支持的代理类型: {proxy.Type}（仅支持 Socks5 / Http）");
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        return tcp;
    }

    // ---- SOCKS5 ----

    private static async Task Socks5HandshakeAsync(NetworkStream stream, TelegramProxyOptions proxy,
        string targetHost, int targetPort, CancellationToken ct)
    {
        var hasAuth = !string.IsNullOrEmpty(proxy.Username);

        // 1. Greeting
        var greeting = hasAuth
            ? new byte[] { 0x05, 0x02, 0x00, 0x02 }   // 支持无认证 + 用户名密码
            : new byte[] { 0x05, 0x01, 0x00 };          // 仅无认证
        await stream.WriteAsync(greeting, ct).ConfigureAwait(false);

        var resp = new byte[2];
        await ReadExactAsync(stream, resp, ct).ConfigureAwait(false);
        if (resp[0] != 0x05)
            throw new InvalidOperationException("SOCKS5 握手失败：代理返回非 SOCKS5 版本");

        if (resp[1] == 0xFF)
            throw new InvalidOperationException("SOCKS5 握手失败：代理不接受任何认证方式");

        // 2. 用户名密码认证
        if (resp[1] == 0x02)
        {
            var user = Encoding.UTF8.GetBytes(proxy.Username ?? "");
            var pass = Encoding.UTF8.GetBytes(proxy.Password ?? "");
            if (user.Length > 255 || pass.Length > 255)
                throw new InvalidOperationException("SOCKS5 用户名/密码过长（>255）");

            var authBuf = new byte[3 + user.Length + pass.Length];
            authBuf[0] = 0x01;
            authBuf[1] = (byte)user.Length;
            Buffer.BlockCopy(user, 0, authBuf, 2, user.Length);
            authBuf[2 + user.Length] = (byte)pass.Length;
            Buffer.BlockCopy(pass, 0, authBuf, 3 + user.Length, pass.Length);

            await stream.WriteAsync(authBuf, ct).ConfigureAwait(false);

            var authResp = new byte[2];
            await ReadExactAsync(stream, authResp, ct).ConfigureAwait(false);
            if (authResp[1] != 0x00)
                throw new InvalidOperationException("SOCKS5 认证失败：用户名或密码错误");
        }

        // 3. CONNECT 请求（目标以域名类型发送：兼容域名与 IPv4/IPv6 字面量，代理端可解析任意形式）
        var host = Encoding.UTF8.GetBytes(targetHost);
        var connectReq = new byte[7 + host.Length];
        connectReq[0] = 0x05;   // VER
        connectReq[1] = 0x01;   // CMD=CONNECT
        connectReq[2] = 0x00;   // RSV
        connectReq[3] = 0x03;   // ATYP=DOMAINNAME
        connectReq[4] = (byte)host.Length;
        Buffer.BlockCopy(host, 0, connectReq, 5, host.Length);
        BinaryPrimitives.WriteUInt16BigEndian(connectReq.AsSpan(5 + host.Length), (ushort)targetPort);

        await stream.WriteAsync(connectReq, ct).ConfigureAwait(false);

        // 4. 响应
        var connectResp = new byte[4];
        await ReadExactAsync(stream, connectResp, ct).ConfigureAwait(false);
        if (connectResp[0] != 0x05)
            throw new InvalidOperationException("SOCKS5 CONNECT 失败：版本错误");
        if (connectResp[1] != 0x00)
            throw new InvalidOperationException($"SOCKS5 CONNECT 被拒绝：错误码 {connectResp[1]}");

        // 读取剩余绑定地址（跳过，不使用）
        var addrLen = connectResp[3] switch
        {
            0x01 => 4,      // IPv4
            0x03 => await ReadByteAsync(stream, ct),  // DOMAIN：先读 1 字节长度
            0x04 => 16,     // IPv6
            _ => 0,
        };
        if (addrLen > 0)
        {
            var skip = new byte[addrLen + 2]; // addr + port
            await ReadExactAsync(stream, skip, ct).ConfigureAwait(false);
        }
        else
        {
            var skip = new byte[2]; // port only
            await ReadExactAsync(stream, skip, ct).ConfigureAwait(false);
        }
    }

    // ---- HTTP CONNECT ----

    private static async Task HttpConnectHandshakeAsync(NetworkStream stream, TelegramProxyOptions proxy,
        string targetHost, int targetPort, CancellationToken ct)
    {
        var target = $"{targetHost}:{targetPort}";
        var sb = new StringBuilder();
        sb.Append($"CONNECT {target} HTTP/1.1\r\n");
        sb.Append($"Host: {target}\r\n");
        if (!string.IsNullOrEmpty(proxy.Username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{proxy.Username}:{proxy.Password}"));
            sb.Append($"Proxy-Authorization: Basic {credentials}\r\n");
        }
        sb.Append("\r\n");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);

        // 读取响应头
        var buf = new byte[1024];
        var totalRead = 0;
        while (totalRead < buf.Length)
        {
            var read = await stream.ReadAsync(buf.AsMemory(totalRead), ct).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
            // 检查 \r\n\r\n 结束
            var header = Encoding.UTF8.GetString(buf, 0, totalRead);
            if (header.Contains("\r\n\r\n"))
            {
                var statusLine = header[..header.IndexOf("\r\n")];
                if (!statusLine.Contains(" 200 "))
                    throw new InvalidOperationException($"HTTP CONNECT 失败：{statusLine}");
                return;
            }
        }
        throw new InvalidOperationException("HTTP CONNECT 失败：代理未返回完整响应");
    }

    // ---- IO 辅助 ----

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct)
                .ConfigureAwait(false);
            if (read == 0)
                throw new InvalidOperationException("代理连接意外断开");
            offset += read;
        }
    }

    private static async Task<byte> ReadByteAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[1];
        await ReadExactAsync(stream, buf, ct).ConfigureAwait(false);
        return buf[0];
    }
}
