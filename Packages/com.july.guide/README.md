# July Guide 0.3.1

面向 Unity / July Arch 的引导种子。框架执行有序教学单元，项目决定教什么、何时教、什么业务结果算成功。旧接口未被正式项目使用，本版不保留兼容执行路径。

## 责任边界

| 框架负责 | 项目负责 |
| --- | --- |
| 候选排序、唯一的步骤游标、完整 Procedure 生命周期 | 教学内容、出现条件、触发时机、业务失效条件 |
| 停止、跳过、失败、清理等待及结果通知 | 退出关卡/场景之前调用并等待 Stop |
| 整段 Completed / Skipped 记录 | 保存服务、账户隔离、存档恢复与落盘策略 |
| 通用确认、已接受的目标点击 | 收集/建造/交付等真正的业务完成条件 |
| Target 注册、几何跟踪和 UGUI 射线过滤 | 目标 ID、业务控件适配、玩法输入控制 |
| 表现等待与清理、默认 UGUI 皮肤 | 文本、本地化、美术 prefab、项目特殊表现 |

`GuideStepDefinition` 只有 `Id / Type / ParamId`。Type 和 ParamId 的解释属于项目的执行器选择入口，不是框架中的通用参数注册表。UI 参数由使用它们的确认/点击 Procedure 接收；纯业务步骤不必提供 UI。定义与 `GuideViewData` 均为构造后不可变对象。

## 最小接入

在项目已有 Architecture 的注册阶段注册 Store 和派生 System；保存数据在首次运行前恢复，定义不进 Store。

```csharp
context.RegisterStore(new GuideStore());
context.RegisterSystem(new GameGuideSystem());
// 由项目保存机制恢复：
context.GetStore<GuideStore>().ReplaceData(savedGuideData);
```

使用默认表现前，按下方“表现与皮肤”注册引导窗口资源，并初始化 IUISystem。纯业务单元不要求打开窗口。

以下示例只展示通用确认、Button 点击的接入。Type 是项目自己的内容编号。默认 ResolveText 原样返回文字；本地化项目覆盖此方法。

```csharp
using System;
using System.Collections.Generic;
using July.Arch;
using July.Guide;

public sealed class GameGuideSystem : GuideSystemBase
{
    private const int GuideWindowId = 13; // 示例项目分配的窗口 ID，不是框架保留 ID
    private const int Explain = 1;
    private const int OpenOrders = 2;

    protected override IEnumerable<GuideDefinition> CreateGuides()
    {
        yield return new GuideDefinition(1, new[]
        {
            new GuideStepDefinition(1, Explain),
            new GuideStepDefinition(2, OpenOrders)
        }, priority: 10, canSkip: true);
    }

    // 示例入口保证适用场景已准备好；正式项目在此读取自己的业务状态。
    protected override bool CanStart(GuideDefinition guide) => true;

    protected override ProcedureBase CreateStepProcedure(GuideStepContext context)
    {
        switch (context.Step.Type)
        {
            case Explain:
                return new GuideConfirmProcedure(context, GuideWindowId,
                    new GuideViewData("先了解订单，再完成一次交付。"));
            case OpenOrders:
                return new GuideClickTargetProcedure(context, GuideWindowId, 901,
                    new GuideViewData("点击打开订单页。",
                        maskType: GuideMaskTypes.Rectangle,
                        pointerType: GuidePointerTypes.Click,
                        placementType: GuidePlacementTypes.Bottom));
            default:
                throw new InvalidOperationException($"Unknown guide type {context.Step.Type}.");
        }
    }
}
```

在实际 Button 节点挂 `GuideButtonTarget`，TargetId 设为 901；场景中使用正常的 Unity EventSystem。项目在数据、UI 及输入入口准备好之后显式调用并观察 `RunAsync`；初始化、存档标脏、上一段结束不会自动触发下一段。

```csharp
GuideExitReason? result = await guide.RunAsync(ownerToken);
// 拆除场景、UI 或业务对象前：
await guide.StopAsync();
```

## 执行与取消合同

所有调用、目标事件以及 owner token 的取消均在 Unity 主线程进行。

- 每次 Run 评估一次候选。Completed / Skipped 不再参选；其余交给 CanStart 判断。优先级较大者先执行，同优先级取 ID 较小者；无候选返回 null。
- 同时只有一个执行。重复 Run 加入当前任务；加入者的 token 只取消自己的等待，首个调用者的 token 才取消执行。
- 框架按 Steps 顺序等待完整 Procedure。Procedure 的业务等待、订阅、表现和输入许可都必须在返回前收尾；不得把所拥有的清理另行 Forget。
- Stop 阻止新的 Run，并等待当前 Procedure 清理、取消回调分发和终态通知全部结束。任何一个 Stop 尚未退出时都不开放新的 Run。停止期间调用 Run 明确抛错。
- Skip 仅对尚未终结且 CanSkip 的运行生效。并发退出意图取第一个；实际失败优先。返回 true 表示最终确实为 Skipped。Aborted 不保存，可重新触发；Skipped 是持久跳过，不代表仅暂时关掉提示。
- 停止、跳过和外部取消统一经过同一取消入口。第三方取消回调抛错不会使 Stop 提前返回；错误在真正清理后由运行任务公开。
- Procedure 已收尾后才进入终态通知。通知期间 IsRunning 仍为 true，CurrentStepId 为 0；普通 Run 只能加入正在终结的旧运行，Stop 只等待，Skip 返回 false。全部通知结束后清空运行状态，再完成 Run 的返回任务。要触发下一段，在 await Run/Stop 返回后显式调用；不要在同步终态事件中等待新一轮。
- 同步 System.Shutdown 只能请求取消，无法替代正常的异步退出。项目应先 await Stop，再拆除 UI、相机和玩法数据。Stop 抛错也已等待清理，项目仍需用 finally 完成其余退出工作。

`GuideStepContext` 提供 GuideId、Step、CanSkip，以及目标等待、文本解析、跳过、诊断等待描述和 Fail。它没有 Next / Complete；只有 Procedure 返回，框架才推进。异步表现发现不能继续的错误时用 `Context.Fail(error)`，使运行失败并展开清理，不只记日志后继续等待。

## 自定义业务单元

完整交互的执行器继承 `GuideStepProcedure`。可以只等待业务事实，也可以在一个单元中完成多个连续动作。业务规则留在正常业务模块；引导消费其真实状态或事件。

```csharp
public sealed class WaitForDeliveryProcedure : GuideStepProcedure
{
    public WaitForDeliveryProcedure(GuideStepContext context) : base(context) { }

    protected override async Cysharp.Threading.Tasks.UniTask OnExecuteAsync(
        System.Threading.CancellationToken ct)
    {
        var completed = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
        void OnDelivered(OrderDeliveredEvent fact)
        {
            if (fact.OrderId == Context.Step.ParamId) completed.TrySetResult();
        }

        Subscribe<OrderDeliveredEvent>(OnDelivered);
        try
        {
            Context.SetWaitingFor($"Order {Context.Step.ParamId} delivery");
            await completed.Task.AttachExternalCancellation(ct);
        }
        finally { Unsubscribe<OrderDeliveredEvent>(OnDelivered); }
    }
}
```

示例需 `using Cysharp.Threading.Tasks;`。`OrderDeliveredEvent` 是项目实际交付提交后发布、含 OrderId 的业务事实，须由项目定义。订阅应建立在业务操作开放之前；若结果可能早已达成，在订阅后查询项目权威状态，不依赖历史事件重放。按钮点击仅证明交互已接受，不证明交付完成；能同步触发业务的按钮和业务观察应放在同一完整单元中。

玩法输入许可也由拥有它的 Procedure 在 using/finally 中释放。若一段连续教学需要始终限制输入，就将连续实践作为一个单元，在其中改变许可；不把资源留给返回任务外部的另一套生命周期。

## 表现与皮肤

`GuidePresentation` 负责目标等待/跟踪、确认/跳过、射线规则和失效传播；July UI 负责窗口实例、Canvas、层级、动画与资源生命周期。Guide 不创建独立常驻根节点，不直接实例化或销毁窗口。

种子提供一个 `Prefabs/DefaultGuideWindow.prefab`：根为 `GuideWindow : UIView`，子节点为可替换的 `GuideUguiSkin`。全屏遮罩/指针与 SafeContent 内的提示/按钮分开；SafeContent 复用 July UI 的 SafeAreaAdapter，坐标转换使用所属 Canvas 的实际相机。

接入步骤：

1. 在项目可采集的资源目录创建默认 prefab 的 Variant，例如 UIGuideWindow，按现有资源规则采集。需要不同结构时替换子皮肤，保留 GuideWindow 与序列化绑定。
2. 在项目现有窗口配置中分配窗口 ID、资源名，设置 `Layer=Guide`、`QueueMode=None`、`ShowMask=false`、`ClickMaskToClose=false`、`IgnoreSafeArea=true`。默认打开/关闭动画为 None，按需使用现有 UI 动画。
3. 向标准确认/点击 Procedure 传入该 windowId。框架不占用全局窗口 ID，不安装或覆盖项目的 Provider。

```csharp
var presentation = new GuidePresentation(GetSystem<July.UI.IUISystem>(), guideWindowId);
try
{
    await presentation.OpenAsync(Context,
        new GuideViewData("GUIDE_TEXT", confirmTextKey: "GUIDE_CONTINUE", skipTextKey: "GUIDE_SKIP"),
        GuideRaycastModes.AllowAll, 0, ct);
    await presentation.WaitForConfirmationAsync(ct);
}
finally { await presentation.CloseAsync(); }
```

同一 Presentation 可以顺序 Open 更新提示和目标，复用同一个窗口；不需要每个步骤制作 prefab。CloseAsync 必须在所属 Procedure 返回前等待，不传已经取消的执行 token；窗口关闭动画和逻辑清理完成之后，框架才推进或发布终态。输入许可应在关闭完成之后释放。

一个窗口实例只能由一个 Presentation 持有。若配置指向错误窗口或已被其它拥有者打开，明确失败，不接管或关闭其它窗口。正常收尾先解除关闭通知，再调用 UI.CloseAsync；窗口被外部提前关闭时，让当前教学失败并清理，不悬挂确认等待，也不把关闭解释为完成或跳过。

Open 不显示确认按钮，WaitForConfirmation 才显示。文案均经 ResolveText 解析。美术引用、字体和按钮布局通过 prefab 编辑；默认 `_messageSizePixels` / `_messageOffsetPixels` 为屏幕像素，提示限制在所配置的内容区域。装饰 Graphic 不拦截射线。

特殊对话、动画演示或玩法专用面板可以由 Procedure 编排其它 July UI 窗口；不要求塞进默认 prefab，也不增加皮肤注册中心。纯业务等待可以完全不显示窗口。

| GuideRaycastModes | 行为 |
| --- | --- |
| BlockAll | 阻挡底层 UI 射线，引导自身按钮仍可操作 |
| BlockOutsideTarget | 只让目标屏幕矩形内的 UI 射线通过 |
| AllowAll | 背景不阻挡 UI 射线 |

这些模式仅影响 UGUI，不代表键盘、手柄、原始触摸或 Gameplay 的输入许可。阻挡目标外需要正 TargetId；通用点击单元不能用 BlockAll。

## Target

- GuideTargetAnchor 启用时注册、禁用时向实际注册的 System 注销。正 ID 在同时存在的目标之间必须唯一。
- GuideUITarget 只提供自身 RectTransform 的屏幕矩形，不接管指针事件；挂到按钮文字/图标子节点不会改变正常点击路由。
- GuideButtonTarget 必须挂在实际 Button 上，从 Button.onClick 报告已接受的点击；禁用或不可交互 Button 不会因原始指针按下而被判成功。
- Toggle、项目自定义控件、世界交互若需要作为点击目标，应从实际接受的业务交互报告 IGuideClickTarget.Clicked，不用原始指针事件伪装业务接受。
- GuideWorldTarget 使用 Renderer Bounds 和显式 RegisterWorldCamera 的相机；只提供几何，不内置世界点击规则。

初次显示可以等待目标/世界相机出现；表现打开后每帧跟踪，射线查询时重新计算区域。目标注销、世界相机丢失或几何失败会使未完成交互失败，不继续沿用旧区域。没有通用超时、自动重新寻路或静默跳过；正确注册、业务失效与取消时机由项目负责。

## 保存与诊断

GuideStoreData 只包含 CompletedGuideIds / SkippedGuideIds；定义、运行对象和局内游标不存档。Completed、Skipped 写整段结果；Aborted、Faulted 不写。保存通过已有 Store GetData / ReplaceData / DirtyMarked 接入，框架不承诺已经落盘。调试重放先 Stop，再 Reset(guideId)。

内存 Commit 是结果提交点。如果之后 DirtyMarked 保存通知失败，已完成/跳过事实不倒退；GuideExitedEvent.Reason 保留结果，Error、LastFailure 和运行任务异常公开错误。不能将所有 Run 异常都解释成“教学没完成”。

可查询 IsRunning、CurrentGuideId、CurrentStepId、WaitingFor、LastFailure；LastFailure 在下一次真正启动时清空。生命周期事件为 GuideStarted、GuideStepEntered、GuideStepExited、GuideExited，仅用于观察，不负责推进。

## 升级与验证范围

0.2 的 Condition / Action / Completion / View 四 Handler、链式 NextStepId、持久化游标和自动触发已移除。0.3 早期草稿的 Step UI 字段、GuideBuiltInTypes、DefaultGuideStepProcedure 与 GuideInputModes 也已移除；改用本文的显式完整 Procedure、不可变表现参数与 GuideRaycastModes。

本包是面向后续多数常见 Unity / July 引导的基础，不预建通用图编辑器、跨场景抢占、局内断点恢复或所有输入后端。已在 Unity 2022.3.62f2、July Arch 0.1.3、July UI 0.2.28、UniTask 2.5.10 下完成真实编译与运行验收：8 项生命周期 EditMode、10 项 July UI / UGUI PlayMode 全部通过。0.3.1 将表现迁入真实 July UI，相机缩放、全屏遮罩/安全区、外部关闭、加载中取消及等待关闭动画均已验证。后续新项目仍需验证自己的内容、输入适配、皮肤和资源生命周期。

## 回归验证

通用测试随包保存在 `Tests/Editor` 和 `Tests/PlayMode`，不引用 GreedyGoose 内容，不增加生产测试入口。消费工程安装 Unity Test Framework 后，在 manifest 的 `testables` 中加入 `com.july.guide`，再从 Test Runner 或命令行运行：

```text
Unity.exe -batchmode -projectPath <消费工程> -runTests -testPlatform EditMode -assemblyNames July.Guide.Validation.SeedTests -testResults <结果.xml>
Unity.exe -batchmode -projectPath <消费工程> -runTests -testPlatform PlayMode -assemblyNames July.Guide.Validation.UiTests -testResults <结果.xml>
```

UGUI 用例使用真实 UISystem、资源加载接口、GuideWindow prefab、相机 Canvas、Button、EventSystem 和自定义缩放皮肤；不要用 `-nographics` 运行这组图形行为验收。测试要求空白测试场景，由 fixture 创建并清理自己的对象。它们验证运行机制与通用交互，不代替项目自己的完整业务验收。
