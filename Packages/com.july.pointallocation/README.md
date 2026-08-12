# July Point Allocation

July Point Allocation 是 JulyFramework 的数据驱动加点模块。本文件是设计与实现契约；后续代码不得在没有重新确认设计的情况下改变这里定义的术语、数据所有权和职责。

当前包已实现 Runtime 定义与进度逻辑、July System 接入、加点图编辑器、分层自动布局、运行时资产导出和 EditMode 契约测试。

## 模块职责

July Point Allocation 负责：

- 编辑、校验并导出加点节点与节点连线。
- 根据定义和初始进度创建相互独立的运行时实例。
- 管理可用点数和节点等级。
- 接收项目已经确认发放的点数，并将其计入指定运行时对象。
- 预演并执行加点、单点退款和整图洗点。
- 保证点数与节点等级原子修改。
- 返回不可变快照并发布状态变化事件。

July Point Allocation 不负责：

- 点数的业务来源。
- 节点对属性、技能、Buff 或战斗系统产生的效果。
- 存档格式、网络协议或配置表框架。
- 游戏 UI 中的节点位置、样式、图标、动画及连线样式。
- 配置版本迁移或旧存档升级。

## 统一术语

### Point Allocation Graph

由可加点节点和有向连接组成的逻辑图。第一版要求它是有向无环图，不要求每个节点只有一个前驱节点；项目可以将它表现为树状、放射状、星盘状或其他 UI 布局。

### Allocation Node

可投入点数并具有等级的目标。节点只拥有自身定义，不拥有父节点、子节点、入边或出边集合。

### Allocation Connection

从一个节点指向另一个节点的有向关系。它同时表达节点间的逻辑连线、起点等级对终点加点的依赖，以及项目 UI 可以选择显示的连线。

连接由整张 Point Allocation Graph 拥有，而不是由任一端节点拥有。

对外领域类型统一命名为 PointAllocationConnectionDefinition。Reference 只表示对象引用，不能表达方向、等级依赖和可显示连线，因此不得使用 PointAllocationReferenceDefinition 或 References 表示节点关系。Edge 只允许作为内部图算法术语，不进入对外定义模型。

### Point Allocation Progress

某个运行时实例当前拥有的可用点数与各节点等级。同一份定义可以创建一个或多个相互独立的进度实例；实现不得假定全局只有一个玩家或一个运行时加点图。

## 唯一关系事实

PointAllocationGraphDefinition.Connections 是节点关系的唯一事实来源。

不得同时增加以下重复数据：

- PointAllocationNodeDefinition.Prerequisites
- PointAllocationNodeDefinition.ParentId
- PointAllocationNodeDefinition.Children
- 另一份仅供 UI 使用的节点连接集合

Editor 中从 A 连接到 B，创建一条 PointAllocationConnectionDefinition(A, B, requiredRank)。运行时根据同一条连接判断 B 的前置等级；项目 UI 也只能从现有连接中选择需要显示的连线。

必须满足：

~~~text
UI 显示的加点连线 ⊆ PointAllocationGraphDefinition.Connections
~~~

UI 可以隐藏逻辑连线或覆盖样式，但不能创建逻辑定义中不存在的加点连线。纯装饰线属于项目 UI 美术，不属于 Allocation Connection。

## 定义模型

~~~text
PointAllocationGraphDefinition
├─ Id : int
├─ Nodes : PointAllocationNodeDefinition[]
└─ Connections : PointAllocationConnectionDefinition[]

PointAllocationNodeDefinition
├─ Id : int
├─ MaxRank : int
└─ RankCosts : int[]

PointAllocationConnectionDefinition
├─ FromNodeId : int
├─ ToNodeId : int
└─ RequiredRank : int
~~~

语义约束：

- Definition Id 与 Node Id 必须为正整数，0 表示无效。
- Point Allocation Graph 至少包含一个节点；空 AuthoringAsset 可以保存，但不得导出为空定义。
- Node Id 只要求在所属 Point Allocation Graph 内唯一。
- 节点删除后不得复用其 Id。
- MaxRank 必须大于等于 1。
- RankCosts.Count 必须等于 MaxRank；数组第 N 项表示从 N 级升到 N+1 级的消耗。
- 每级点数消耗必须大于 0。
- Connection 两端必须引用同一份图定义中存在的节点。
- 不允许自连接、重复连接或有向环。
- RequiredRank 必须介于 1 与起点节点 MaxRank 之间。
- 没有入边的节点是入口节点。
- 第一版中，终点节点的所有入边均须满足；在真实项目确认需要 OR 或条件组之前，不增加 PrerequisiteMode 或通用表达式系统。

## 运行时模型

同一份定义可以创建多个相互独立的运行时对象：

~~~text
PointAllocationRuntime
├─ Definition : PointAllocationGraphDefinition
└─ Progress : PointAllocationSnapshot
~~~

PointAllocationRuntime 对象本身代表一份独立运行时进度，不再增加 InstanceId。项目可以从同一份 PointAllocationGraphDefinition 创建多个运行时对象，但它们不得共享可变进度。

进度只保存最小事实：

~~~text
PointAllocationSnapshot
├─ AvailablePoints : int
└─ NodeRanks : (NodeId, CurrentRank)[]
~~~

NodeRanks 使用稀疏规范形式：只保存 CurrentRank 大于 0 的节点，按 NodeId 升序排列。缺失节点视为 0 级；输入快照中的未知节点、重复节点、零或负等级均为非法。查询返回的新快照不得共享运行时内部集合。

Locked、Available、Maxed、累计投入点数等可以由定义和等级计算的状态不进入进度。

配置发生不兼容变化时，由项目在恢复前迁移进度。PointAllocationRuntime 不保存配置 Version，也不静默修复未知节点、越界等级或非法点数。

运行时加载定义后可以构建内部 NodeById、IncomingConnectionsByNodeId 和 OutgoingConnectionsByNodeId 索引，但这些索引不是第二份配置事实。

## 运行时对象所有权

IPointAllocationSystem 作为 PointAllocation 模块接入 ArchContext 的入口，负责注册和查询不可变定义，并根据定义与初始 PointAllocationSnapshot 创建独立的 PointAllocationRuntime。

IPointAllocationSystem 不保存运行时对象注册表，不为运行时对象分配框架级标识，也不负责把运行时对象映射到玩家、角色、职业方案或预览方案。

项目持有 PointAllocationRuntime 引用并管理其生命周期和业务归属。PointAllocationRuntime 只暴露受控查询、加点、退款、洗点和完整进度替换，不能直接修改其内部进度，因此公开运行时对象不会绕过模块不变量。

定义注册使用 DefinitionId 保证唯一；重复注册失败且不替换旧定义。移除注册只影响后续查询和创建，已经创建的 PointAllocationRuntime 继续持有原不可变定义并保持可用。

## 权威进度恢复

第一版中，PointAllocationRuntime 只支持通过完整 PointAllocationSnapshot 原子替换自身进度，不提供局部节点替换接口。

完整替换必须先验证全部数据；任一节点未知、节点重复、等级越界、可用点数非法或依赖关系不成立时，替换失败且旧进度保持不变。成功后整个运行时进度一次性替换。

服务器或存档层只提供局部变化时，由项目层先合并为完整快照，再交给对应 PointAllocationRuntime 替换。PointAllocation 模块不得根据局部权威数据猜测点数变化或自动修复下游节点。

## 加点与退款语义

项目在等级提升、道具使用或其他业务中确认可分配点数已经获得后，通过 GrantPoints(amount) 向具体 PointAllocationRuntime 入账。amount 必须大于 0；点数来源、发放条件和重复发放防护仍由项目负责。减少或校正权威点数必须通过完整进度替换完成，不能使用负数 GrantPoints。

加点必须同时满足：

- 节点存在且尚未满级。
- 所有入边的起点等级达到各自 RequiredRank。
- 可用点数不少于目标等级对应的 RankCosts。

成功加点必须在一次事务中扣除点数并提升一级；任一检查失败时不得修改状态。

单点退款后必须重新验证所有仍有投入的节点。若会使任一已投入节点不再满足入边依赖，则普通退款失败。第一版不默认实现级联退款。

整图洗点将所有节点等级归零，并按当前定义中的已投入等级成本退还点数。项目修改历史等级成本时必须同时迁移玩家进度。

普通业务失败通过结果值返回，不记录错误日志。监听者异常需要被记录并隔离，不能破坏已提交状态或阻止后续事件派发。

所有预演和修改命令统一返回 PointAllocationOperationResult，至少包含 Success 与 FailureReason。正常失败必须能区分节点不存在、已满级、前置不满足、点数不足、当前等级为零、退款会破坏下游依赖、输入非法和点数溢出；UI 不需要重复实现规则来解释失败原因。

## 运行时事件

一次成功且实际改变状态的本地命令只发布一个 PointAllocationChangedEvent。事件在全部状态原子提交后发布，不拆分为点数事件和节点等级事件。

事件由发生变化的 PointAllocationRuntime 自身发布，监听者订阅具体运行时对象，因此事件数据不携带 InstanceId、PlayerId、CharacterId 或 LoadoutId 等身份。

~~~text
PointAllocationChangedEvent
├─ Reason
├─ PreviousAvailablePoints
├─ CurrentAvailablePoints
└─ NodeRankChanges[]

PointAllocationNodeRankChange
├─ NodeId
├─ PreviousRank
└─ CurrentRank
~~~

加点和单点退款通常包含一个节点变化，整图洗点可以包含多个节点变化，单纯调整可用点数时 NodeRankChanges 为空。事件数据必须是独立不可变快照。

权威完整替换成功后，由对应 PointAllocationRuntime 只发布一次 PointAllocationReplacedEvent。权威替换不推断或逐项发布本地点数、等级变化事件；监听者收到替换事件后从同一运行时对象重新查询完整快照。

## Editor 设计重点

Editor 是逻辑配置工具，不是游戏 UI 布局工具。

Editor 使用两类资产分离编辑信息与运行时定义：

~~~text
PointAllocationAuthoringAsset        // 仅 Editor
├─ DefinitionId
├─ Nodes + authoring label/note/position/locked
├─ Connections
├─ NextNodeId
└─ Canvas state

PointAllocationGraphDefinitionAsset       // Runtime 可加载的导出结果
├─ DefinitionId
├─ Nodes
└─ Connections
~~~

PointAllocationAuthoringAsset 是唯一编辑源；PointAllocationGraphDefinitionAsset 是可覆盖生成的导出产物，不允许在加点图编辑器中反向编辑。运行时资产不得包含节点坐标、锁定状态、画布状态、编辑标签或备注。

- 画布展示 PointAllocationNodeDefinition 与 PointAllocationConnectionDefinition。
- 节点坐标、缩放和画布平移只属于 Editor authoring 元数据，不进入运行时定义。
- 第一版只提供分层有向图自动布局，不实现放射状、环形、力导向或其他布局算法。
- 分层布局允许从上到下或从左到右；方向和节点间距是同一布局算法的参数，不视为不同布局类型。
- 多个入口节点位于起始层，多前置节点根据依赖关系分层；自动布局应尽量减少连线交叉并保持重复整理结果稳定。
- 节点允许自由拖动并保存 Editor 坐标；自动整理支持全图和选中区域，已锁定节点不得被自动移动。
- 创建画布连线就是创建 Allocation Connection，并默认 RequiredRank = 1。
- 选中连线可以编辑 RequiredRank。
- NodeId 由 AuthoringAsset 的单调递增 NextNodeId 自动分配且只读；删除节点后不得降低 NextNodeId 或复用旧 Id。
- 删除节点时必须同时删除其所有入边和出边。
- 导出前必须验证全部定义不变量并阻止非法导出。
- GraphView 只能存在于 Editor 实现内部，运行时类型与导出格式不得依赖 GraphView。

## 项目 UI 接入

项目 UI 持有需要展示的 PointAllocationRuntime，并通过 NodeId 绑定节点运行时状态。允许显示的节点连线来自该运行时对象使用的 PointAllocationGraphDefinition.Connections。

玩家、角色、职业方案、预设栏位等业务身份与 PointAllocationRuntime 的映射由项目负责。PointAllocation 模块不保存这些身份，也不要求项目转换为另一套框架 InstanceId。

项目 UI 自行决定节点坐标、整体布局、可见性、置灰方式、样式、图标、动画，以及是否隐藏某条现有 Allocation Connection。树状、放射状、星盘状等游戏表现布局均属于项目 UI，不属于加点图编辑器的布局职责。

框架只提供节点当前等级、最大等级、是否能加点、失败原因、可用点数和连接状态等逻辑数据。

## 基本接入流程

1. 在 Project 窗口通过 `Create/JulyGF/加点图/编辑源` 创建编辑源资产。
2. 双击资产或通过 `JulyGF/加点图编辑器` 打开图编辑器。
3. 编辑节点、等级成本和连接，执行 Validate 后导出 `PointAllocationGraphDefinitionAsset`。
4. 项目加载导出资产并注册定义，再创建需要的独立运行时对象。

~~~csharp
var pointAllocationSystem = new PointAllocationSystem();
architecture.RegisterSystem(pointAllocationSystem);

if (!definitionAsset.TryCreateDefinition(out var definition, out var errors))
    return;

if (!pointAllocationSystem.RegisterDefinition(definition).Success)
    return;

var createResult = pointAllocationSystem.CreateRuntime(
    definition.Id,
    PointAllocationSnapshot.Empty(initialPoints),
    out var runtime);

if (!createResult.Success)
    return;

runtime.ProgressChanged += OnPointAllocationProgressChanged;
runtime.AddRank(nodeId);
~~~

项目根据自身存档或服务器协议显式转换 PointAllocationSnapshot，并自行保存业务对象到 PointAllocationRuntime 的映射。

## 实现守则

- 配置只有 Nodes 与 Connections 两类核心事实，避免双向持久化和重复关系。
- 配置注册后按不可变数据处理；运行时不得修改定义。
- Editor 与 Runtime 必须调用同一份定义和进度校验逻辑，不复制校验规则。
- 查询返回独立不可变快照，不暴露内部集合。
- 本地命令先完整验证，再原子提交，最后发布事件。
- 权威恢复与本地命令分开；恢复不得推断本地状态迁移事件。
- 第一版仅支持 Unity 主线程同步调用，不引入锁或异步命令。
- 不因未来可能需要而提前增加多点数池、复杂条件表达式、配置版本或效果执行框架。
