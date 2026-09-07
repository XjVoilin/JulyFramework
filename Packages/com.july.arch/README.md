# July Arch

July Arch 用 System、Store、Procedure 和 View 组织模块。项目按这些角色约定代码归属，复杂实现通过普通类拆分。框架提供访问能力，职责约定主要由项目组织和代码审查维持，不要求每个模块单独建立程序集。

## 基本角色

| 角色 | 职责 |
| --- | --- |
| `System` | 提供模块能力，管理业务过程及其运行数据的生命周期，协调 Store、普通类及其他模块 |
| `Store` | 管理需要独立维护的业务数据，默认用于长期玩家数据，并维护相关的数据关联、索引和缓存 |
| `Procedure` | 编排包含动画、等待、广告等异步环节的一次业务流程 |
| `View` | 处理显示和输入，将业务操作交给 System 或 Procedure |

这里“长期玩家数据”指业务含义会延续到下次运行的记录，例如资产、养成进度和已完成任务。数据可以保存于本地，也可以由服务器维护、在登录或响应时重新下发；它仍然会随着业务操作发生变化，并非数值永远不变。

长期玩家数据是 Store 的默认用途，不是基类限制。需要脱离某次玩法运行独立维护的数据，也可以使用 Store；例如独立于战斗存在、需要单独同步的多人房间数据。仅仅有多个读取者，不足以要求把 System 的运行数据搬入 Store。

Store 的职责与本地存档机制分别决定。继承 `StoreBase<TData>` 不会自动保存数据；需要本地恢复和保存时，由组合根向 SaveSystem 声明。服务器提供的数据也可以使用相同的 Store，无需增加一种服务器 Store 基类。见 [ADR 0004](../../docs/adr/0004-declare-store-persistence-at-composition.md)。

## 数据与逻辑的归属

| 遇到的内容 | 默认归属 |
| --- | --- |
| 当前战斗的单位、冷却、当前轮次、临时选择 | System 持有的数据对象或普通类 |
| 货币、装备、养成等级、关卡进度 | Store 及其数据对象 |
| 用于查询上述 Store 数据的派生索引或缓存 | 跟随 Store 维护，不因其无需保存而另拆一份所有者 |
| 一次操作的规则判断、执行顺序、跨模块协调 | System |
| 一段内聚的复杂计算或模拟 | System 使用的普通类 |
| 输入和显示 | View |
| 需要等待表现或外部异步行为的业务步骤协调 | Procedure |

先判断：这份数据是否属于这个 System 管理的一次运行？属于，就先由 System 持有，并负责创建、推进和结束；需要脱离该运行独立维护时，再考虑 Store。数据是否来自服务器、是否需要保存，都不能单独决定归属。

例如战斗后来支持退出续玩，System 仍可管理正在运行的 BattleRun，保存与恢复机制负责记录和重建它。需要独立保留的恢复记录可以放入 Store，但不要求把整个运行对象迁入 Store，也不预先要求每个玩法建立快照和转换层。确实同时存在恢复记录与运行对象时，应明确记录更新和恢复的时机，避免两份数据都作为可独立修改的当前业务事实。

## System 与普通类

System 负责模块的业务入口、运行逻辑和生命周期，可以直接持有运行数据对象，也可以通过普通类组织复杂逻辑。拥有运行数据不要求把所有字段和算法写进 System，也无需把每次调用后仍然存在的数据都放进 Store。

System 可以：

- 读取和修改 Store，或调用 Store 提供的完整数据操作；
- 持有战斗、棋盘等临时运行对象；
- 持有并调用计算、生成、选择或模拟等普通类；
- 协调其他模块能力，管理订阅、时钟和取消等运行资源。

普通类是模块内部实现，不是新的框架角色。只有拆分能够隐藏一项独立复杂度时才拆分；不按文件长度强制增加层级，也不要求所有普通类都是无状态的纯计算。

业务 System 默认通过状态和事件与表现协作；需要等待表现时使用 Procedure。框架现有的 View 访问能力不等于每次业务操作都应直接操纵 View。UI、引导等以表现为职责的框架能力按各自包契约使用这些能力。

## 运行数据的读取约定

View、Procedure 和其他 System 可以通过负责该运行的 System 查询当前数据，例如 `BattleSystem.CurrentRun`。Store 不是所有业务数据的唯一读取入口；读取者默认只查询，业务修改通过 System 提供的操作完成。

查询入口应说明没有运行时的行为，例如 CurrentRun 在开始前和结束后为 null。运行数据被替换或清理后，旧引用不会自动指向新一轮；每次刷新显示或在异步等待后继续处理当前运行时，应重新查询。若某个流程专门绑定旧的一轮，则由该流程按运行身份或取消机制处理结束，不应把旧操作应用到新一轮。

可以直接返回普通数据对象，按需要使用可读属性或查询方法，不要求成套只读接口、部分快照或逐字段转发。返回对象引用不授予外部任意修改它的职责。

## Store 与 Data 的访问约定

一个 Store 管理一组归属一致、需要独立维护的业务数据。模块只有运行数据时可以没有 Store；需要 Store 时先从一个开始，在数据归属或生命周期有明确分离需求时再拆分，不因字段多而机械拆分。维护这些数据所需的索引和缓存跟随其所有者，不因无需保存而单独搬到 System。

`GetData()` 返回当前可变对象的引用，不会复制，也不是只读快照。项目可以让 System 直接读取和修改简单 Data，不强制为每个字段提供 setter、只读接口或快照类。View 可以读取显示所需的数据；业务修改默认从 System 或 Procedure 进入。

修改时按数据关系选择方式：

- 简单字段且没有派生关联：System 可以直接修改 Data，再通过具体 Store 的入口通知变更。
- 需要同时维护关联字段、索引或缓存：由 Store 或其内部数据对象提供完整操作，在数据一致之后通知。规则判断、业务编排和跨模块操作仍由 System 负责。
- 整体装载或服务器覆盖：使用 `ReplaceData(data)`；若具体领域包提供专门的覆盖方法，按该包的契约使用。

当前 `MarkDirty()` 是 protected。直接修改 Data 不会自动标脏；需要此种使用方式的具体 Store 可以提供一个公共通知方法，示例见下文。这个方法仅转发变更信号，不会自动重建索引、更新缓存、发送服务器请求或发布项目业务事件。

`DirtyMarked` 是数据修改信号，不代表写盘完成。已声明本地持久化的 Store 由 SaveSystem 监听；未声明的 Store 不会因此自动保存。即使声明为 Critical，必须等待写入结果的调用仍需使用 SaveSystem 的 `SaveNowAsync`。

`ReplaceData` 会更换引用、调用 `OnDataReplaced()`，然后标脏。持有旧 Data 引用的对象不会自动转向新数据，因此跨调用读取时应重新获取当前 Data。覆盖本身不表示已向服务器提交，也不会自动发送项目定义的业务事件。当前基类不保证覆写钩子抛异常后回滚；外部数据应在调用前完成适用的边界校验，有派生状态的 Store 还需遵循其具体覆盖契约。

## 示例：临时战斗与长期进度

下面的类可以放在同一个项目程序集。`BattleRun` 随战斗创建和结束，由 System 持有；累计胜场由 Store 保留。示例采用现有 API，`NotifyDataChanged` 是项目自行提供的方法。

```csharp
using System;
using Cysharp.Threading.Tasks;
using July.Arch;

[Serializable]
public sealed class ProgressData
{
    public int Wins;
}

public sealed class ProgressStore : StoreBase<ProgressData>
{
    // 仅用于此例没有派生索引或缓存的 Data。
    public void NotifyDataChanged() => MarkDirty();
}

public sealed class BattleRun
{
    public int Round = 1;
}

public readonly struct ProgressChanged { }

public sealed class BattleSystem : SystemBase
{
    private ProgressStore _progress;
    private BattleRun _run;

    // 当前运行的查询入口；开始前和结束后为 null，读取者不直接修改。
    public BattleRun CurrentRun => _run;

    protected override UniTask OnInitializeAsync()
    {
        _progress = GetStore<ProgressStore>();
        return UniTask.CompletedTask;
    }

    public void StartBattle() => _run = new BattleRun();

    public void FinishBattle(bool won)
    {
        if (_run == null)
            throw new InvalidOperationException("尚未开始战斗");

        if (won)
        {
            var data = _progress.GetData();
            data.Wins = checked(data.Wins + 1);
        }

        _run = null;

        if (won)
        {
            _progress.NotifyDataChanged();
            Publish(new ProgressChanged());
        }
    }

    protected override void OnShutdown() => _run = null;
}
```

界面每次刷新时通过 `battleSystem.CurrentRun` 获取当前战斗，为 null 时显示未在战斗中的表现；下一轮开始后重新读取，不继续使用上一轮缓存的引用。累计胜场仍从 ProgressStore 读取。

组合根创建并注册 ProgressStore 和 BattleSystem，再初始化 Context。需要本地存档时，将同一个 Store 通过 `saveSystem.Persist(store, key, importance)` 声明，并且仍需 `context.RegisterStore(store)`；Persist 不代替 Arch 注册。SaveSystem 应先于依赖恢复数据的 BattleSystem 初始化。

## 示例：服务器覆盖长期数据

服务器维护的进度同样放在 ProgressStore。以下示例是另一种服务器权威项目的使用方式：本地提交胜负请求后，由网络响应入口调用 `ApplyServerProgress`，不同时使用上例的本地胜场累加作为权威结果。

```csharp
public sealed class ProgressSyncSystem : SystemBase
{
    private ProgressStore _progress;

    protected override UniTask OnInitializeAsync()
    {
        _progress = GetStore<ProgressStore>();
        return UniTask.CompletedTask;
    }

    public void ApplyServerProgress(ProgressData received)
    {
        if (received == null)
            throw new ArgumentNullException(nameof(received));
        if (received.Wins < 0)
            throw new ArgumentOutOfRangeException(nameof(received));

        _progress.ReplaceData(received);
        Publish(new ProgressChanged());
    }
}
```

此例复用上一段的 using、数据和事件定义。网络层完成协议解析，并按实际协议处理响应先后顺序后，再交给此入口。覆盖后把 received 视为 Store 当前持有的数据，后续修改遵守同一套通知约定；读取者重新调用 `GetData()`。是否需要本地缓存仍由组合根另行决定。

## Procedure 与事件

一次操作有明确的异步先后顺序，并需要等待表现或外部行为时，由 Procedure 协调。同一个事实变化需要多个独立对象监听时，使用事件。

Procedure 按需要携带表现所需信息，不要求每个步骤都创建一套 Result 类型。事件也不代替明确的调用顺序；接收方不应依靠偶然的订阅顺序完成同一次数据修改。

## 验证与失败策略

用户输入、配置、存档、网络和第三方数据在进入责任边界时验证。内部代码依赖已经建立的契约，不逐层重复验证。正常业务拒绝与内部违约应能区分，内部违约应保留可定位的失败信息。

一次计算得到的候选集合或判定结果，可以沿调用链传递，避免下游重复进行同样的筛选和计算。

## 适用边界

这些约定规定职责和修改责任，不固定目录层级、普通类后缀或每个模块的类数量。回合玩法、连续战斗和经营玩法可以采用不同的内部模型。既有领域包的具体 API 契约仍然有效；校正文档不会自动迁移其数据，也不要求为了符合命名而搬动现有缓存或索引。
