# 更新记录

## 0.5.2 - 2026-10-25

- Use JLogger across platform services and adapters, with explicit assembly and package dependencies.
- Keep expected advertising, sharing and optional SDK-operation failures below error severity to avoid unnecessary error reporting.

## 未发布

- 加速度计停止回调无论成功与否都结束本次操作并回到 Idle，保留最新启动意愿，允许回前台或显式 Start 重新启动；删除永久 Faulted 状态及微信错误文本特判。停止失败仍发布真实失败结果并记录错误；退出不重启，启动失败不循环重试。

- 删除 DprConfig 的 EnableLongEdgeLimit/MaxLongEdge、两端构造校验/日志及公共策略的长边分支。仅按设备档位的整帧像素预算降 DPR，预算内的细长窗口保持默认尺寸。
- 高/中/低预算继续为 2100000/1500000/1000000，未知按中档；保留预算开关、DPR 1 下限、不放大、PC 跳过及平台精度/有效 DPR 路径。消费项目同步删除旧长边序列化字段并重建 Player。

- DPR 配置收拢到 PlatformConfig.Dpr（DprConfig），像素预算开关明确命名 EnablePixelBudget。既有平铺字段需迁移到新结构；不保留两套权威配置。
- DevicePixelRatioPolicy 统一解释开关、选择档位预算与计算限制；设备适配器仅负责平台分级、SDK 调用/精度及有效 DPR。原 DevicePixelRatioBudget 文件重命名并保留 GUID。
- 推荐预算、框架默认长边、项目覆盖值、未知档映射、PC 跳过、双关闭、DPR 下限及 SafeArea 行为保持。需要重建 Player。

- DPR 性能策略改为按档位限制整帧像素总量，移除 1/0.7/0.5 固定倍率。保留 EnableDprPerformanceScaling 序列化字段及启动开关，Inspector 改为“启用分档像素预算”。高/中/低默认预算为 2100000/1500000/1000000 像素，未知设备使用中档；分级阈值保持。
- 像素预算与独立长边限制按较严者一次计算 DPR，低于预算保持默认尺寸；保留 DPR 1 下限且不高于平台默认 DPR。最低 DPR 可能使实际像素超预算，日志提供预算、目标 DPR 和尺寸估算，实际以 Screen/RT 为准。
- 参数在平台启动时生效，影响整个项目；需重建 Player。推荐值未经目标设备标定，不能据此承诺手机内存、帧率或画质。

## 0.5.0 - 2026-10-22

- PlatformConfig 新增独立的 DPR 性能降档和绘制长边限制开关，默认开启以保持原行为。微信、抖音仅在对应开关开启时应用该策略；都关闭时保留平台默认 DPR，不调用降低 DPR 的接口。日志包含两个开关状态。
- 长边限制关闭时忽略 MaxFramebufferLongEdge；仅保留性能降档时维持最低 DPR 为 1。开关在设备服务初始化时生效，需重新构建 Player 并冷启动验证。
- 微信、抖音适配器构造改为接收 PlatformConfig，Bootstrap 同步传递，直接构造适配器的调用方需同步调整。

## 0.4.5 - 2026-10-20

- 微信加速度计的固定采样档位由 game 改为 ui（名义约60ms/次），使连续施力的输入节奏与已核对的参考基准一致。实际回调频率仍受设备和运行负载影响。
- 保持现有 Start/Stop 接口、启停串行化、旧采样隔离、前后台恢复及抖音适配器，不增加运行中换档机制。
- 此改动影响所有微信加速度计消费者；基于旧采样节奏的力度参数需要复测。平台属于主包程序集，升级后须重建 Player，单独更新热更 DLL 不能替换旧适配器。
- 真实 SDK 声明编译及受控边界下的 ui 请求/生命周期检查通过；微信设备实际频率和力度验收仍待完成。

## 0.4.4 - 2026-10-19

- 补齐 Platform 测试程序集对 July.Events 的直接引用，恢复加速度计事件测试编译。
- 推荐流订阅状态查询先检查 `CanIUse.CheckFeedSubscribeStatus`，当前环境不支持时跳过 SDK 调用并记录普通日志。

## 0.4.3 - 2026-10-15

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
