# July Input

JulyFramework 的基础玩法输入与统一阻断入口。使用 Unity legacy 输入和 UGUI，由现有 Arch 生命周期驱动。

基础指针帧支持鼠标、多指、取消和 UI 起点过滤；内置点击、四方向滑动和方向键是可选便捷识别。关闭识别不会关闭基础采集。

## 接入

在 UISystem 之前注册 `new UnityInputSystem(config)`。InputConfig 只配置便捷识别；构造时校验启用的能力并复制配置值。

```csharp
var input = context.GetSystem<IInputSystem>();
var frame = input.ReadFrame();
for (var i = 0; i < frame.PointerCount; i++)
{
    var pointer = frame.GetPointer(i);
    // 用 pointer.Id 跟踪本次按压，按 Phase 处理业务。
}
```

只使用基础输入的项目可设置 EnableClick、EnableSwipe、EnableKeyboard 为 false。没有手势识别器时，鼠标、多指与取消仍然正常工作。

## 基础帧契约

- ReadFrame 按 Unity 帧号缓存设备采样；业务 Update 即使早于 Arch.Update，也能取得当前帧数据。
- 多次读取不消费输入，不派发 Clicked、Direction 或取消回调。内置识别由 Arch.Update 每帧推进一次。
- InputFrame 是不可变值快照。后续采样与同帧取消不会修改已经返回的帧；屏蔽或显式取消发生后，应重新读取，不能继续提交旧快照中的操作。
- FrameCount 是 Unity 帧号，Time 是未缩放时间。空帧也提供时间和 ResetVersion，支持长按计时和两次按压之间的等待。
- PointerCount 是本帧的样本条目数，包含 Ended/Canceled，不等于仍按住的手指数。鼠标在同帧按下又松开时，同一 ID 按顺序提供 Began、Ended 两条样本。
- 指针 ID 在本次按压期间稳定；跨帧跟踪使用 ID，不使用集合索引。触摸 ID 使用 fingerId，鼠标主键使用 -1。
- Position、Delta、StartPosition 使用屏幕像素；StartTime 使用未缩放时间。持续时间可由 frame.Time - pointer.StartTime 得到。
- 指针阶段为 Began、Moved、Stationary、Ended、Canceled。正常松开与取消分开表达；IsActive 仅在前三种阶段为 true。
- 基础层记录所有被接收的指针。单指选择、双指配对、两指转单指的玩法行为由消费者决定。
- 有触摸时优先读取触摸，避免模拟鼠标产生重复操作。不接管没有观察到 Began 的已按住指针；已跟踪指针从设备消失时输出 Canceled。
- 所有调用在 Unity 主线程进行。基础输入不检查格子、物体或玩法规则，业务仍负责操作许可。

## UI 归属

在 GraphicRaycaster 命中区域开始的按压不进入玩法指针帧，移出 UI 后也不补造 Began。场景 Collider 的命中不属于 UI 遮挡；装饰图形应关闭 Raycast Target。

已经接收的玩法指针经过 UI 时保持原交互归属。业务若不允许在 UI 上松开提交，可使用 `input.IsOverUI(pointer.Position)` 查询当前位置。查询只返回事实，不修改指针状态。

UI 内部交互继续通过 UGUI 的 PointerEventData 处理。July Input 不依赖 July UI；UI 范围的阻断由 July UI 接入执行。

## 阻断、取消与恢复

```csharp
using (input.Block(InputScope.All))
{
    await LoadAsync();
}
```

- Gameplay 阻断基础玩法指针和内置识别；UI 阻断 July UI 接入的交互；All 为两者组合。仅 UI 阻断不会取消玩法指针。
- 每次 Block 返回独立 IDisposable；释放只撤销本次贡献，重复 Dispose 无副作用。
- IsBlocked(scope) 表示所选范围任一被阻断；BlockStateChanged 仅在有效范围变化时通知。恢复等待不属于阻断计数。
- 进入 Gameplay 阻断、失焦、显式 CancelPointer 和关闭系统，都会取消当前交互并递增 ResetVersion，即使没有活动指针也递增。
- ResetVersion 是整个输入序列的失效标记。可选识别对象应在版本变化时清空等待中的状态，例如首击已结束后仍等待第二击的双击识别。
- 单个设备指针取消只影响对应 ID，不递增全局 ResetVersion，不取消其他手指。
- PointerCanceled 提供 ID、最后位置和原因。显式取消、阻断、失焦立即通知；设备取消/丢失先进入帧，再在正常 OnUpdate 通知，因此读取帧不会重入业务。
- 通知前先提交取消状态。同一已结束交互不重复通知。事件与 Canceled 样本表达同一事实，同一个业务应选择一个取消收尾入口。
- CancelPointer 不修改阻断计数，但会等待相关设备释放；解除 Gameplay 阻断或恢复焦点后同样先等待设备空闲。确认空闲的这一帧不接收新操作，下一帧才恢复。
- 已执行的业务不回滚。没有全局计数清零、定时解锁或超时释放；直接业务调用和第三方自行读取设备不受本模块控制。

## 内置便捷识别

```csharp
input.Clicked += OnClicked;
input.Direction += OnDirection;
// 使用者退出时：
input.Clicked -= OnClicked;
input.Direction -= OnDirection;
input.CancelPointer();
```

内置识别独立选择一个新按下的指针，不接管已按住的其他手指。点击要求整个按压过程最大偏移不超过 ClickTolerance；移出后返回不算点击。滑动在松开时按主轴输出方向，等幅斜线按横向。两者由阈值保证互斥。

阈值按按下时屏幕短边换算到 ReferenceShortSide；基础帧仍保持真实像素坐标。方向键按下输出一次；UI 占用键盘导航或文本编辑时，不同时输出玩法方向。

识别结束只清理识别状态，不删除基础样本、不递增 ResetVersion、不发出 PointerCanceled。业务回调若取消或屏蔽后立即解除，系统仍通过重置版本丢弃本轮剩余的旧识别结果。

## 后续增加手势

稳定扩展点是 ReadFrame，不是当前 internal IGestureRecognizer。该内部接口只用于协调已有点击与滑动，不承担跨按压、多指或通用竞争调度。

未来可在包内增加具体的长按、双击或缩放识别对象，由玩法局部持有，在已有 Update 中读取每一帧（包括空帧），返回各自类型的结果。它们不读取设备、不消费基础输入，也不需要额外 System 或全局注册器。

- 长按读取持续时间，输出开始/持续/结束；成立后不终止基础指针。
- 双击在第一次松开后继续等待，并用空帧时间判断超时。立即单击或等待双击窗口，是这个组合自身的明确策略。
- 缩放按稳定 ID 选择两根手指，输出中心位移和比例；业务决定镜头限制与单指接续。
- 新识别对象比较 ResetVersion，变化时清除旧候选；其拥有者停用/退出时主动 Reset 并清理交互表现，恢复后只接收新的 Began。
- 单击/双击、点击/长按等冲突由实际需要的组合处理。同一业务不要同时接收内置 Clicked 和组合自身的单击结果。

新增手势只影响选择使用它的调用方，不改变基础帧或已有 Clicked/Direction 的行为。本次未新增这些手势类。

## 验证

Tests 通过可控的内部输入源，使用 ReadFrame、OnUpdate、Block、CancelPointer 等正式入口验证基础帧、多指、门控与内置识别。测试源不属于公开运行期扩展接口。

实际设备、UGUI 派发、微信/抖音触摸和焦点恢复仍需要运行环境验收。July UI 的取消收尾与业务 PointerUp 提交语义应在升级联动中单独检查。
