# Luban 引导配置模板

这是供新项目复制的最小作者模板，包含一个“确认提示 → 点击目标”的完整引导。它不依赖 GreedyGoose，没有水果、关卡、路线或订单概念。`July.Guide` 运行程序集仍不依赖 Luban 或任何生成表类型。

## 使用

1. 将本目录复制到项目的配置工具目录，保留 `Datas`、`Defines` 和 `luban.conf`。已有 Luban 工程可将三个 sheet 和定义合入现有配置源；不要额外创建配置 System。
2. 使用项目自己的 Luban 可执行文件生成，例如在本目录运行：

   `dotnet <Luban.dll路径> -t client -c cs-simple-json -d json --conf luban.conf --validationFailAsError -x outputCodeDir=<代码输出目录> -x outputDataDir=<JSON输出目录>`

3. 把 `SampleGuideSystem.cs.txt` 复制并改为 `.cs`，引用项目的生成类型。示例构造函数接收已加载的 `guide_sample.Tables`；已有 July.Config 项目直接改为从 `IConfigSystem` 取表。不要维护另一份运行配置模型。
4. 在 July UI 注册窗口 900，资源为默认 `DefaultGuideWindow` 或项目 Variant，使用 Guide 层。复制到新项目时将表中窗口编号替换为项目实际编号。
5. 在待点击 Button 上添加 `GuideButtonTarget`，设置 TargetId 为 101。在已有 Architecture 注册 `GuideStore` 和派生 System，UI 与目标准备好后调用 `RunAsync`；退出所属界面前等待 `StopAsync`。存档仍接项目现有保存机制。
6. 接入项目本地化并覆盖 `ResolveText`。原始示例显示 `SAMPLE_*` 键，方便独立验证。表中的文本、按钮文字和窗口编号都是项目内容；并不是框架保留值。

## 配置职责

| Sheet | 内容 |
| --- | --- |
| 引导 | 稳定编号、优先级、是否可跳过 |
| 步骤 | 所属引导、顺序、稳定步骤编号、执行类型、参数编号 |
| 表现 | 窗口、目标、提示与按钮文本 |

`sequence` 决定执行顺序；`stepId` 用于稳定诊断，不因调序重编。`GuideStore` 记录整段引导结果，已投放的引导编号不能随意复用。改变已完成玩家的投放策略属于项目存档/版本策略。

默认确认阻断 UI 点击，点击目标仅放行目标范围。非 UI 的输入（角色移动、快捷键等）由项目取得和释放自己的输入控制权。空 TargetId 只适用于无目标确认，ClickTarget 必须绑定实际目标。

`GuideStepKind` 是示例自己的类型编号。新增收集、建造等教学时，在项目添加参数表和完整 Procedure，并在唯一的 `CreateStepProcedure` 中分派。不要为了把业务逻辑塞进表而拆成“显示提示/等事件/计数/跳转”等通用指令。

## 校验与扩展

Luban 在生成阶段检查引导/表现引用、类型和组合索引；生成失败应阻止交付。合并进项目时给窗口和本地化字段加上该项目实际表的 `ref`，本模板不虚构这些外部表。

生成数据加载后直接映射定义，类型与参数表不匹配时通过正常取表/执行器分派快速失败，不维护反射注册表或启动全表巡检。涉及业务语义（目标确实可点击、地图路线可达、输入确实释放）的部分需要项目场景回归，不能由表引用校验替代。

