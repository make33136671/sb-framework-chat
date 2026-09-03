namespace Sb.Framework.Chat.Adapter;

/// <summary>
/// 媒体自动下载策略提供者（群聊/私聊可单独设置，由宿主应用实现并注册）。
/// 返回 false 表示该场景不自动下载（需前端手动点击下载）。
/// </summary>
public interface IMediaDownloadPolicyProvider
{
    /// <summary>
    /// 是否允许自动下载媒体。
    /// </summary>
    /// <param name="accountId">账号 id</param>
    /// <param name="peerType">"Group" 群聊 / "Private" 私聊</param>
    /// <param name="category">媒体分类：image/video/voice/file/sticker</param>
    bool AllowAutoDownload(string accountId, string peerType, string category);
}
