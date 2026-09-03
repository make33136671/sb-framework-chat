using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Sb.Framework.Chat.Realtime.Auth;
using Sb.Framework.Chat.Realtime.Conversations;
using Sb.Framework.Chat.Realtime.Messages;
using Sb.Framework.Chat.Realtime.Options;
using Sb.Framework.Chat.Realtime.Presence;
using Sb.Framework.Chat.Realtime.ReadState;
using System.Security.Claims;

namespace Sb.Framework.Chat.Realtime.Hub;

/// <summary>
/// 实时通讯 Hub。客户端通过 WebSocket 连接（sb-framework 支持 query access_token 鉴权）。
/// 事件约定：服务端 → 客户端推送 OnMessage / OnReadReceipt。
/// </summary>
[Authorize(AuthenticationSchemes = "JwtAdmin")]
public class RealtimeHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly IRealtimeAuthProvider _auth;
    private readonly IRealtimeUserProvider _userProvider;
    private readonly IPresenceService _presence;
    private readonly IMessageService _messages;
    private readonly IMessageDeliveryService _delivery;
    private readonly IReadStateService _readState;
    private readonly IConversationService _conversations;
    private readonly RealtimeOptions _options;
    private readonly ILogger _logger;

    public RealtimeHub(
        IRealtimeAuthProvider auth,
        IRealtimeUserProvider userProvider,
        IPresenceService presence,
        IMessageService messages,
        IMessageDeliveryService delivery,
        IReadStateService readState,
        IConversationService conversations,
        RealtimeOptions options,
        ILogger<RealtimeHub> logger)
    {
        _auth = auth;
        _userProvider = userProvider;
        _presence = presence;
        _messages = messages;
        _delivery = delivery;
        _readState = readState;
        _conversations = conversations;
        _options = options;
        _logger = logger;
    }

    /// <summary>当前用户 id：优先取 IUserIdProvider 结果（派生项目可自定义映射），否则回退 JWT Id 声明。</summary>
    private string CurrentUserId
    {
        get
        {
            if (!string.IsNullOrEmpty(Context.UserIdentifier)) return Context.UserIdentifier;
            var idClaim = Context.User?.FindFirstValue("Id");
            if (!string.IsNullOrEmpty(idClaim)) return idClaim;
            return Context.ConnectionId;
        }
    }

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId;
        if (!_auth.ValidateConnection(new RealtimeConnectionContext
        {
            UserId = userId,
            ConnectionId = Context.ConnectionId,
            Query = Context.GetHttpContext()?.Request.Query
                .ToDictionary(k => k.Key, v => (string?)v.Value.ToString()),
        }))
        {
            _logger.LogWarning("Realtime 连接鉴权失败: UserId={UserId}", userId);
            Context.Abort();
            return;
        }

        _presence.MarkOnline(userId, Context.ConnectionId);

        // 加入用户个人通知组 agent:{userId}，用于接收新会话通知等推送
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{userId}");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = CurrentUserId;
        _logger.LogDebug("Hub 断开连接: UserId={UserId}, ConnectionId={ConnectionId}, Exception={Exception}",
            userId, Context.ConnectionId, exception?.Message ?? "none");
        _presence.MarkOffline(userId, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>客户端 → 服务器：发送消息（参数兼容 JS SDK：string conversationId, int contentType, string? content）。</summary>
    public async Task<RealtimeMessage?> SendMessage(string conversationId, int contentType, string? content)
    {
        if (!long.TryParse(conversationId, out var convId))
        {
            _logger.LogWarning("SendMessage: 无效的 conversationId={ConversationId}", conversationId);
            return null;
        }

        // 自动加入会话组（确保发送者也能收到自己发的消息广播）
        await Groups.AddToGroupAsync(Context.ConnectionId, MessageDeliveryService.GroupName(convId));

        var ctStr = MapContentType(contentType);

        var msg = await _messages.SendAsync(new RealtimeMessage
        {
            ConversationId = convId,
            SenderId = CurrentUserId,
            ContentType = ctStr,
            Content = content,
        });
        return msg;
    }

    /// <summary>客户端 → 服务器：标记已读并广播回执。</summary>
    [Obsolete("请使用 AcknowledgeRead 替代。")]
    public async Task MarkRead(string conversationId, long lastSeq)
    {
        await AcknowledgeRead(conversationId, lastSeq);
    }

    /// <summary>客户端 → 服务器：确认送达。</summary>
    public async Task AcknowledgeDelivery(string conversationId, long seq)
    {
        if (!long.TryParse(conversationId, out var convId)) return;

        var ok = await _messages.MarkDeliveredAsync(convId, CurrentUserId, seq);
        if (ok)
        {
            await _delivery.DeliverDeliveryReceiptAsync(new DeliveryReceipt
            {
                ConversationId = convId,
                UserId = CurrentUserId,
                Seq = seq,
            });
        }
    }

    /// <summary>客户端 → 服务器：确认已读并广播回执（替代 MarkRead）。</summary>
    public async Task AcknowledgeRead(string conversationId, long lastReadSeq)
    {
        if (!long.TryParse(conversationId, out var convId)) return;

        await _readState.MarkReadAsync(convId, CurrentUserId, lastReadSeq);
        var max = await _messages.GetHistoryAsync(convId, 0, 1);
        var maxSeq = max.Count > 0 ? max[0].Seq : lastReadSeq;
        await _delivery.DeliverReadReceiptAsync(new ReadReceipt
        {
            ConversationId = convId,
            ReaderId = CurrentUserId,
            LastReadSeq = maxSeq,
        });
    }

    /// <summary>客户端 → 服务器：撤回消息。</summary>
    public async Task<RealtimeMessage?> WithdrawMessage(string conversationId, long seq)
    {
        if (!long.TryParse(conversationId, out var convId)) return null;

        var msg = await _messages.WithdrawAsync(convId, seq, CurrentUserId);
        return msg;
    }

    /// <summary>客户端 → 服务器：编辑消息。</summary>
    public async Task<RealtimeMessage?> EditMessage(string conversationId, long seq, string newContent)
    {
        if (!long.TryParse(conversationId, out var convId)) return null;

        var msg = await _messages.EditAsync(convId, seq, CurrentUserId, newContent);
        return msg;
    }

    /// <summary>客户端 → 服务器：删除消息（软删除）。</summary>
    public async Task<bool> DeleteMessage(string conversationId, long seq)
    {
        if (!long.TryParse(conversationId, out var convId)) return false;

        return await _messages.DeleteAsync(convId, seq, CurrentUserId);
    }

    /// <summary>客户端 → 服务器：加入会话组（开始监听，广播模式多客服加入同一会话）。</summary>
    public async Task JoinConversation(string conversationId)
    {
        if (!long.TryParse(conversationId, out var convId)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, MessageDeliveryService.GroupName(convId));
        _logger.LogDebug("用户 {UserId} 加入会话组 {ConversationId}", CurrentUserId, convId);
    }

    /// <summary>客户端 → 服务器：离开会话组。</summary>
    public async Task LeaveConversation(string conversationId)
    {
        if (!long.TryParse(conversationId, out var convId)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, MessageDeliveryService.GroupName(convId));
    }

    /// <summary>客户端 → 服务器：拉取历史消息（返回给调用者）。自动加入会话组以接收后续推送。</summary>
    public async Task<IReadOnlyList<RealtimeMessage>> GetHistory(string conversationId, long beforeSeq = 0, int count = 50)
    {
        if (!long.TryParse(conversationId, out var convId)) return Array.Empty<RealtimeMessage>();

        // 自动加入会话组，确保后续消息能推送到当前连接
        await Groups.AddToGroupAsync(Context.ConnectionId, MessageDeliveryService.GroupName(convId));

        if (count <= 0 || count > _options.MaxHistoryBatch) count = _options.MaxHistoryBatch;
        return await _messages.GetHistoryAsync(convId, beforeSeq, count);
    }

    /// <summary>客户端 → 服务器：获取会话列表（当前用户参与的会话）。</summary>
    public async Task<IReadOnlyList<RealtimeConversation>> GetConversationList(int count = 100)
    {
        var list = await _conversations.GetConversationsAsync(CurrentUserId);
        if (count > 0 && list.Count > count)
            return list.Take(count).ToList();
        return list;
    }

    /// <summary>客户端 → 服务器：根据用户 ID 获取单个会话（查找当前用户与目标用户的共同会话）。</summary>
    public async Task<RealtimeConversation?> GetOneConversation(string userID, int sessionType = 1)
    {
        if (string.IsNullOrEmpty(userID)) return null;

        // sessionType=3 (group): userID 是会话 ID，直接查询
        if (sessionType == 3 && long.TryParse(userID, out var convId))
        {
            return await _conversations.GetAsync(convId, CurrentUserId);
        }

        // sessionType=1 (direct): 查找当前用户与目标用户的共同会话
        var myConversations = await _conversations.GetConversationsAsync(CurrentUserId);
        foreach (var conv in myConversations)
        {
            var members = await _conversations.GetMemberIdsAsync(conv.Id);
            if (members.Contains(userID))
            {
                return await _conversations.GetAsync(conv.Id, CurrentUserId);
            }
        }
        return null;
    }

    /// <summary>客户端 → 服务器：获取用户信息。</summary>
    public Task<RealtimeUserInfo?> GetUserInfo(string userId)
    {
        return Task.FromResult(_userProvider.Resolve(userId));
    }

    /// <summary>客户端 → 服务器：发送系统消息。</summary>
    public async Task<RealtimeMessage?> SendSystemMessage(string conversationId, string type, string? payload)
    {
        if (!long.TryParse(conversationId, out var convId)) return null;

        await Groups.AddToGroupAsync(Context.ConnectionId, MessageDeliveryService.GroupName(convId));

        return await _messages.SendSystemAsync(convId, type, payload, CurrentUserId);
    }

    /// <summary>客户端 → 服务器：心跳保活。客户端定期调用以更新最后活跃时间。</summary>
    public Task Ping()
    {
        _presence.UpdateHeartbeat(CurrentUserId);
        return Task.CompletedTask;
    }

    /// <summary>JS MessageType 数字 → C# contentType 字符串。</summary>
    private static string MapContentType(int contentType) => contentType switch
    {
        101 => "text",
        102 => "image",
        103 => "voice",
        104 => "video",
        105 => "file",
        106 => "at_text",
        110 => "custom",
        113 => "typing",
        114 => "quote",
        115 => "face",
        _ => "text",
    };

    /// <summary>
    /// 校验并规范化 contentType 命名空间格式。
    /// 支持的命名空间前缀：system.* / call.* / interact.* / custom.*。
    /// 未知的命名空间类型返回 "custom" 兜底。
    /// </summary>
    private static string NormalizeContentType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return "custom";

        // 已知的基础类型直接返回
        var baseTypes = new HashSet<string>
        {
            "text", "image", "voice", "video", "file",
            "at_text", "custom", "typing", "quote", "face",
        };
        if (baseTypes.Contains(contentType)) return contentType;

        // 命名空间格式校验
        if (contentType.Contains('.'))
        {
            var prefix = contentType.Split('.')[0];
            return prefix switch
            {
                "system" or "call" or "interact" or "custom" => contentType,
                _ => "custom",
            };
        }

        // 无命名空间的未知类型，兜底为 custom
        return "custom";
    }
}
