# July Input

JulyFramework 运行期输入入口：鼠标/触摸点击、四方向滑动、方向键，以及玩法/UI 的统一阻断。
首期使用 Unity legacy 输入和 UGUI，不包含长按、双击、多指、操作映射或多后端适配。

## 接入

在 UISystem 之前注册 `new UnityInputSystem(config)`；由 ArchContext.Update 驱动。
InputConfig 可嵌入项目 ScriptableObject，配置识别开关、按键和参考距离，构造时校验并复制。
通过 IInputSystem 订阅 Direction / Clicked，消费者结束时解除订阅；点击输出屏幕像素位置。
项目负责对象选择、世界射线和操作许可，不直接重复轮询 UnityEngine.Input。

```csharp
var input = context.GetSystem<IInputSystem>();
input.Direction += OnDirection;
input.Clicked += OnClicked;
// 消费者退出时：
input.Direction -= OnDirection;
input.Clicked -= OnClicked;
input.CancelPointer();
```

## 统一阻断

```csharp
var gate = context.GetSystem<IInputGate>();
using (gate.Block(InputScope.All))
{
    await LoadAsync();
}
```

- Gameplay 阻断本模块产生的玩法意图；UI 阻断 July UI 接入的 UGUI 交互；All 为两者组合。
- 每次申请返回独立 IDisposable，调用方持有并释放；多个拥有者互不覆盖，重复 Dispose 无副作用。
- `IsBlocked(scope)` 表示所选范围任一被阻断；`BlockStateChanged` 仅在有效范围变化时通知，监听者读取当前状态。
- 原始无参数 Block/Unblock 已移除。没有全局清零、定时解锁或超时自动释放。
- 阻断取消未完成交互；恢复不补发旧输入。已执行业务、动画、网络、模拟不回滚或暂停。
- UI 正常事件由 Unity 原生流程分发；回调内申请阻断，允许当前一轮处理完成后统一取消交互。清理通知仍会发出，但取消路径不合成点击、提交或 Drop。
- UI 和 Gameplay 解除阻断后先等待各自相关按压释放，再接收新操作；不提供旧按压未结束时另一个新指针立即恢复的保证。
- 全局 UI 阻断不会绕过界面射线产生玩法点击穿透。
- IInputGate 不拦截代码直接调用业务方法、第三方自行轮询设备或操作系统快捷键。

UI 依赖 Input，Input 不依赖 July UI。没有 UI 的场景仍能使用 Gameplay；UI 范围的执行由 July UI 接入提供。
运行期框架建立前的启动重试界面由 Bootstrap 处理，不依赖尚未初始化的 Input。

## 指针与键盘规则

- 有触摸时优先处理触摸，避免模拟鼠标重复；一次跟踪一个 fingerId，不接管已按住的其他手指。
- 在 UGUI 射线命中区域按下不开始玩法手势；场景 Collider 不算 UI 阻挡。装饰图形关闭 Raycast Target。
- 已开始的玩法手势经过 UI 不转交；松开时点击与滑动互斥。
- 点击要求整个手势最大偏移不超过 ClickTolerance；移出再返回不算点击。
- 滑动按松开位移主轴输出一个方向，等幅斜线按横向；不足阈值不输出。
- 距离按按下时屏幕短边换算到 ReferenceShortSide；坐标输出仍为实际屏幕像素。
- 方向键按下输出一次，不连发；UGUI 当前选择对象占用导航或文本编辑时，不同时输出玩法方向。
- Gameplay 阻断、失焦、TouchPhase.Canceled 或 CancelPointer 撤销未完成手势。失焦通过 Application.focusChanged 直接通知，即使后台不运行 Update 也会取消；初始化时订阅，关闭时解除。
- 所有调用在 Unity 主线程进行；业务 System 继续检查玩法阶段和操作许可。

## 窗口与项目

UIOpenOptions.BlockGameplayInput 由 UIWindowSession 持有，从开始加载至关闭完成；排队请求未开始时不占用。
该选项只阻断 Gameplay，不会禁掉窗口自身。独立流程需要全部禁用时直接申请 All。
GreedyGoose 启用滑动/方向键、关闭点击；项目 LevelControlSystem 订阅方向并统一处理方向、剪尾和重开，提供按钮可用状态；Guide 设置和恢复允许操作集合，Session 只判断业务条件，加载与退出持有 All。

## 验证状态

本轮只做静态核对，未运行测试、Unity 编译或真机验证。
已有 InputGateTests 源码同步为凭据 API；UI/项目测试初始化顺序同步为先 Input 后 UI。
实际 UI 取消、编辑焦点、鼠标/触摸、嵌套阻断和窗口加载失败仍需运行验收。

## 手势识别结构

UnityInputSystem 负责设备采集、指针归属、UI 命中和全局阻断；ClickRecognizer 与 SwipeRecognizer 只接收 GestureSample，返回 GestureResult，不读取 Unity 输入、不调用业务。
IGestureRecognizer 提供 Begin / Update / End / Cancel。按住期间即使位置不变也调用 Update；样本包括屏幕位置、参考像素位移与未缩放的经过时间。返回结果统一由 System 发布，当前每次交互最多产生一个结果。
配置决定创建哪些识别器。点击容差小于滑动距离，保证两者互斥，不依赖注册顺序。新增长按、双击时必须明确与现有识别器的互斥或延迟规则；当前没有通用优先级、竞争图、多指采集或动态注册机制。键盘方向不包装为手势。
Gameplay 仅在进入阻断或失焦时取消一次；等待设备释放并经过一个空闲更新后恢复，不再维护独立的恢复帧编号。ArchContext 每帧调用一次 OnUpdate。
保留现有阻断、指针归属、点击与滑动互斥、移出后返回的测试源码和采样入口，未运行测试或 Unity 编译；实际设备采集与恢复时序仍需运行验收。
