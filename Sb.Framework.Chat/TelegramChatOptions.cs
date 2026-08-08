namespace Sb.Framework.Chat;

/// <summary>
/// Telegram SDK 配置项（appsettings.json 节：Chat）。
/// </summary>
public sealed class TelegramChatOptions
{
    /// <summary>会话上下文内存容量（ChatContext.Messages 有界长度）。</summary>
    public int ContextCacheCapacity { get; set; } = 50;

    /// <summary>媒体落盘根路径（相对 wwwroot）。</summary>
    public string MediaStoragePath { get; set; } = "/storage/chat/";

    /// <summary>Bot 通道配置。</summary>
    public TelegramBotOptions Bot { get; set; } = new();

    /// <summary>个人号通道配置。</summary>
    public TelegramMtProtoOptions MtProto { get; set; } = new();
}

/// <summary>Bot 通道配置。</summary>
public sealed class TelegramBotOptions
{
    /// <summary>统一 Webhook 前缀（如 /telegram/webhook）。</summary>
    public string PathPrefix { get; set; } = "/telegram/webhook";

    /// <summary>Webhook 公网基地址（配置到 Bot 时用）。</summary>
    public string? WebhookBaseUrl { get; set; }

    /// <summary>Webhook secret_token（HMAC 验签用）。</summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>个人号通道配置。</summary>
public sealed class TelegramMtProtoOptions
{
    /// <summary>MTProto api_id（my.telegram.org 注册应用得到）。</summary>
    public string? ApiId { get; set; }

    /// <summary>MTProto api_hash。</summary>
    public string? ApiHash { get; set; }

    /// <summary>会话存储方式：Db / File / EncryptedFile。</summary>
    public string SessionStore { get; set; } = "Db";

    /// <summary>断线重连参数。</summary>
    public TelegramReconnectOptions Reconnect { get; set; } = new();

    /// <summary>代理配置（默认不启用，直连 Telegram DC）。</summary>
    public TelegramProxyOptions Proxy { get; set; } = new();

    /// <summary>防撤回：入站媒体自动下载配置。</summary>
    public AutoDownloadOptions AutoDownload { get; set; } = new();
}

/// <summary>
/// Telegram 连接代理配置。默认不启用（Enabled=false），直连。
/// 配置后 MTProto 适配器通过代理隧道连接 Telegram DC 服务器。
/// </summary>
public sealed class TelegramProxyOptions
{
    /// <summary>代理类型：Socks5 / Http。空字符串=禁用。</summary>
    public string Type { get; set; } = "";

    /// <summary>代理服务器地址。</summary>
    public string Host { get; set; } = "";

    /// <summary>代理服务器端口。</summary>
    public int Port { get; set; }

    /// <summary>代理认证用户名（可选）。</summary>
    public string? Username { get; set; }

    /// <summary>代理认证密码（可选）。</summary>
    public string? Password { get; set; }

    /// <summary>是否启用代理。</summary>
    public bool Enabled =>
        !string.IsNullOrWhiteSpace(Type)
        && !string.IsNullOrWhiteSpace(Host)
        && Port > 0;
}

/// <summary>
/// 防撤回：入站媒体自动下载配置。
/// 消息到达时自动下载媒体到服务器，防止发送方撤回后无法获取。
/// </summary>
public sealed class AutoDownloadOptions
{
    /// <summary>是否启用自动下载（默认开启）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>最大文件大小（MB），超过则跳过。默认 50MB。</summary>
    public long MaxFileSizeMB { get; set; } = 50;

    /// <summary>自动下载图片。</summary>
    public bool DownloadImages { get; set; } = true;

    /// <summary>自动下载视频。</summary>
    public bool DownloadVideos { get; set; } = true;

    /// <summary>自动下载语音消息。</summary>
    public bool DownloadVoice { get; set; } = true;

    /// <summary>自动下载普通文件。</summary>
    public bool DownloadFiles { get; set; } = true;

    /// <summary>自动下载贴纸（默认关闭，体积小但数量多）。</summary>
    public bool DownloadStickers { get; set; } = false;

    /// <summary>允许的文件扩展名白名单（如 [".pdf", ".doc", ".zip"]）。留空=允许全部。</summary>
    public List<string> AllowedExtensions { get; set; } = new();

    /// <summary>禁止的文件扩展名黑名单（如 [".exe", ".bat"]）。</summary>
    public List<string> BlockedExtensions { get; set; } = new();
}

/// <summary>断线重连参数（对齐 Core RetryUtil）。</summary>
public sealed class TelegramReconnectOptions
{
    public int MaxRetries { get; set; } = 5;

    public int BaseBackoffMs { get; set; } = 1000;

    public int MaxBackoffMs { get; set; } = 30000;
}
