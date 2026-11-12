# July Physics Authoring

Unity 2022.3 编辑器碰撞体制作工具。提供整体包围、拾取结构贴合，以及 Prefab 当前／候选对照、参数调整、候选暂存和应用。包为 **Editor-only**，不包含运行时程序集，不依赖 July 游戏框架；项目通过 Editor Adapter 接入自己的物品、输出分组和绑定规则。

## 安装与入口

通过 UPM 安装 `com.july.physics.authoring`，打开 **JulyGF → 资源管理 → 碰撞体总览**。在 Project 选择 Prefab 或文件夹，点击“读取所选”。默认显示原模型、当前碰撞体和候选；拖动任一预览同步旋转。

在“更多 → 制作设置”配置模型根相对路径（空为 Prefab 根）、输出分组和 Mesh 目录。默认输出为 `Colliders`，Mesh 目录为 `Assets/Generated/ColliderAuthoring`。可配置多个独立输出目标，每个目标有稳定 ID、名称和生成方式；目标路径不能重复或相互包含。仅扫描静态 MeshFilter＋MeshRenderer，排除输出组，不自动烘焙蒙皮动画。

## 生成与应用

- **整体包围**：用原生盒、球、胶囊概括主体，默认最多3个；保留整体轮廓，可以填平内部小凹陷，不生成碰撞网格。
- **结构贴合**：按单件模型分析必要空白和开放结构，减少不必要凹性；先尝试原生形状，无法满足生成要求的部分回退为少量凸网格。

生成只创建候选，应用才写回 Prefab。行内和底部的范围选择只改变处理范围，点击相邻主按钮才执行；批量操作只处理可见选中项。组外已有 Collider 默认保留，项目适配可要求先整理归属。当前 Mesh 预览显示源网格，不代表已经重建 PhysX 烹制外形。

结构贴合的常用默认值：

| 参数 | 默认 | 范围 |
| --- | --- | --- |
| 碰撞体数量上限（包含原生和凸包） | 8 | 1–16 |
| 单凸包顶点数上限 | 16 | 4–24 |
| 凸包额外内缩／模型最长边 | 10% | 0–20% |
| 原生形状偏差／模型最长边 | 10% | 0–20% |
| 原生点击范围变化 | 20% | 0–30% |

生成的凸网格总顶点数不超过数量预算乘以单包顶点上限，默认128。内缩误差相对未压缩原凸体测量，不逐轮累积。无法满足生成预算时明确失败并保留上一次成功候选。提高原生容差会增加近似机会，也可能改变主体和孔口边缘。生成结果仍需按实际拾取用途取舍。

候选旁“调参”只修改当前 Prefab、本栏和当前生成方式的配方；“调参*”表示独立覆盖，可恢复默认。生成候选依赖当时的模型、坐标和参数，相关数据改变会使它过期。

## 项目候选处理与名称标记

项目适配在 `ColliderTargetLayout.Processor` 提供可选的 `IColliderCandidateProcessor`；直接生成请求也可指定 `ColliderGenerationRequest.Processor`。框架在拟合完成后、预览和缓存封存前调用一次 `Process(ColliderCandidateDraft)`。草稿提供拟合坐标系中的源包围盒、候选形状和摘要；项目可以调整形状中心、旋转及原生盒／球／胶囊尺寸。形状类型、数量和网格几何保持，由框架维护内部拟合表示。处理规则、尺码或其它业务判断全部由项目实现。

处理器按每个请求创建不可变的配置快照，`Fingerprint` 必须是包含规则标识、算法版本和全部有效输入的稳定非空字符串。框架将指纹随候选保存，当前规则或输入改变时，生成候选失效；修改算法但输入不变时应更新规则版本。没有处理器的旧候选继续按无项目处理读取。`Process` 在 Unity 主线程执行，只编辑草稿；框架复制并检查处理结果后封存，保留草稿引用不会改变最终候选。预览、缓存与应用共用封存结果，缓存恢复和应用不会再次处理；异常或取消保留上一次成功候选。人工直接复用继续作为原样快照，不执行项目处理，也不因处理规则改变而失效。

名称旁标记仍通过 `ColliderAssetLayout.Badge`／`BadgeTooltip`／`BadgeWarning` 提供声明式数据，由通用列表绘制。

项目窗口可重写 `BuildProjectSortMenu(DropdownMenu)`，将排序项加入“原模型”列头菜单（位于全选按钮上方），通过 `ToggleProjectSort(label, ascendingOrder, descendingOrder)` 提供名称和双向比较器，并用 `IsProjectSort` 显示菜单选中状态。首次选择项目项为升序，重复选择切换方向；列头显示名称及 ↑／↓。比较器返回零时统一按名称／路径自然排序，缺失值等业务顺序由项目定义。列头内置“名称”用于恢复默认名称顺序，重复选择同样反向。刷新沿用当前排序，选择名称或任一内置列排序后切回内置排序，不改变筛选与勾选。

## 直接复用

- **整体包围候选 → 复用当前**：把本栏当前启用的盒、球、胶囊原样转换为候选，保留数量、中心、尺寸和轴向；不重新拟合，不改结构贴合候选。
- **结构贴合候选 → 复用整体候选**：直接深复制整体包围候选，覆盖本栏候选。

复用是人工认可的直接快照，立即暂存，**不执行贴合、孔隙、误差、数量或顶点预算验收**；生成参数变化不使手动复用候选失效，再次生成才替换它。只保留来源存在、当前支持的类型、模型／坐标一致及实际应用所需的数据约束。复用不应用 Prefab、不自动确认审阅；说明和手动标识随候选缓存保存。

## 筛查、统计与审阅

“检查范围”按目标分组，可同时勾选当前与候选，默认全选；“全部列”恢复全选。“提醒筛选”使用全部物品、有提醒、重点及以上、规则不符按钮，“仅全凸包”是独立条件。任一勾选列满足条件即可显示物品；提醒和全凸包必须在同一列同时满足。统计跟随状态、搜索及组成筛选，按提醒等级筛选前的范围汇总；每件按所选列最高级别计一次，全凸包数量可重叠。

初始提醒／重点阈值：整体包围数量>3／>6，结构贴合数量>8／>12，凸包数量≥4／≥8，单凸包顶点>16／>24，结构凸网格总顶点>64／>128。类型、缺失 Mesh 和项目24顶点上限问题单列为规则不符。可在“更多 → 制作设置 → 性能提醒”修改或关闭成本提醒；全凸包本身不代表性能差。

提示只用于制作筛查，不限制复用或应用，不估算真机耗时。各列支持数量、种类、顶点、三角面、凸包和性能等级排序。底部汇总可见选中项的当前／候选网格量；重复引用的 Mesh 按实例计数。

“状态”显示全部／待处理／已完成，“筛选”显示不限／异常／新纳入／需复审。计数基于读取范围，搜索和组成条件叠加；“已完成”隐藏并忽略子筛选。生成和应用均不自动确认；检查当前结果后点击“标记完成”。“标为待处理”只改变审阅状态，保留碰撞体和候选。已确认的模型或当前 Collider 改变后进入需复审。

## 存储与失败行为

| 内容 | 位置 |
| --- | --- |
| 成功候选 | `July/ColliderAuthoring/<PrefabGUID>.json` |
| 可再生分析／原始拟合缓存 | `Library/July/ColliderAuthoring/PickingProxyStages/<几何指纹>/` |
| 制作配方 | `ProjectSettings/July/ColliderAuthoring/settings.json` |
| 逐项参数与审阅 | `ProjectSettings/July/ColliderAuthoring/<ProfileId>.reviews.json` |
| 个人生成方式 | `Library/July/ColliderAuthoring/<ProfileId>/view.json` |
| 正式生成 Mesh | 制作设置指定的 Assets 目录 |

删除 Library 只丢弃阶段计算和个人状态，不删除已有候选或审阅。仅调整顶点、内缩或原生近似参数时复用原始拟合；调整数量时可复用分析。模型、分析参数、算法身份或 GPU 核心变化会使对应阶段失效。“更多 → 重新分析并生成所选”跳过阶段缓存，只生成候选。

文档与缓存采用同目录临时文件＋原子替换；工具读取允许删除共享，避免自身读取阻止保存。Windows 的已知短暂共享／替换冲突最多尝试3次、间隔20ms；权限错误直接返回，持续失败仍明确提示。候选缓存保存失败时内存预览保持，重载后可能恢复不到本次结果。阶段缓存损坏会提示并重算；配方和审阅损坏明确报错，不静默重建。

应用先核对所选组和绑定，成功后保存该 Prefab；失败不保存该项，批量此前成功项保留。打开同一物品的 Prefab 编辑模式时禁止从列表应用。保存 Prefab 后作者记录写入失败会单独报告。应用不承诺跨资源文件事务，也不自动清理历史未引用网格。

## 生成后端与资源

整体包围使用 CPU／Unity ComputeShader 评分；结构分析可在 Windows 使用系统 D3D11，不可用时报告原因并使用 CPU。无需安装 CUDA、Python、驱动或外部几何库。着色器和代码随包发布，读取位置通过 UPM 解析，不要求嵌入 Packages 目录。

批量仍逐件执行；内部独立视角、候选搜索和射线交点预计算最多8线程，接受顺序和采样顺序固定。复用射线交点，避免逐片重复计算；进度条由可见行读取，不为每个进度事件刷新整表。按原始拟合先尝试原生形状，只有失败的片才压缩凸包。分析网格和完整训练／优化对象不挂在最终返回结果上长期保留。

历史圆柱、圆台、棱柱的网格转换、快照读写及必要数学仍保留；新整体包围不搜索这些类型。旧网格名称／标签识别只用于忽略旧禁用源，不恢复旧手工编辑或同步功能。

## 项目适配与公开 API

项目 **Editor** 程序集引用 `July.Physics.Authoring.Editor`，实现 `IColliderAuthoringAdapter`：

1. `CreateProfile` 声明稳定方案／目标 ID、显示名、相对路径、生成方式和项目 Mesh 目录。
2. `CanHandle`、`Inspect` 只读识别物品、来源、当前形状和配置问题。
3. `Prepare` 仅准备传入的临时内容与所选目标；`CreateRequest` 提供 Layer、材质、Trigger、接触偏移等配置。
4. `Complete` 在形状创建后、保存前完成项目绑定；抛出异常则该 Prefab 不保存。

窗口继承 `ColliderOverviewWindow` 并覆写 `CreateAdapter`；少量项目工具可覆写 `BuildProjectTools`。不同布局可持有公开 `ColliderWorkspace`，调用 Read、GenerateAsync、ReuseCurrentEnvelope、ReuseEnvelopePreview、Apply、MarkHandled、Cancel，并按生命周期 Dispose。配置返回副本，通过 Configure 提交，避免复制缓存和保存逻辑。

单组 Editor 工具可使用 `ColliderAuthoringContext.Create`、`ColliderGeneration.CreateRequest`、`PreviewAsync` 与 `ApplyPreview`；必要绑定使用 Adapter 或请求 `AfterApply`，`Applied` 事件只作观察。自有配方面板可调用 `ColliderGenerationSettingsGUI.Draw`。这些 API 和 `ColliderGeometryGoal` 全部属于 Editor 程序集。

## 从旧版本升级

0.6 起移除旧 `ColliderAuthoringTarget`、`PhysicalColliderAuthoringTarget`、`FineSphereEditData` 组件及运行时程序集。升级含这些组件的项目时，应先清除旧制作元数据；旧配方拥有、未被其他逻辑引用且已禁用的源／编辑备份可一起清理，启用碰撞体和网格保持。移除运行时及 Editor asmdef 对 `July.Physics.Authoring` 的引用和 linker 保留项，Editor 改引用 `July.Physics.Authoring.Editor`。本包不自动改写使用者资源。

旧分层候选可在停止生成后把 `<ProfileId>/<PrefabGUID>.json` 移至 `July/ColliderAuthoring/`；阶段缓存迁至 Library 下的对应目录。内容无需转换，不覆盖同名不同内容的文件；程序只读取当前路径。

## 验证

在项目 manifest 的 testables 中加入 `com.july.physics.authoring`，并安装 Unity Test Framework，可运行包内 Editor 回归。覆盖生成、原生／凸包混合、预算与孔隙、CPU／GPU 一致性、取消、资源／Undo、缓存、复用、参数和筛查。可选真实模型用例从隔离工程的 `Assets/TestInputs` 与 `Assets/GpuInputs` 读取冻结顶点／三角面 JSON；未提供时明确跳过，不依赖使用者正式游戏资源。
