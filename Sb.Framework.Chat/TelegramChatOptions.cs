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
}

/// <summary>断线重连参数（对齐 Core RetryUtil）。</summary>
public sealed class TelegramReconnectOptions
{
    public int MaxRetries { get; set; } = 5;

    public int BaseBackoffMs { get; set; } = 1000;

    public int MaxBackoffMs { get; set; } = 30000;
}
