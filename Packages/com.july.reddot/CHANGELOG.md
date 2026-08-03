# Changelog

## 0.2.0 - 2026-08-03

- 使用 `RedDotBuilder` 统一构建节点与 Handler 定义，由 `RedDotSystemBase` 负责安装。
- 增加节点拓扑和 Handler 绑定的严格校验。
- 增加框架级 `IRedDotRegistrar`，用于业务功能接入红点系统。
- 注册完成后自动执行首次 Handler 刷新。
- `UIRedDot` 支持在运行时安全切换 Key。
- 移除绕过 Builder 的公开节点注册入口，红点树统一经过校验后安装。

## 0.1.1 - 2026-07-22

- Added a reusable UIRedDot inspector that discovers keys from project RedDotTreeConfig assets.

## 0.1.0

- Added red-dot tree runtime, handlers, UI presenter and editor tooling.
