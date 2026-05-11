# Changelog

## [0.1.0-preview.1] - 2026-05-11

### Added

- 仓库初始化：从原 `sb-chat` 迁移到 `saibo/framework/sb-framework-chat`
- 工程
  - `SB.Chat` — 聊天平台抽象层（`IChatBot` / `IChatClient` / `Models`）
  - `SB.Chat.Telegram` — Telegram 适配器
  - `SB.Chat.Tests` — 单元测试
- 治理基线：README / CONTRIBUTING / CODEOWNERS / CI / MR 模板

### Known Issues

- ⚠️ 工程命名未对齐 Saibo §3 命名规范（应为 `Sb.Framework.Chat.*`）— 待 v0.2.0 重命名
