# July Guide

配置表驱动、由项目扩展的新手引导运行时。包内只有一个 `July.Guide` 程序集，不包含 Editor、Tests 或 Samples。

## 职责

Guide 负责候选选择、单引导调度、线性步骤推进、进度、Target 生命周期、默认表现和 Handler 调用时机。

项目负责准备 `GuideStoreData`、注册业务 Handler、监听项目自己的状态事件，并在状态可能影响引导时调用 `NotifyGuideStateChanged()`。Guide 不关心 Store 数据来自配置、存档还是其他来源，也不监听 `DirtyMarked`。

## 接入

```csharp
context.RegisterStore(new GuideStore());
context.RegisterSystem(new GameGuideSystem());
```

GuideSystem 初始化时评估一次候选。项目如果在初始化之后替换 `GuideStoreData`，应在自己的 GuideSystem 中明确调用 `NotifyGuideStateChanged()`。

```csharp
public sealed class GameGuideSystem : GuideSystemBase
{
    protected override void OnConfigure()
    {
        RegisterConditionHandler(new GameGuideConditionHandler());
        RegisterActionHandler(new GameGuideActionHandler());
        RegisterCompletionHandler(new GameGuideCompletionHandler());
        Subscribe<PlayerStateChangedEvent>(_ => NotifyGuideStateChanged());
    }
}
```

Guide 没有通用配置变化事件，也不会把 `DirtyMarked` 当作引导触发器。

## 配置

- `GuideDefinition`：ID、优先级、入口步骤、是否允许跳过、开始条件和过期条件。
- `GuideStepDefinition`：ID、所属 Guide、下一步骤、Target、进入 Action、View、InputMode、Completion 和过期条件。
- `GuideHandlerRef`：`Type + ParamId`。Type 选择 Handler；ParamId 由 Handler 到项目 Store 或配置中解释。
- 所有 ID 和可扩展类型都是 `int`。引用字段中的 `0` 表示无引用或无下一步骤。

可扩展分类不使用 enum。框架只提供内置常量，项目通过新的 Type 和对应 Handler 扩展。GuideSystem 不解释项目自定义值。

Store 只验证 Guide/Step ID、引用关系和线性步骤结构，不验证项目 Handler、表现和输入的业务组合。

候选按 Priority 降序、GuideId 升序选择。任一时刻只运行一个 Guide 和一个 Step。

## Handler

- `IGuideConditionHandler`：同步读取项目 Store，判断条件。
- `IGuideActionHandler`：进入 Step 时执行一次项目动作。
- `IGuideCompletionHandler`：等待完成信号，不推进步骤、不修改进度。
- `IGuideViewHandler`：打开和关闭一种表现。

第一版只内置 `ImmediateCompletion = 0`。点击、拖拽、业务事件或其他完成方式由项目注册 CompletionHandler；框架不通过 Target 截获输入。

第一版不内置 StoreCondition、PresentationConfirm 或 Delay Completion。

## View 和输入

View 类型：

- `NoView = 0`
- `DefaultView = 1`

DefaultView 提供矩形遮罩、提示文字、静态指针和跳过按钮。项目需要本地化、美术资源、动画或其他布局时，注册新的 ViewHandler Type。

DefaultView 支持三个内置 InputMode：

- `BlockAll = 0`：阻止底层所有输入，Guide 自己的按钮可以操作。
- `BlockOutsideTarget = 1`：阻止 Target 外部输入，Target 区域透传。
- `AllowAll = 2`：Guide 背景不拦截底层输入。

MaskType、PointerType、PlacementType 和 InputMode 都是 `int`。项目自定义值由项目自己的 ViewHandler 解释。

## Target

```csharp
public interface IGuideTarget
{
    int TargetId { get; }
    Rect ScreenRect { get; }
}
```

`GuideTargetAnchor` 继承 `GameView` 并实现 `IGuideTarget`，启用时注册，禁用时注销。GuideSystem 直接管理 Target，不使用 Registry、字符串路径、Tag 或 `GameObject.Find`。

- `GuideUITarget` 使用自身 RectTransform 和所属 root Canvas 计算 ScreenRect。Overlay Canvas 不需要 Camera；Camera/World Canvas 使用自己的 Canvas Camera。
- `GuideWorldTarget` 暴露 Bounds，并从 GuideSystem 获取项目注册的 World Camera，将 Bounds 投影为 ScreenRect。

项目显式管理 World Camera：

```csharp
guideSystem.RegisterWorldCamera(camera);
guideSystem.UnregisterWorldCamera(camera);
```

Target 或 World Camera 可以动态加载；需要世界相机的步骤会等待相机注册完成。
