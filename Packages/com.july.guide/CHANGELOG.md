# 更新记录

## 0.4.0 - 2026-09-28

- 删除 Context.Fail、_reportedFailure 和多异常合并机制；Procedure 的错误直接经运行任务传播，LastFailure 仅保留实际任务失败供诊断。同步移除专属上报用例，未运行测试。

- 收敛为引导执行核心：删除 GuidePresentation、转发式 GuideWindow、Skin 体系、默认 prefab 和内置确认/点击表现流程。
- 移除无项目消费者的目标注册、相机注册、本地化中转；解除 July UI / UGUI / TMP 依赖。
- 项目 Procedure 直接使用 July UI 和普通项目窗口；矩形遮罩与射线能力移入 July UI 的可选 UIRectHole。
- 旧 UGUI 专属测试随被移除的合同退役，保留核心生命周期测试；项目测试引用迁移到项目窗口。未运行测试或编译。


- 简化取消：RunAsync 移除外部 token 参数，每轮由 System 持有一个独立 CTS，删除外部取消手动转发、取消分发完成信号及取消回调异常接管。Procedure 仍通过 finally 清理，运行任务在清理后完成；取消回调必须遵循不抛业务异常的合同。
- 删除多个 Stop 等待者的计数协调；停止清理期间 Run 与其他忙碌状态一致，立即返回 null。终态事件直接交给 EventBus，删除逐条事件异常包装。
- 将针对取消回调/事件错误处理器再抛错的特例用例替换为正常停止、重复停止、事件错误报告合同的源码用例；本轮未运行测试。

- 开始条件按 int 类型批量注册：IGuideStartCondition 提供 Type 与 CanStart(int paramId)，CreateStartConditions 替换逐引导 CreateStartCondition；GuidePlan 必须声明 StartConditionType / StartConditionParamId。参数编号引用项目配置记录，同类型策略在多段引导间复用，不按引导 ID 分发。
- 初始化检查重复条件类型及未注册类型引用；参数记录结构和引用有效性由项目配置边界保证。当前项目直接映射已解析的关卡配置引用，Luban 模板使用显式注册的无参数策略。
- 运行中重复 Run 立即返回 null，不等待当前执行、不排队；同步更新终态重入合同、项目消费者及 Luban 接入示例。
- 补充按类型分发、不同参数复用同一策略、动态准入、重复/缺失注册的回归用例源码，迁移已有消费者；本轮未运行测试。

## 0.3.1

- 引导表现通过现有 IUISystem 打开 GuideWindow；移除自建 Canvas、常驻根节点、手动实例化/销毁和独立更新器。
- 标准确认/点击接收项目分配的 windowId；自定义 Procedure 使用 GuidePresentation(IUISystem, windowId)，finally 等待 CloseAsync。移除 skinPrefab 构造参数和同步 Dispose。
- 随包提供 DefaultGuideWindow prefab，项目使用 Variant 接入现有窗口/资源配置。全屏遮罩与安全区内容分离，默认皮肤使用实际 UI 相机投影。
- 支持连续更新复用窗口；外部提前关闭报告失败；Stop 等待窗口关闭动画，加载取消按 UI/资源合同清理。
- 复用 July UI 0.2.28，未修改 UI 框架或加入新宿主接口。通用表现测试增至十项。

## 0.3.0

破坏性种子升级。旧 API 尚未在正式项目落地，本版删除旧执行路径，不提供双轨兼容。

- 框架唯一持有步骤顺序。每步通过完整 GuideStepProcedure 执行观察、表现、业务等待及资源清理，返回后才推进。
- 删除四类 Handler、GuideHandlerRef、链式下一步骤和持久化游标。Step 只保留不可变 Id / Type / ParamId，项目解释内容与业务参数。
- CreatePlans / CanStart / CreateStepProcedure 定义接入。Run 显式评估一段候选，无候选返回 null；优先级降序、ID 升序。
- Stop / Skip / 外部取消统一入口；等待取消分发与 Procedure 清理，公开取消回调异常。终态通知保持旧运行可加入，多 Stop 共同阻止新执行；通知结束后才完成运行任务。
- 分开静态定义、瞬态执行与整段结果。Store 仅记录 Completed / Skipped；提交后保存通知失败公开错误，不回滚已提交事实。
- GuideConfirmProcedure / GuideClickTargetProcedure 提供通用完整交互；不可变 GuidePresentationOptions 直接传入使用者，不占用通用步骤模型。
- GuidePresentation 保留目标跟踪、确认、跳过、射线、失效与资源清理；GuideUguiSkin / DefaultGuideUguiSkin 支持项目替换美术与布局。
- GuideUITarget 仅提供几何，不接管指针路由；GuideButtonTarget 从 Button.onClick 观察接受点击。世界交互和自定义控件适配仍由项目负责。
- GuideRaycastModes 明确只约束 UGUI。业务输入许可由拥有它的 Procedure 在退出前释放，框架不解释玩法输入。
- 暴露当前 Guide / Step / WaitingFor / LastFailure 及生命周期事件。更新边界、接入、运行、皮肤及保存文档。

- 运行验收修复：皮肤在未激活层级下配置时，Canvas 查询包含未激活对象，避免正常皮肤被误判。
- 随包保留 8 项生命周期 EditMode 与 6 项 UGUI PlayMode 回归测试；Unity 2022.3.62f2 下编译及全部 14 项运行验证通过。项目业务集成测试留在项目侧。
