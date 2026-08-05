# SB Framework Chat

> SB Framework 的 Telegram 统一消息 SDK — 双通道（Bot API + MTProto 个人号）全能开发框架。
> 服务两套独立系统（商家入驻平台 + 内部合规系统），作为两者共享的统一地基。
> 设计参考盛派微信 SDK（强类型消息 + MessageHandler 分发 + 容器/缓存 + 模块化）。

## 内容

| 工程 | 内容 |
|------|------|
| `Sb.Framework.Chat` | 统一核心：消息模型 / MessageHandler / ChatContext（防撤回载体）/ AccountContainer / 存储（chat_* 6 表）/ 通知推送 / 发送队列 / Mock 适配器 / 健康检查 |
| `Sb.Framework.Chat.Telegram` | Bot 通道适配器（Telegram.Bot）：webhook/polling、命令/回调/菜单、图文发送、统一 Webhook 端点 |
| `Sb.Framework.Chat.Telegram.MtProto` | 个人号通道适配器（WTelegramClient）：会话/认证流程、Update 流、历史补拉、**防撤回**、媒体下载 |

> 旧 `SB.Chat / SB.Chat.Telegram / SB.Chat.Tests` 已**弃用删除**（评审结论：抽象太薄、协议泄漏），由本 SDK 取代。

## 派生项目怎么用

```xml
<PackageReference Include="Sb.Framework.WebApi" Version="0.4.14" />
<PackageReference Include="Sb.Framework.Chat" Version="0.1.0-preview.1" />
<PackageReference Include="Sb.Framework.Chat.Telegram" Version="0.1.0-preview.1" />        <!-- Bot 通道 -->
<PackageReference Include="Sb.Framework.Chat.Telegram.MtProto" Version="0.1.0-preview.1" /> <!-- 个人号通道 -->
```

```csharp
builder.AddSbFramework();              // 框架装配
builder.AddTelegramChat();             // SDK 核心装配（配置/缓存/通知/发送队列）
builder.AddTelegramBotAdapter();       // Bot 通道（按需）
builder.AddTelegramMtProtoAdapter();   // 个人号通道（按需；需实现 IAuthProvider 提供验证码/2FA）
```

业务代码（盛派风格，同一 Handler 服务双通道）：

```csharp
public class CustomMessageHandler : TelegramMessageHandler<ChatContext>
{
    protected override Task<TelegramResponseMessageBase?> OnTextRequestAsync(RequestMessageText message, CancellationToken ct)
        => Task.FromResult<TelegramResponseMessageBase?>(new ResponseMessageText { Content = "收到" });
}
```

主动推送（业务系统一行式通知）：

```csharp
public class XxxService
{
    public Task NotifyAsync(INotificationService notify, string customerId, string text)
        => notify.SendTextAsync("bot-account", customerId, text);  // 经发送队列自动限速/重试
}
```

## 能力矩阵

详见 [Sb.Framework.Chat/FEATURES.md](./Sb.Framework.Chat/FEATURES.md)（权威进度追踪，完成项标 ✅，未完成标 🚧/❌）。

## 设计与框架关系

- 仓库平级、依赖下级：SDK 依赖 `sb-framework` Core/Models 基础设施，框架不反向依赖。
- 通用工具（RetryUtil 等 5 个）已抽象到 `SB.Framework.Core/Util`。
- 完整设计见家族文档《07-Telegram-SDK-设计草案》《08-Telegram-SDK-技术设计》。

## 贡献

详见 [CONTRIBUTING.md](./CONTRIBUTING.md)。命名规范、版本号策略等通用规则跟 [sb-framework](https://gitlab.saibo.club/saibo/framework/sb-framework) 一致。

## License

Proprietary — 赛博科技内部使用。
