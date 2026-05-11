# 贡献 sb-framework-chat

## 范围

本仓库只放**聊天平台抽象层 + 适配器**。**不要**把派生项目的业务（PM 的 ChatRequirement / ChatBatch / etc.）放这里。

具体业务（"用聊天搜集需求"等）应在派生项目 `Sb.Apps.<App>.Services.Chat/` 下，调用本仓库的 `SB.Chat` 抽象。

## 三不准

| 禁止 | 检查方式 |
|------|---------|
| 不准放派生业务实体（如 ChatRequirement / ChatBatch） | Code Review |
| 不准引用 PM / ERP / CRM 等派生项目类型 | CI |
| 不准在抽象层（`SB.Chat`）依赖具体 IM 协议（Telegram / Discord / etc.） | Code Review — 协议在 `SB.Chat.<Provider>` 适配器层 |

## 分支 / commit / 版本

跟 [sb-framework](http://192.168.10.241/saibo/framework/sb-framework) 治理基线一致：
- main 受保护，所有变更走 MR
- commit message 中文 Conventional Commits（`feat(telegram): xxx`）
- 版本号 SemVer，预发布 `-preview.N`
- 打 tag `v0.1.0-preview.N` 触发 CI 自动 push NuGet 包

## 新增 IM 适配器

新增 Discord / Slack / WeChat 等：
1. 新建工程 `SB.Chat.<Provider>` （未来重命名后 `Sb.Framework.Chat.<Provider>`）
2. 实现 `IChatBot` / `IChatClient` 接口
3. README 加入 Provider 列表
4. 不要污染 `SB.Chat` 抽象层接口（保持协议中立）

## 测试

```bash
dotnet test sb-framework-chat.sln
```
