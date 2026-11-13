# July UI Authoring

从 Photoshop/Figma 的 `*_ui_data.json` 和 PNG 生成 Unity UI Prefab。支持历史 `_ps_data.json`、`_figma_data.json` 文件名。

## 导出协议 v2

JSON 根的 `schemaVersion` 为 2。图片位置与宽高是可带小数的 PNG 渲染矩形，包含透明留边。旋转/缩放已烘焙进 PNG，图片节点 rotationZ=0、scaleX/Y=1。`*N` 只控制 PNG 像素，不参与 Transform 缩放，也不通过历史 `$` 后缀改成 PNG 原生尺寸。

图片 PNG 将当前图层透明度归一为 100%，JSON opacity 为当前图层及祖先的有效透明度，Unity 应用一次。PNG 保留填充、蒙版和效果的局部透明度。普通图片 Image.color 使用白色；只有 `~` 图片是白色蒙版并通过 fontColor 还原颜色。Tint 与 `%` 的组合顺序不影响文件查找。文字按其文字字段、左右/居中对齐及透明度生成。

旧数据没有 schemaVersion 时，图片透明度视为已烘焙，避免再次乘透明度；基本布局继续读取。旧旋转或隐式 Tint 数据建议用新版导出器重新导出，无法仅根据旧 JSON 完整修复。

## 导入和返回结果

通过 `Tools/PS2UGUI/Art Importer` 导入解压后的美术目录。目标为 `Assets/` 下的 Art/Textures 目录，上级需要包含 `Res/`，Prefab 写入对应的 `Res/Prefabs/UI/{面板名}/{面板名}.prefab`。

`ArtImporter.Import` 返回 success、cancelled、error、prefabPath、复制数量及 missingSprites。JSON 解析、命名和布局由 Translate 统一负责，文件复制不另行解析后缀。先检查源图片与输出目录并确认覆盖，再复制资源，设置 Single Sprite，最后生成 Prefab。缺图、取消和失败不会报成功；prefabPath 只在成功时返回真实 Prefab 路径。

`PS2UGUIGenerator.GenerateFromJson` 同样返回 GenerationResult。覆盖保存保留原 Prefab GUID；临时根节点和生成上下文在异常及取消后清理。滑动条配对时保留 Fill 的独立 Tint 和透明度。

## 验证记录

2026-11-13 使用独立 Unity 2022.3.57f1 工程通过本包 22 项 EditMode 测试（无跳过），包括小数布局、Tint/九宫格命名、文字对齐、Sprite 和 Prefab 实际生成、透明度边界、缺图、输出目录失败、覆盖取消与 GUID 保留，以及真实 Photoshop 24.3.0 的 Tint 和旋转导出产物。修复后复查还覆盖滑动条 Fill、历史 `$` 和文字名称中星号的处理。

测试工程使用本机包依赖；游戏 URP 的两个较新 SortingLayer 回调仅在临时测试副本中禁用，以适配本机 2022.3.57 编辑器。未修改游戏依赖或 URP 维护源码，未验收游戏项目的实际画面和真实 Figma 客户端。设置 `ART2UGUI_TEST_EXPORT` 为 Photoshop 原生 `BoundsCase8` 导出目录可运行两个跨工具用例，否则这两个用例会明确跳过。

本次维护源码尚未发布新 UPM 标签。使用新导出器前，需要让消费项目引用更新后的 authoring 包；请勿修改 Library/PackageCache。
