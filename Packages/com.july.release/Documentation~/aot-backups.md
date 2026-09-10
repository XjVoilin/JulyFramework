# AOT 备份接口（0.1.4）

调用方只选择持久备份目录，框架负责 AOT 生成、保存、恢复和校验。无需模式字段。工作区备份仍是必要构建产物，不能删除生成步骤。

## 命令行

在现有 Unity 命令中使用以下参数（保留既有平台宏预处理、projectPath、buildTarget、日志等参数）：

```text
-executeMethod July.Release.Editor.BuildPipelineCI.FullBuild -platform WeChat -env Dev -planVersion 1.6.0 -miniGame -aotBackupOutputPath "D:/BuildFarm/GooseMarket/ReleaseSources/WeChat/1.6.0/2/aot"

-executeMethod July.Release.Editor.BuildPipelineCI.HotUpdateBuild -platform WeChat -env Dev -planVersion 1.6.1 -aotBackupVersion 1.6.0 -aotBackupInputPath "D:/BuildFarm/GooseMarket/ReleaseSources/WeChat/1.6.0/2/aot"
```

路径必须是绝对目录，支持带空格的引号参数；直接使用目录本身，不追加项目、平台、版本或构建号。不得同时传两个路径、重复传入同一路径开关、缺少值，或用于错误入口（包括 RunStep / SyncPlatformDefines）。路径不能与框架 AOT 工作备份根目录相同或互为父子，不能使用符号链接或目录联接。

输出目录不能预先创建，父目录可以存在。即使 `-forceRebuild` 已打开，也不能覆盖现有持久备份；重新全量构建必须使用新的备份目录。

输入目录唯一确定热更来源，清单识别 CoreVersion / AOTBackupVersion。可选 `-aotBackupVersion` 是一致性断言，不一致立即失败。平台及 BuildTarget 和本次请求必须一致；PlanVersion 继续独立递增。不从目录名推断版本，不自动选择、不使用本地归档或已有工作副本兜底、不生成新 AOT 掩盖缺失。恢复前校验源文件，恢复时在临时目录校验复制结果，然后替换整个工作副本以清除残留；编译前复核同一份清单和工作副本。源码 hash 比较与 HybridCLR 元数据检测继续执行。

## 备份格式

```text
aot/
  aot-backup.json
  aot-source.hash
  AOTGenericReferences.cs
  mscorlib.dll
  System.dll
  ...其他 AOT DLL 及工作备份中的文件/子目录
```

`aot-backup.json` 是 UTF-8 JSON，格式版本为 1：

```json
{
  "formatVersion": 1,
  "platform": "WeChat",
  "buildTarget": "WebGL",
  "coreVersion": "1.6.0",
  "requiredAotAssemblies": ["System.dll", "mscorlib.dll"],
  "files": [
    {"path": "mscorlib.dll", "size": 12345, "sha256": "64位小写SHA-256"}
  ]
}
```

以上 files 仅展示字段，真实清单列出除清单自身外的全部文件，包括泛型引用文件、源码 hash 及所有必需 DLL。路径相对于备份目录，使用 `/`；拒绝绝对路径、越界路径、重复文件和链接。文件集合必须完全相等，缺失或额外文件均失败；逐文件核对字节数和 SHA-256。必需程序集取生成的 AOTGenericReferences 程序集列表与本项目补充程序集的并集，必需 DLL 不能为空。引用列表缺失或为空、源码 hash 缺失或不合法均失败，不能使用旧格式的放行或 SDK 程序集列表回退。

输出先复制到最终目录的同级临时目录 `aot.aot-tmp-<GUID>`，校验后最后写清单，再通过目录 rename 发布到最终路径；失败清理本次临时目录。进程被强制终止可能留下临时目录，它不是成功输出，不应被调用方选择。没有清单的目录不构成有效备份。清单 hash 在选中后固定，防止同次构建的后续步骤悄悄更换基线。这是文件完整性校验，不是数字签名。

## 成功判定及调用方职责

最终目录发布完成后框架输出一行：

```text
[AOTBackup] SAVED path="<绝对路径>" platform=WeChat buildTarget=WebGL coreVersion=1.6.0 files=<数量> manifestSha256=<清单SHA-256>
```

调用方应核对本次路径和版本的 SAVED 日志、最终目录及 `aot-backup.json` 存在，并且等待整个 Unity 构建退出码为 0，才能将完整构建标记成功。日志中的 manifestSha256 可与清单文件 SHA-256 核对。仅目录存在或仅 AOT 保存成功不代表后续出包成功；后续失败时保留备份，框架不自动删除持久数据。后续热更由框架再次完整校验，不需要调用方复刻校验器。

调用方应移除自行复制工作区 AOT 到持久目录、自行恢复工作区 AOT、操作或猜测 HybridCLRData 内部布局、指定输入失败时回退“最新备份”及缺失时生成 AOT 等逻辑。仍可负责持久目录的选择、原包保存及整体构建状态记录。无需读取或写入框架工作区目录。

无路径参数时，继续生成工作备份并使用 BuildConfig 的本地归档父目录；已有工作副本优先、缺失时从所选版本本地归档恢复。旧格式备份只在这个本地流程保留兼容。本接口不接收旧的外部复制快照，应重新全量生成新格式。

## 验证

`Tests/Editor/AotBackupTests.cs` 提供独立临时目录的 NUnit 文件系统测试，覆盖解析和空格路径、错误入口、重复/冲突/缺值、单份持久归档、拒绝覆盖、路径重叠、I/O 失败、完整性及身份校验、清除旧工作副本、禁止回退、基线固定和本地行为兼容。`ReleaseContractTests` 同时检查全量/热更步骤顺序保持不变。

真实生成 AOT、完整全量出包后再热更，以及 CI 退出和 Source 状态联调，需要在调用方接入并发布包后验证。测试不会触发上传、通知或操作真实备份。
