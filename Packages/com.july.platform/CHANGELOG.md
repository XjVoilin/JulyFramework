# 更新记录

## 未发布

- 新增 IAccelerometerService.Start/Stop，沿用普通方法与 July.Arch 事件模式；分别发布 AccelerometerStartResultEvent、AccelerometerStopResultEvent 和 AccelerometerSampleEvent。
- 微信、抖音适配器注册真实加速度计实现；默认适配器发布不支持的失败结果，不生成模拟样本。
- 由小游戏在主线程控制启停顺序：收到启动结果后再停止，收到停止结果后再重新启动。不引入 Task、采样会话协调器或前后台管理。
- 两端直接调用 SDK 并发布事件，使用具名数据回调和平台默认采样间隔；微信在 Stop 中解绑回调，Shutdown 直接调用 Stop。不维护额外监听器状态、释放闭包或补偿启停逻辑。
- 样本为 SDK 设备 X/Y/Z 原值，时间戳为接收时的 realtimeSinceStartupAsDouble（秒）。当前 SDK 声明未明确单位和横竖屏轴向，未添加猜测换算；两端单位、方向、实际频率和切后台恢复需真机验收后标定。
- 测试覆盖 Default 服务注册和 Arch 结果事件，不再保留异步会话协调测试。

## 0.4.1 - 2026-09-13

- 包内提供微信初始化及好友查询所需的 JulyJsBridge.jslib，补齐 C# 声明对应的 JS 实现。
- 升级项目需移除旧 JulyJsBridge.jslib 及其 meta，避免重复导出；桥接协议与初始化时机保持不变。

## 0.4.0 - 2026-09-12

- 新增 PlatformConfig，集中配置帧缓冲最长边与微信、抖音激励广告位；空广告位表示不启用对应广告。
- 接口变更：ILoginService.LoginAsync 接收 CancellationToken，SDK 晚到回调不再覆盖已取消请求的 Code。
- 平台初始化传递取消并清理失败状态；DeferAllServices 统一执行平台服务的延迟初始化。

## 0.3.10 - 2026-09-03

- 小游戏 DPR 限制从帧缓冲总像素预算改为帧缓冲最长边限制，在约束手机极端分辨率的同时保留平板清晰度。

## 0.3.4 - 2026-08-17

- 直接根据目标 `RawImage` 计算微信开放数据域视口范围，无需 `MinPoint` 和 `MaxPoint` 子节点。

## 0.3.2 - 2026-07-22

- 为微信剪贴板接口补充请求、成功和失败诊断信息。

## 0.3.1 - 2026-07-22

- 为统一的启动来源和内容渠道协议分配稳定的数值。

## 0.3.0

- 新增统一的平台启动信息和生命周期适配器。
