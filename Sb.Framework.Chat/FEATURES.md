# Sb.Framework.Chat 能力矩阵（FEATURES）

> 此文件随版本演进维护。**"以为支持了其实没有"是禁忌**——能力必须如实标注。
> 状态：✅ 已实现 / 🚧 规划中（已排阶段）/ ❌ 未规划

## 当前版本：0.1.0-preview.1

### SDK 核心

| 能力 | 状态 | 阶段 |
|------|------|------|
| 统一消息模型（文本/图片 + 类型注册表） | ✅ | Phase1 |
| MessageHandler 分发 + 管道钩子 | ✅ | Phase1 |
| ChatContext / MessageContainer（防撤回载体） | ✅ | Phase1 |
| AccountContainer / AccountBag（OwnerId/TenantId） | ✅ | Phase1 |
| 内存缓存策略（可插拔） | ✅ | Phase1 |
| 适配器接口 ITelegramAdapter | ✅ | Phase1 |
| 存储 chat_* 表实体 | 🚧 | Phase1 |
| 视频/语音/文件/贴纸消息模型 | 🚧 | Phase2 |

### 增强功能

| 能力 | 状态 | 阶段 |
|------|------|------|
| 统一通知推送 INotificationService | 🚧 | Phase1 |
| 内置发送队列 + 频率控制 | 🚧 | Phase1 |
| Mock 适配器（开发/测试） | 🚧 | Phase1 |
| 内置统一 Webhook 端点 | 🚧 | Phase1 |
| 账号健康度 → /health | 🚧 | Phase1 |
| 通用消息中间件（敏感词/审计/关键词） | 🚧 | Phase2 |

### 通道适配器

| 能力 | Bot | Personal | 阶段 |
|------|-----|----------|------|
| 收发文本/图片 | 🚧 | 🚧 | Phase1 |
| 命令/回调/菜单 | 🚧 | — | Phase1 |
| 撤回标记+内容保留 | — | 🚧 | Phase1 |
| 历史补拉/会话同步 | — | 🚧 | Phase1 |
| Mini Apps / Web Apps | 🚧 | — | Phase1 |
| 支付（Stars/Payments） | 🚧 | — | Phase2 |
| 群聊/频道支持 | 🚧 | 🚧 | Phase2 |
| 转发 | 🚧 | 🚧 | Phase2 |
| 已读/在线/表情反应 | ❌ | ❌ | 未规划 |
| 多设备/会话多开 | ❌ | ❌ | 未规划 |
| 语音/视频通话 | ❌ | ❌ | 明确排除 |

> 注：本表反映 SDK 核心当前进度。BotApiAdapter / MtProtoAdapter 尚未实现（Phase1 Step 4/5）。
