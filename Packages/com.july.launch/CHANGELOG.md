# 更新记录

## Unreleased

- 增加 Unity 2022.3 主工具栏与菜单 Launch 启动入口，自动定位 Assets 下唯一同名场景。
- 临时启动目标跨脚本重载保留，退出或取消播放后恢复原启动设置；缺失和重名明确提示。

## 0.2.0 - 2026-09-12

- LaunchPipeline 只执行一次，支持封闭计划及整条流水线的完成、失败通知。
- 并行步骤在返回或重试前取消其余任务并等待收尾，区分取消与失败。
- JulyGameEntry 销毁时先取消并等待启动任务，再执行 OnShutdown。
- 接口变更：IHotUpdateRegistrar.OnGameLaunch 接收 CancellationToken，项目实现需同步更新。
