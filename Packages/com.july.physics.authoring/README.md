# July Physics Authoring

Unity 2022.3 的基础碰撞体制作工具，从显示 Mesh 拟合少量 Box、Sphere、Capsule，不生成 Mesh 对象、碰撞网格资产或原生插件。

## 使用

在专用于碰撞体的父节点挂载 `ColliderAuthoringTarget`，通过该组件的 Inspector 调整。组件没有字段或运行逻辑，所在 Transform 就是完整输出范围，不依赖节点名称或已有 Collider；空分组、改名和 inactive 分组均可制作。独立 `ColliderAuthoringWindow` 及旧菜单、Inspector 顶部扩展已移除。

Prefab 应在 Prefab 模式中编辑；资源组件或场景 Prefab 实例的 Inspector 提供进入 Prefab 模式的按钮，普通场景对象可以直接操作。来源默认扫描最近 Rigidbody 下的显示 Mesh，或者当前 Prefab/对象根；项目可通过上下文提供明确显示来源。展开“来源 Mesh”可更换模型扫描根并重新扫描。

主要参数为“贴合误差”和“最大形状数”；高级设置包含材质和启用状态。生成会替换输出节点上的 Collider 和全部子节点，包括手调结果，保留父节点及 `ColliderAuthoringTarget`。输出范围不能包含 Renderer 或 Rigidbody；父节点及祖先须统一缩放、无剪切，显示来源可保留镜像或非均匀缩放。

生成、项目回调构成一个 Undo 操作，回调失败恢复原结果。Undo/Redo 保留本次手选的来源根并重新扫描，不恢复默认来源范围。组件 Inspector 共享进行中状态，避免切换选中、重建 Inspector 后重复生成，不增加任务队列或逐对象缓存。当前 Prefab 模式直接更新，无需关闭重开。Inspector 不强制保存，沿用 Unity 的 Prefab Auto Save 设置或手动保存。

## 拟合参数

- 最大形状数默认 6，范围 1–16，限制整个物品的总形状数，而非单 Mesh 数量。
- 贴合误差默认 0.4，范围 0.05–1，表示体素估计的多余覆盖体积比例；越小越倾向细化，不代表屏幕像素或精确几何误差。

几何按最长轴 32 格保守体素化，结合外部填充、主轴和截面变化拟合，有限搜索保留四种候选拆分方案。达到数量预算仍不满足目标时，返回当前方案并明确提示复核、手调或增加预算。小孔、细枝、薄片、开放网格及接近体素尺寸的细节需人工检查。

显示网格通过 Editor MeshData 读取，不改变 Read/Write、导入设置或原始几何。已有 Prefab 不会在导入时自动重建，旧碰撞 Mesh 资产也不会自动删除。

## 项目接入

`ColliderAuthoringContext.Create(output, settings)` 读取分组共有设置并收集来源。原窗口中的上下文配置迁至 `ColliderAuthoringContext.Opening`，项目继续设置 `SourceRoot` 或请求属性，不再判断是否显示 Inspector。

`ColliderGeneration.GenerateAsync(request)` 执行同一套拟合、替换和撤销流程，也可供项目批量工具直接调用；等待期间调用方不得修改请求。`ColliderGeneration.Applied` 在撤销事务内运行，项目通过 Undo/SerializedObject 配置组件和绑定，异常将恢复原结果。

本机最近使用参数由 `ColliderAuthoringContext.LastSettings()` 读取，不保存逐 Prefab 的参数或来源配置。项目按自身资源结构选择需要制作的碰撞体分组并挂载标记。

## 程序集与验证

`July.Physics.Authoring` 普通程序集只包含空的 `ColliderAuthoringTarget`，可随 Prefab 在运行时保留。`July.Physics.Authoring.Editor` 包含 Inspector、上下文和拟合算法，仅在 Editor 编译。标记组件不会把算法和界面带入 Player；本版不增加构建剥离流程。

安装 Unity Test Framework 并在 manifest 的 `testables` 中加入 `com.july.physics.authoring`，运行 `July.Physics.Authoring.Tests` 的 EditMode 测试。包内覆盖基础形状选择、整物品预算、环孔、不可读网格、坐标转换、Undo/Redo、回调回滚、混合设置、空组及标记保留；项目专属绑定和 Prefab 流程由项目集成检查覆盖。
