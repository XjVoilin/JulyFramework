# July 字体制作

独立安装的 Unity 2022.3 编辑器字体裁剪工具。入口：**JulyGF → 资源管理 → 字体裁剪**。
仅依赖 Unity IMGUI、JSON 内置模块，不依赖 July UI、TMP、Localization 或 AssetCheck，不包含运行时代码。

## 使用

1. 打开面板即自动检测可用的 Python 3.10+（含 pip），显示实际解释器路径与版本，不需要手动指定。检测系统 PATH；Windows 还读取用户／系统安装登记及 Python Launcher，支持 Unity 启动后新安装的 Python。候选必须实际运行探测才会被采用，不仅按文件名判断；跳过 Windows Store 启动别名。
2. 检测成功后点击“初始化工具环境”。通过 pip 下载 `Tools~/requirements.txt` 固定版本的 fontTools wheel，安装到当前项目 `Library/July/FontAuthoring/dependencies`；不修改系统 Python 的 site-packages。首次初始化需要访问 PyPI，网络或解释器错误会明确显示。未发现可用 Python 时安装 Python 3.10+（含 pip）后点击“重新检测”。检测不自动安装 Python，也不修改 PATH。
3. 选择、拖入或输入 `.ttf`／`.otf` 字体路径，支持 Assets 内字体及工程外字体。
4. 保留默认的 GB2312 汉字、ASCII、常用标点符号和全角字符，按需粘贴额外字符或选择 UTF-8 `.txt` 字符文件。也可选择“仅使用补充字符”。
5. 点击“预检查”查看原字体缺字；点击“开始裁剪”会重新预检查当前文件，缺字时须确认是否继续，已有输出时须确认替换。

输出固定在原字体同级目录：`Example.ttf` → `Example-subset.ttf`，同时输出 `Example-subset.charset.txt`。保留源文件及轮廓格式，不执行 OTF/TTF 格式转换，不支持 TTC、WOFF 或 WOFF2。替换裁剪字体不删除其 `.meta`，保留已有 Unity GUID。Assets 内的成功输出由编辑器导入；生成成功后自动打开输出目录并选中生成字体。预检查、取消和失败不会打开目录。

## 字符与验证

- 最终保留字符为基础字符集、额外字符、字符文件的并集，按 Unicode 码点去重，支持补充平面字符。忽略控制字符及文本 BOM，保留空格、变体选择符及 ZWJ 等塑形所需字符。
- `.txt` 作为纯文本读取，不解析 JSON、Excel 或本地化表；JSON／本地化来源检查继续由 AssetCheck 负责，可复制缺字后粘贴到本窗口。
- 原字体缺失的字符单独列出，裁剪不会补出字形。确认缺字后仍保留请求字符清单，便于后续核对；清单可能包含原字体缺字。
- 验证以“请求字符与原字体 Unicode cmap 的交集”为准。裁剪后交集中的任一字符丢失即失败，临时结果不会替换已有输出。此验证不等同于所有语言塑形效果或 Unity 实机文字渲染验收。
- 每次裁剪重新计算源文件及有效字符集指纹；预检查后到裁剪前发生变化时拒绝使用旧确认。
- 两个文件在验证后逐个发布。磁盘故障、权限变化等导致发布阶段失败时明确报错；文件系统不提供两个文件共同提交的事务，失败后应核对字体与字符清单。

## 生命周期与本地状态

任务异步执行，运行期间禁用输入和重复启动，支持取消。取消等待当前处理步骤结束，在发布前检查取消标记；若取消请求到达时已完成发布，显示实际完成结果。关闭窗口或重载编辑器程序集会停止所持进程并清理本次任务的临时文件；已经完成发布的输出保留。

最近输入保存在 `Library/July/FontAuthoring/state.asset`；解释器每次打开面板自动检测，不保存手动路径。请求缓存位于同目录 `Jobs/`。这些都是本机数据，删除 Library 后需要重新初始化。依赖下载使用 pip 常规缓存与临时目录。fontTools 使用 MIT 许可证，详情见 [fontTools](https://github.com/fonttools/fonttools)。

## 命令行

```powershell
# 使用当前项目独立依赖环境，不加载系统 site-packages
python -S <包目录>/Tools~/subset_font.py check <源字体.otf> --dependencies Library/July/FontAuthoring/dependencies --extra-text "喆𠮷"
python -S <包目录>/Tools~/subset_font.py subset <源字体.otf> --dependencies Library/July/FontAuthoring/dependencies --charset-file <字符.txt>
```

默认存在缺字时失败；人工核对后可用 `--allow-missing`，确认替换已有输出时用 `--replace`。`--no-default` 只保留补充字符。输出为 JSON，失败返回非零退出码，取消返回 130。直接使用系统 fontTools 时可以不加 `-S` 和 `--dependencies`，但必须与 requirements 中版本一致。

## 验证

Python 回归：`python -B Tests~/test_subset_font.py -v`，需安装 requirements 中版本。
Unity EditMode 回归：在验证工程的 `testables` 添加本包。真实进程集成测试还需设置 `JULY_FONT_TEST_PYTHON`、`JULY_FONT_TEST_SOURCE`（含 A、中、𠮷 字符的测试 TTF），并初始化该工程的工具依赖。窗口测试需可用图形设备。
