using System.Reflection;
using System.Text.Json;
using SB.Chat.Models;
using SB.Chat.Telegram;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace SB.Chat.Tests.Telegram;

/// <summary>
/// 测试 ProcessUpdate 的事件映射逻辑
/// 通过反射调用 private ProcessUpdate，用 JSON 反序列化构造 Telegram.Bot 类型
/// </summary>
public class TelegramBotConnectorWebhookTests
{
    private readonly TelegramBotConnector _bot;
    private readonly MethodInfo _processUpdate;

    public TelegramBotConnectorWebhookTests()
    {
        _bot = new TelegramBotConnector("dummy-token");
        _processUpdate = typeof(TelegramBotConnector)
            .GetMethod("ProcessUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    private void InvokeProcessUpdate(Update update) =>
        _processUpdate.Invoke(_bot, new object[] { update });

    private static Update DeserializeUpdate(string json) =>
        JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options)!;

    [Fact]
    public void TextMessage_FiresOnMessage()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 1,
            "message": {
                "message_id": 42,
                "text": "Hello world",
                "chat": { "id": 100, "type": "private" },
                "from": { "id": 999, "is_bot": false, "first_name": "Alice", "last_name": "Wang" },
                "date": 1700000000
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal("100", received!.ChatId);
        Assert.Equal("42", received.MessageId);
        Assert.Equal("999", received.SenderId);
        Assert.Equal("Alice Wang", received.SenderName);
        Assert.Equal("Hello world", received.Content);
        Assert.Equal(ChatMessageType.Text, received.MessageType);
        Assert.True(received.IsPrivateChat);
        Assert.Empty(received.Media);
    }

    [Fact]
    public void GroupMessage_IsPrivateChat_False()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 2,
            "message": {
                "message_id": 1,
                "text": "group msg",
                "chat": { "id": 200, "type": "group", "title": "Dev Team" },
                "from": { "id": 10, "is_bot": false, "first_name": "Bob" },
                "date": 1700000000
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.False(received!.IsPrivateChat);
        Assert.Equal("Dev Team", received.ChatTitle);
    }

    [Fact]
    public void PhotoMessage_ExtractsMedia()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 3,
            "message": {
                "message_id": 2,
                "caption": "nice photo",
                "chat": { "id": 300, "type": "private" },
                "from": { "id": 11, "is_bot": false, "first_name": "Carol" },
                "date": 1700000000,
                "photo": [
                    { "file_id": "small", "file_unique_id": "s1", "width": 100, "height": 100, "file_size": 500 },
                    { "file_id": "large", "file_unique_id": "l1", "width": 800, "height": 600, "file_size": 5000 }
                ]
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal("nice photo", received!.Content);
        Assert.Equal(ChatMessageType.Image, received.MessageType);
        Assert.Single(received.Media);
        Assert.Equal("large", received.Media[0].FileId);
        Assert.Equal(ChatMessageType.Image, received.Media[0].MediaType);
        Assert.Equal("image/jpeg", received.Media[0].MimeType);
    }

    [Fact]
    public void DocumentMessage_ExtractsMedia()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 4,
            "message": {
                "message_id": 3,
                "chat": { "id": 400, "type": "private" },
                "from": { "id": 12, "is_bot": false, "first_name": "Dave" },
                "date": 1700000000,
                "document": {
                    "file_id": "doc1", "file_unique_id": "du1",
                    "file_name": "report.pdf", "file_size": 10240,
                    "mime_type": "application/pdf"
                }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal(ChatMessageType.File, received!.MessageType);
        Assert.Single(received.Media);
        Assert.Equal("doc1", received.Media[0].FileId);
        Assert.Equal("report.pdf", received.Media[0].FileName);
        Assert.Equal(10240, received.Media[0].FileSize);
        Assert.Equal("application/pdf", received.Media[0].MimeType);
    }

    [Fact]
    public void AudioMessage_ExtractsMedia()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 5,
            "message": {
                "message_id": 4,
                "chat": { "id": 500, "type": "private" },
                "from": { "id": 13, "is_bot": false, "first_name": "Eve" },
                "date": 1700000000,
                "audio": {
                    "file_id": "audio1", "file_unique_id": "au1",
                    "duration": 120,
                    "file_name": "song.mp3", "file_size": 4096,
                    "mime_type": "audio/mpeg"
                }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal(ChatMessageType.Audio, received!.MessageType);
        Assert.Single(received.Media);
        Assert.Equal("audio1", received.Media[0].FileId);
        Assert.Equal(ChatMessageType.Audio, received.Media[0].MediaType);
    }

    [Fact]
    public void VoiceMessage_DetectsAsAudio()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 6,
            "message": {
                "message_id": 5,
                "chat": { "id": 600, "type": "private" },
                "from": { "id": 14, "is_bot": false, "first_name": "Frank" },
                "date": 1700000000,
                "voice": { "file_id": "voice1", "file_unique_id": "v1", "duration": 5, "file_size": 2048 }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal(ChatMessageType.Audio, received!.MessageType);
        Assert.Single(received.Media);
        Assert.Equal("audio/ogg", received.Media[0].MimeType);
    }

    [Fact]
    public void VideoMessage_ExtractsMedia()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 7,
            "message": {
                "message_id": 6,
                "chat": { "id": 700, "type": "private" },
                "from": { "id": 15, "is_bot": false, "first_name": "Grace" },
                "date": 1700000000,
                "video": {
                    "file_id": "vid1", "file_unique_id": "vi1",
                    "width": 1920, "height": 1080, "duration": 30,
                    "file_name": "clip.mp4", "file_size": 102400
                }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal(ChatMessageType.Video, received!.MessageType);
        Assert.Single(received.Media);
        Assert.Equal("vid1", received.Media[0].FileId);
    }

    [Fact]
    public void StickerMessage_ExtractsMedia()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 8,
            "message": {
                "message_id": 7,
                "chat": { "id": 800, "type": "private" },
                "from": { "id": 16, "is_bot": false, "first_name": "Hank" },
                "date": 1700000000,
                "sticker": {
                    "file_id": "stk1", "file_unique_id": "st1",
                    "type": "regular",
                    "width": 512, "height": 512, "is_animated": false, "is_video": false
                }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal(ChatMessageType.Sticker, received!.MessageType);
        Assert.Single(received.Media);
        Assert.Equal("image/webp", received.Media[0].MimeType);
    }

    [Fact]
    public void CallbackQuery_FiresOnCallback()
    {
        ChatCallbackEvent? received = null;
        _bot.OnCallback += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 9,
            "callback_query": {
                "id": "cb123",
                "from": { "id": 20, "is_bot": false, "first_name": "Ivy" },
                "data": "menu:my_tasks",
                "chat_instance": "xxx",
                "message": {
                    "message_id": 50,
                    "chat": { "id": 900, "type": "private" },
                    "date": 1700000000
                }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal("cb123", received!.CallbackId);
        Assert.Equal("900", received.ChatId);
        Assert.Equal("50", received.MessageId);
        Assert.Equal("20", received.UserId);
        Assert.Equal("menu:my_tasks", received.CallbackData);
    }

    [Fact]
    public void MyChatMember_BotAdded_FiresOnGroupEvent()
    {
        ChatGroupEvent? received = null;
        _bot.OnGroupEvent += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 10,
            "my_chat_member": {
                "chat": { "id": 1000, "type": "group", "title": "Project Chat" },
                "from": { "id": 30, "is_bot": false, "first_name": "Admin" },
                "date": 1700000000,
                "old_chat_member": { "status": "left", "user": { "id": 1, "is_bot": true, "first_name": "Bot" } },
                "new_chat_member": { "status": "member", "user": { "id": 1, "is_bot": true, "first_name": "Bot" } }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal(ChatGroupEventType.BotAdded, received!.EventType);
        Assert.Equal("1000", received.ChatId);
        Assert.Equal("Project Chat", received.ChatTitle);
    }

    [Fact]
    public void MyChatMember_BotRemoved_FiresOnGroupEvent()
    {
        ChatGroupEvent? received = null;
        _bot.OnGroupEvent += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 11,
            "my_chat_member": {
                "chat": { "id": 1001, "type": "group", "title": "Old Group" },
                "from": { "id": 31, "is_bot": false, "first_name": "Admin" },
                "date": 1700000000,
                "old_chat_member": { "status": "member", "user": { "id": 1, "is_bot": true, "first_name": "Bot" } },
                "new_chat_member": { "status": "left", "user": { "id": 1, "is_bot": true, "first_name": "Bot" } }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal(ChatGroupEventType.BotRemoved, received!.EventType);
        Assert.Equal("1001", received.ChatId);
    }

    [Fact]
    public void ReplyMessage_ExtractsReplyToMessageId()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 12,
            "message": {
                "message_id": 10,
                "text": "reply text",
                "chat": { "id": 1100, "type": "group" },
                "from": { "id": 40, "is_bot": false, "first_name": "Jake" },
                "date": 1700000000,
                "reply_to_message": { "message_id": 8, "chat": { "id": 1100, "type": "group" }, "date": 1700000000 }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal("8", received!.ReplyToMessageId);
    }

    [Fact]
    public void SenderName_FirstNameOnly()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 13,
            "message": {
                "message_id": 11,
                "text": "hi",
                "chat": { "id": 1200, "type": "private" },
                "from": { "id": 50, "is_bot": false, "first_name": "Kim" },
                "date": 1700000000
            }
        }
        """);

        InvokeProcessUpdate(update);
        Assert.Equal("Kim", received!.SenderName);
    }

    [Fact]
    public void SenderName_FallbackToUsername()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 14,
            "message": {
                "message_id": 12,
                "text": "hi",
                "chat": { "id": 1300, "type": "private" },
                "from": { "id": 60, "is_bot": false, "first_name": "", "username": "cool_user" },
                "date": 1700000000
            }
        }
        """);

        InvokeProcessUpdate(update);
        Assert.Equal("cool_user", received!.SenderName);
    }

    [Fact]
    public void NullMessage_NoEventFired()
    {
        var messageFired = false;
        var callbackFired = false;
        var groupFired = false;
        _bot.OnMessage += (_, _) => messageFired = true;
        _bot.OnCallback += (_, _) => callbackFired = true;
        _bot.OnGroupEvent += (_, _) => groupFired = true;

        InvokeProcessUpdate(new Update());

        Assert.False(messageFired);
        Assert.False(callbackFired);
        Assert.False(groupFired);
    }

    [Fact]
    public void EventSubscription_CanAttachAndDetach()
    {
        var count = 0;
        void Handler(object? s, ChatMessageEvent e) => count++;

        _bot.OnMessage += Handler;

        var update = DeserializeUpdate("""
        {
            "update_id": 15,
            "message": {
                "message_id": 99,
                "text": "test",
                "chat": { "id": 1400, "type": "private" },
                "from": { "id": 70, "is_bot": false, "first_name": "Test" },
                "date": 1700000000
            }
        }
        """);

        InvokeProcessUpdate(update);
        Assert.Equal(1, count);

        _bot.OnMessage -= Handler;
        InvokeProcessUpdate(update);
        Assert.Equal(1, count); // not incremented
    }

    [Fact]
    public void VideoNoteMessage_DetectsAsVideo()
    {
        ChatMessageEvent? received = null;
        _bot.OnMessage += (_, e) => received = e;

        var update = DeserializeUpdate("""
        {
            "update_id": 16,
            "message": {
                "message_id": 20,
                "chat": { "id": 1500, "type": "private" },
                "from": { "id": 80, "is_bot": false, "first_name": "Leo" },
                "date": 1700000000,
                "video_note": { "file_id": "vn1", "file_unique_id": "vnu1", "length": 240, "duration": 10, "file_size": 8192 }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.NotNull(received);
        Assert.Equal(ChatMessageType.Video, received!.MessageType);
        Assert.Single(received.Media);
        Assert.Equal("vn1", received.Media[0].FileId);
        Assert.Equal("video/mp4", received.Media[0].MimeType);
    }

    [Fact]
    public void MyChatMember_NoStatusChange_NoEvent()
    {
        ChatGroupEvent? received = null;
        _bot.OnGroupEvent += (_, e) => received = e;

        // member -> administrator: not BotAdded/BotRemoved
        var update = DeserializeUpdate("""
        {
            "update_id": 17,
            "my_chat_member": {
                "chat": { "id": 1600, "type": "group", "title": "Some Group" },
                "from": { "id": 90, "is_bot": false, "first_name": "Admin" },
                "date": 1700000000,
                "old_chat_member": { "status": "member", "user": { "id": 1, "is_bot": true, "first_name": "Bot" } },
                "new_chat_member": { "status": "administrator", "user": { "id": 1, "is_bot": true, "first_name": "Bot" } }
            }
        }
        """);

        InvokeProcessUpdate(update);

        Assert.Null(received); // no BotAdded or BotRemoved
    }
}
