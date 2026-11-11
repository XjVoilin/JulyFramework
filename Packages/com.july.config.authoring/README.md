# July Config Authoring

Unity 2022.3 的可选 Editor 包，复用项目现有 dotnet 和 Luban 工具。

## 使用

- 配置统一在 `Project Settings → July → 配置表`：生成方案、每个任务的 C# / JSON 输出目录。
- 输入固定为 `Tools/Luban/DataTables`，默认使用其中的 `luban.conf`；命名空间来自配置文件，均在设置页只读展示。
- 工具固定为 `Tools/Luban/Luban/Luban.dll`，目标/格式固定为 `all` / `cs-simple-json` / `json`。
- 打开 `JulyGF → 配置表 → 生成窗口`，点击“生成配置”。多任务时提供任务选择、“生成全部”和“生成选中”。
- 生成窗口只包含生成操作和“设置”入口，不展示输入输出配置，不提供刷新或日志面板。生成成功输出一条 Unity Console Log，失败输出 Error；不在面板显示生成结果。

默认输出为 `Assets/Game/Scripts/Runtime/Generated` 和 `Assets/Game/Res/Configs`。输出必须位于项目 Assets 的子目录，代码/数据及各任务目录不能重叠。

设置保存到 `ProjectSettings/July/ConfigAuthoring/settings.asset`。文件归属保存在同目录的 `outputs.json`，两者应提交版本控制。`Library/July/ConfigAuthoring` 仅用于生成暂存，不保存生成日志。

## 项目扩展

生成方案是实现 `IConfigGenerationScheme` 的普通 C# 类，提供公开无参构造函数，在 Project Settings 选择类型，不创建 ScriptableObject 资产。`CreateTasks` 返回有序任务，可声明现有 conf，或提供数据目录与 schema，由框架生成临时 conf。任务 ID 用于保存输出覆盖，需保持稳定。

任务的 `IConfigOutputProcessor` 负责附加产物。默认 `TablesExtensionProcessor` 生成 `TablesExt.cs` 的表名与注册方法。项目处理器使用暂存目录及表类型、属性名、加载键元数据生成自己的 Provider；所有产物共同发布。

面板直接继承 `ConfigAuthoringWindow`，并声明项目自己的 MenuItem：

```csharp
public sealed class ProjectConfigWindow : ConfigAuthoringWindow
{
    [UnityEditor.MenuItem("项目/配置表生成")]
    private static void Open() => OpenWindow<ProjectConfigWindow>("项目配置表");

    protected override void DrawContent()
    {
        // 在这里绘制项目专属操作。
        DrawDefaultContent();
    }
}
```

不需要面板接口、注册器或 SO。可以复用 `DrawDefaultContent()`，也可以自行绘制内容。生成按钮、关卡编辑器及 CI 均调用 `ConfigGeneration.GenerateAll()` 或 `ConfigGeneration.Generate("模块ID")`，统一读取 Project Settings。

`Samples~/BoxProject` 是可选示例：Common 优先、模块独立输入输出、附加 Config/Localization/UIWindow Provider、继承窗口与自定义菜单。导入后在 Project Settings 选择其普通 C# 方案。现有 GooseMarket、GreedyGoose 无需修改。

## 发布行为

所选任务与处理器全部在暂存目录完成后再统一更新正式输出；失败保留旧产物，发布 I/O 失败恢复已写文件。只清理自身归属的旧文件与 meta，保留手写文件及现有 GUID。首次识别带 auto-generated 标记的 C#，根据现有 Tables.cs 识别对应 JSON。

任务需使用独立输出目录。修改方案时保留任务 ID；清除归属清单会失去已搬移文件的归属记录。Play、编译及生成期间禁用生成。

## CI

在没有其他 Unity 实例打开的项目目录执行：

```text
Unity -batchmode -quit -projectPath <project> -executeMethod July.Config.Authoring.ConfigGeneration.GenerateAllForCI
```

失败抛异常，错误同时显示在 Unity Console。包不修改 Release 流水线，不安装或升级工具，也不增加 Player 程序集。
