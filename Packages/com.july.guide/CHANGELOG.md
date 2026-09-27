# 更新记录

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
- CreateGuides / CanStart / CreateStepProcedure 定义接入。Run 显式评估一段候选，无候选返回 null；优先级降序、ID 升序。
- Stop / Skip / 外部取消统一入口；等待取消分发与 Procedure 清理，公开取消回调异常。终态通知保持旧运行可加入，多 Stop 共同阻止新执行；通知结束后才完成运行任务。
- 分开静态定义、瞬态执行与整段结果。Store 仅记录 Completed / Skipped；提交后保存通知失败公开错误，不回滚已提交事实。
- GuideConfirmProcedure / GuideClickTargetProcedure 提供通用完整交互；不可变 GuideViewData 直接传入使用者，不占用通用步骤模型。
- GuidePresentation 保留目标跟踪、确认、跳过、射线、失效与资源清理；GuideUguiSkin / DefaultGuideUguiSkin 支持项目替换美术与布局。
- GuideUITarget 仅提供几何，不接管指针路由；GuideButtonTarget 从 Button.onClick 观察接受点击。世界交互和自定义控件适配仍由项目负责。
- GuideRaycastModes 明确只约束 UGUI。业务输入许可由拥有它的 Procedure 在退出前释放，框架不解释玩法输入。
- 暴露当前 Guide / Step / WaitingFor / LastFailure 及生命周期事件。更新边界、接入、运行、皮肤及保存文档。

- 运行验收修复：皮肤在未激活层级下配置时，Canvas 查询包含未激活对象，避免正常皮肤被误判。
- 随包保留 8 项生命周期 EditMode 与 6 项 UGUI PlayMode 回归测试；Unity 2022.3.62f2 下编译及全部 14 项运行验证通过。项目业务集成测试留在项目侧。
