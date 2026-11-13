# July UI Authoring

倍率命名已统一为 `^N`，例如 `img_logo^0.5`、`img_prop[240x240]^0.5`。旧 `*N` 不再解析；请将旧 PSD/Figma 节点改名并重新导出，旧 JSON 中的星号倍率会明确报错，不会静默按新倍率解释。PNG 资源文件名保持不带倍率。

从 Photoshop/Figma 的 `*_ui_data.json` 和 PNG 生成 Unity UI Prefab。支持历史 `_ps_data.json`、`_figma_data.json` 文件名。

## 导出协议 v2

JSON 根的 `schemaVersion` 为 2。图片位置与宽高是可带小数的 PNG 渲染矩形，包含透明留边。旋转/缩放已烘焙进 PNG，图片节点 rotationZ=0、scaleX/Y=1。`^N` 只控制 PNG 像素，不参与 Transform 缩放，也不通过历史 `$` 后缀改成 PNG 原生尺寸。

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

## 文字字号与换行修复（2026-11-13）

文字字号由导出端折算 PS/Figma 文字变换。wordWrap 控制 TMP 自动换行，PS 点文字及 Figma 自动宽高文字为 false；旧 JSON 缺省为 true，保持既有行为。本次使用游戏实际字体及 Canvas/TMP 网格验证购物喽字号 105、三个字符、单行显示；独立 Unity 工程 25 项通过，无跳过。该用例通过 ART2UGUI_TEST_FONT_ASSET 指定已导入的游戏字体，需要 TMP 基础资源。

本次新增修复仍在维护源码中，0.2.2 发布包不包含此新增换行字段；消费项目需更新修复包或临时本地引用，重新导入/生成 Prefab 后生效。

## PS 固定画布（2026-11-13）

PS 节点可用 img_prop[240x240] 指定固定逻辑画布。小图不放大，大图等比缩小，居中补透明，面板留边包含在画布内；原设计中心不变。若使用 ^0.5，PNG 为 120×120，逻辑矩形仍为 240×240。JSON fixedCanvas=true 才移除尺寸后缀，其他来源的方括号名称保持原样；导入器按 JSON 矩形布局，Scale=1，不再进行第二次尺寸调整。固定尺寸仅作用于当前图片，不继承给独立子节点或文字。

本地包已同步解析，未更改包版本、未发布远端；原 0.2.2 Git 标签不包含本次功能。独立 Unity 30 项检查全部通过，包括原生 PS 240×240 PNG 的资源查找、布局及 Scale；ART2UGUI_TEST_FIXED_EXPORT 可指定 FixedCanvasCase0 目录运行跨工具用例。
