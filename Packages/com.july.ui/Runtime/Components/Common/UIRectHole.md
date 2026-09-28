# UIRectHole

可选 UGUI Graphic，挂到项目 prefab 的全屏 RectTransform 上，设置 Color。
窗口把洞口转换为此 RectTransform 的本地 Rect 后调用 SetHole；目标移动时由窗口更新。
ClearHole 恢复完整遮罩。raycastTarget=false 全部放行，true 时 PassThroughHole 决定是否放行洞口。
组件不认识 Guide、目标编号、相机注册或业务状态。已有 UIHollowMask shader 用于纹理形状洞口，
本组件解决明确的矩形几何洞口，两者不相互依赖。当前 GreedyGoose 教学没有高亮需求，未挂载本组件。
此改动位于本地 July UI 源码，尚未发布；GreedyGoose 继续使用已固定的 July UI 版本。
