# SB Framework Chat

> SB Framework 的聊天平台抽象层 — 给派生项目（PM / ERP / CRM / ...）集成 Telegram / 未来扩展其他 IM 用。

## 内容

| 工程 | 内容 |
|------|------|
| `SB.Chat` | 抽象层（`IChatBot` / `IChatClient` / `Models`） |
| `SB.Chat.Telegram` | Telegram 适配器（基于 Telegram Bot API） |
| `SB.Chat.Tests` | 单元测试 |

## 派生项目怎么用

通过 `saibo/framework` GitLab NuGet feed 引用：

```xml
<PackageReference Include="SB.Chat" Version="0.1.0-preview.1" />
<PackageReference Include="SB.Chat.Telegram" Version="0.1.0-preview.1" />
```

## 已知技术债

- ⚠️ 工程命名 `SB.Chat` / `SB.Chat.Telegram` **不符合** Saibo [命名规范](../sb-framework/docs/naming-conventions.md) §3 — 应为 `Sb.Framework.Chat` / `Sb.Framework.Chat.Telegram`。
- **计划**：v0.2.0 版本重命名，同时升级派生项目引用。
- **现状**：保留原名以避免连锁修改派生 pm-api 等。

## 贡献

详见 [CONTRIBUTING.md](./CONTRIBUTING.md)。命名规范、版本号策略等通用规则跟 [sb-framework](https://gitlab.saibo.club/saibo/framework/sb-framework) 一致。

## License

Proprietary — 赛博科技内部使用。
