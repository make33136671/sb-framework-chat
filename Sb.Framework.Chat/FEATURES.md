# Sb.Framework.Chat 能力矩阵 + 实现进度

> 此文件随版本演进维护，是 SDK 进度的**权威追踪**。
> **"以为支持了其实没有"是禁忌**——状态必须如实标注，已完成的才能标 ✅。
>
> 状态图例：
> - ✅ **已完成**（代码已实现、编译通过、已入库）
> - 🔨 **进行中**（正在开发）
> - 🚧 **已规划**（已排阶段，未开始）
> - ❌ **未规划**（明确排除）

## 当前版本：0.1.0-preview.1

## 实现进度总览

| Step | 内容 | 状态 |
|------|------|------|
| Step 1 | Core 通用工具（sb-framework） | ✅ **已完成**（5 个工具入库） |
| Step 2 | SDK 核心（模型/Handler/Context/Account/存储/装配） | ✅ **已完成**（编译通过，入库） |
| Step 3 | 增强基础（通知推送/发送队列/Mock 适配器） | ✅ **已完成**（编译通过） |
| Step 4 | MtProtoAdapter 生产化（会话/同步/防撤回/媒体） | ✅ **已完成**（编译通过；需真实凭据验证） |
| Step 5 | BotApiAdapter + 统一 Webhook 端点 + 健康检查 | ✅ **已完成**（编译通过；需真实凭据验证） |
| Step 5 | BotApiAdapter + 统一 Webhook 端点 | 🚧 未开始 |

---

## 一、Core 通用工具（sb-framework）✅ 已完成

| 工具 | 文件 | 状态 |
|------|------|------|
| `RetryUtil`（指数退避+抖动） | `SB.Framework.Core/Util/RetryUtil.cs` | ✅ 已完成 |
| `IdempotencyUtil`（时间窗口幂等去重） | `SB.Framework.Core/Util/IdempotencyUtil.cs` | ✅ 已完成 |
| `RateLimiterUtil`（按 key 固定窗口限流） | `SB.Framework.Core/Util/RateLimiterUtil.cs` | ✅ 已完成 |
| `WebhookSignatureValidator`（HMAC-SHA256 验签） | `SB.Framework.Core/Util/WebhookSignatureValidator.cs` | ✅ 已完成 |
| `SecurityUtil.MaskSecret/MaskToken/MaskPhone`（脱敏） | `SB.Framework.Core/Util/SecurityUtil.cs` | ✅ 已完成 |

## 二、SDK 核心（Sb.Framework.Chat）✅ 主体已完成

| 能力 | 文件 | 状态 | 阶段 |
|------|------|------|------|
| 统一消息模型（文本/图片/回调/群事件 + 媒体注册表） | `Model/*.cs` | ✅ 已完成 | Phase1 |
| 响应消息模型（文本/图片/回调应答） | `Model/TelegramResponseMessageBase.cs` | ✅ 已完成 | Phase1 |
| MessageHandler 分发 + 管道钩子 + 未知类型兜底 | `MessageHandler/TelegramMessageHandler.cs` | ✅ 已完成 | Phase1 |
| ChatContext（会话上下文） | `Context/ChatContext.cs` | ✅ 已完成 | Phase1 |
| MessageContainer（防撤回载体，删除只标记） | `Context/MessageContainer.cs` | ✅ 已完成 | Phase1 |
| AccountContainer / AccountBag（OwnerId/TenantId + Capabilities） | `Account/Account*.cs` | ✅ 已完成 | Phase1 |
| 会话状态机 + 凭证抽象 | `Account/AccountEnums.cs`、`AccountCredential.cs` | ✅ 已完成 | Phase1 |
| IMessageCacheStrategy + 内存实现（可插拔） | `Cache/*.cs` | ✅ 已完成 | Phase1 |
| ITelegramAdapter 适配器接口 | `Adapter/ITelegramAdapter.cs` | ✅ 已完成 | Phase1 |
| 存储 chat_* 表实体（6 张表 + ChatModuleInitializer） | `Storage/Chat*.cs`、`ChatModuleInitializer.cs` | ✅ **已完成** | Phase1 |
| 装配扩展 AddTelegramChat + 配置节 TelegramChatOptions | `Extensions/TelegramChatExtensions.cs`、`TelegramChatOptions.cs` | ✅ **已完成** | Phase1 |
| 视频/语音/文件/贴纸消息模型 | — | 🚧 未开始 | Phase2 |

## 三、增强功能

| 能力 | 状态 | 阶段 |
|------|------|------|
| 统一通知推送 `INotificationService` | ✅ **已完成**（DefaultNotificationService） | Phase1 |
| 内置发送队列 + 频率控制 | ✅ **已完成**（DefaultSendQueue：Channel+限速+重试+保序） | Phase1 |
| Mock 适配器（开发/测试） | ✅ **已完成**（MockTelegramAdapter + AddTelegramMockAdapter） | Phase1 |
| 内置统一 Webhook 端点 | 🚧 未开始 | Phase1 |
| 账号健康度 → /health | 🚧 未开始 | Phase1 |
| 通用消息中间件（敏感词/审计/关键词） | 🚧 未开始 | Phase2 |

## 四、通道适配器

| 能力 | Bot | Personal | 阶段 |
|------|-----|----------|------|
| 收发文本/图片 | 🔨 已实现待验证 | 🔨 已实现待验证 | Phase1 |
| 命令/回调/菜单 | 🔨 已实现待验证 | — | Phase1 |
| 撤回标记+内容保留 | — | 🔨 已实现待验证 | Phase1 |
| 历史补拉/会话同步 | — | 🔨 占位（业务层按 ChatSyncState 驱动） | Phase1 |
| Mini Apps / Web Apps | 🚧 未开始 | — | Phase1 |
| 支付（Stars/Payments） | 🚧 未开始 | — | Phase2 |
| 群聊/频道支持 | 🚧 未开始 | 🚧 未开始 | Phase2 |
| 转发 | 🚧 未开始 | 🚧 未开始 | Phase2 |
| 已读/在线/表情反应 | ❌ 未规划 | ❌ 未规划 | 未规划 |
| 多设备/会话多开 | ❌ 未规划 | ❌ 未规划 | 未规划 |
| 语音/视频通话 | ❌ 明确排除 | ❌ 明确排除 | 不做 |

> 注：双通道适配器（Step 4/5）代码均已实现并编译通过，但**依赖真实 Telegram 凭据与网络**，需真实账号联调验证后转 ✅。媒体下载 dcId / 发送 peer 解析 / webhook 公网配置为简化实现，联调时校正。

---

## 待补项

- 双通道真实凭据联调验证（撤回/发送/媒体下载/webhook）
- 通用消息中间件（敏感词/审计/关键词，Phase2）
- Mini Apps / Web Apps 调用、支付（Phase2）

> 完成即转 ✅。每完成一项即时更新本表。
