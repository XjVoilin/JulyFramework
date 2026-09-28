# July Guide

JulyFramework 下的引导执行模块。只负责计划、开始条件、步骤调度、停止/跳过和完成记录。
不规定窗口、皮肤、目标定位或本地化。项目通过已有 July UI 操作普通 UIView。

## 接入

注册 GuideStore 和项目 GuideSystemBase 派生类。保存仍使用 July Arch 的 Store 持久化能力。
项目实现 CreatePlans、CreateStartConditions、CreateStepProcedure。

- GuidePlan 包含 Id、Steps、StartConditionType、StartConditionParamId、Priority、CanSkip。
- 条件按 int Type 注册，同类型复用一个 IGuideStartCondition；CanStart(int paramId) 读取最新业务状态。
- ParamId 是项目配置记录引用，不是通用参数值；无参数策略可显式约定 0。
- GuideStep 包含 Id、Type、ParamId，项目在 Procedure 执行位置读取具体参数。
- 初始化拒绝重复条件类型、重复计划和未注册条件引用。配置内容由项目边界负责。

## 执行合同

业务和界面准备完成后调用 `guide.RunAsync()`。空闲时选择满足条件且未完成的最高优先级计划，
同优先级按 Id；运行或清理期间重复触发返回 null，不排队、不等待、不自动启动下一段。
所有调用遵守 Unity 主线程合同。RunAsync 没有外部取消参数，每轮由 System 持有独立 CTS。

项目 Procedure 顺序编排业务与表现，可以没有窗口，也可在一个完整步骤中多次更新同一窗口。
Procedure 返回后推进下一步骤，整段完成或允许跳过后才提交持久记录。中断不保存内部步骤进度。
重新执行从 Plan 的第一步开始；需要重建玩法状态时，由项目完成后再触发。

`StopAsync()` 请求中止并等待 Procedure 的 finally 清理，结果为 Aborted；不回滚玩法。
`SkipCurrentGuideAsync()` 仅允许可跳过计划。步骤内部可用 Context.RequestSkip 请求跳过。
退出场景或释放业务资源前必须先等待 StopAsync；同步 Shutdown 不能替代异步清理。
Context 还提供 GuideId、Step、CanSkip和 SetWaitingFor，不提供 Next/Complete。
事件回调记录业务事实，Procedure 在执行或等待过程中检查并直接抛出异常。没有独立的异常上报或汇总入口。取消回调不执行会抛错的业务清理。

## 项目表现

Procedure 通过 IUISystem.OpenAsync(windowId, projectData, ct) 打开项目 UIView，
使用窗口的普通方法更新提示并等待确认。窗口只报告交互，不推进计划或保存结果。
Procedure 将跳过事件连接 Context.RequestSkip，在执行等待时检查窗口是否仍打开；等待确认本身的失败由窗口返回的任务抛出。
在 finally 中解除订阅并 await IUISystem.CloseAsync(window)，清理不传已取消的 token。
使用业务 System 管理操作许可，不通过窗口获取玩法控制权。

项目可在 prefab 中组合通用 UI 组件。July UI 源码提供可选 UIRectHole 矩形遮罩/射线组件，
它不依赖 Guide，也不是当前 Guide 的安装依赖。手指等动画按项目需求使用普通 UI/动画能力。
本包不提供默认窗口、GuidePresentation、GuideUguiSkin、目标注册或相机注册。
本地化直接使用项目服务；配置目标编号的定位方式由实际项目决定，不要求先注册所有业务对象。

## 迁移与验证

这是未发布的结构调整：移除旧 View/Target 运行代码、默认 prefab 和专属 UGUI 测试，
解除 July UI、UGUI、TMP 程序集依赖及 UI/TMP 包依赖。旧皮肤 prefab 须迁移为项目普通窗口。
Samples~/LubanAuthoring 保留配置适配示例；确认/点击类型是示例项目约定，不是核心内置执行器。
Editor 生命周期测试继续保留，GreedyGoose 的项目教学测试源码已迁移到项目 UIGuideWindow。
本轮仅静态检查，未运行测试、Unity 编译、prefab 导入或播放验收；历史验收不覆盖这次迁移。
