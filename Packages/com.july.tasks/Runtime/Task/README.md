# Task 模块

Task 是传统手游任务的最小状态核心。它只管理任务的完整数据、共享累计值、阶段状态迁移和领域事件，不负责玩法事件路由、时间策略、存储、网络、配置、UI、奖励内容或奖励发放。

## 核心数据

所有任务都由至少一个阶段组成，常规任务就是只有一个阶段的任务。

`TaskData` 是不可变值类型：

- `TaskId`：必须大于 `0`。
- `CurrentValue`：所有阶段共享的累计事实值，必须大于等于 `0`。
- `Stages`：至少包含一个按业务顺序排列的阶段。

`TaskStageData` 也是不可变值类型：

- `TargetValue`：阶段累计目标，必须大于 `0`。
- `State`：`Active`、`Completed` 或 `Claimed`。

阶段在列表中的下标就是它的定位信息，不额外保存阶段标识。完整数据写入只校验结构，不推断或修复累计值与阶段状态的一致性。服务器返回的 `Active + CurrentValue >= TargetValue` 等组合会被原样保留。

## 项目接入

项目只需注册一个派生系统：

```csharp
public interface IGameTaskSystem : ITaskSystem
{
    // 仅添加当前项目真实需要的能力。
}

public sealed class GameTaskSystem : TaskSystemBase, IGameTaskSystem
{
    protected override void OnConfigure()
    {
        // 注册项目事件监听。
    }

    protected override void OnDispose()
    {
        // 解除项目事件监听。
    }
}
```

核心命令不可重写。项目特有的每日任务、玩法事件映射、任务分组和自定义状态应通过同一个派生系统或上层服务组合，不需要再注册第二个 TaskSystem。

## 权威数据与序列化

网络协议或存档层应定义适合自身序列化工具的 DTO，并显式映射到 `TaskData` 和 `TaskStageData`。Task 模块不直接依赖 JsonUtility、Newtonsoft、Protobuf 或某个服务器协议。

服务器下发完整任务集合时调用：

```csharp
taskSystem.ReplaceAllTasks(serverTasks);
```

替换会先验证全部数据；任意一项非法或 TaskId 重复时返回 `false`，旧集合保持不变。成功后整个集合被覆盖，并只发布一次 `TaskCollectionReplacedEvent`。空集合是合法输入，会清空全部任务。

## 本地状态迁移

- `SetCurrentValue` 只接受绝对累计值；存在 Active 阶段时数值只能单调增加。
- 累计值一次达到多个阶段时，所有已达到的 Active 阶段都会变为 Completed，累计值不按阶段目标截断。
- 全部阶段均为 Completed 或 Claimed 后，继续设置累计值成功但不产生变化。
- `ClaimStage` 只把指定下标的 Completed 阶段变为 Claimed，重复领取已 Claimed 阶段成功但不产生变化。
- 领取阶段不检查前置阶段状态，允许跳过前面未领取的阶段。
- `ResetTask` 将累计值重置为 `0`，并将全部阶段恢复为 Active，阶段目标不变。
- `ResetAllTasks` 一次性重置全部已注册任务，批量修改完成后再按既有事件规则同步通知上层。
- 阶段目标和阶段结构不提供单独修改命令；变化通过完整集合替换完成。

奖励内容和实际发放属于上层。推荐顺序为：上层确认指定阶段为 Completed，成功发放该阶段奖励，再调用 `ClaimStage(taskId, stageIndex)` 记录已领取事实。上层可以通过 `TaskId + StageIndex` 关联奖励配置。

## 成就

成就使用相同的任务和阶段模型，不需要额外的任务类型或状态。成就“不重置”属于上层策略：不要对成就调用 `ResetTask`。

`ResetAllTasks` 会无差别重置当前系统中的全部任务。如果同一个系统同时保存普通任务和成就，上层不得使用该命令，应只对需要重置的任务逐条调用 `ResetTask`，或者由服务器计算后通过 `ReplaceAllTasks` 全量覆盖。

## 事件

本地修改可能发布：

- `TaskValueChangedEvent`：TaskId、前值和当前值。
- `TaskStageStateChangedEvent`：TaskId、StageIndex、前状态和当前状态。

一次累计值更新先发布数值事件，再按 StageIndex 从小到大发布阶段状态事件。事件进入同步 FIFO 队列；监听期间再次调用任务命令时，新事件追加到队尾，根命令在队列派发完毕后返回。监听异常由 Task 记录并隔离，不向命令调用方传播。

注册和移除任务保持静默。成功的完整集合替换只发布集合标记事件，不逐任务发布变化事件。

## 返回值与线程

命令返回 `true` 表示请求合法并被接受，不代表一定发生变化；合法的幂等无变化不记录日志、不发布事件。失败的修改命令只返回 `false`，普通失败不由 Task 模块记录日志。`TryGetTask` 未命中同样属于正常查询。

模块仅支持 Unity 主线程同步调用，不包含锁、异步调度或跨线程恢复逻辑。网络回调应先切回主线程，再调用任务系统。
