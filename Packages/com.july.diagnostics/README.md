# July Diagnostics

## Editor 测试账号 UUID

在项目读取登录设备 ID 的代码中，让 `UNITY_EDITOR` 分支返回
`July.Diagnostics.EditorTestDeviceId.GetDeviceId()`。项目程序集需引用 `July.Diagnostics`。
该方法在无覆盖时返回 `SystemInfo.deviceUniqueIdentifier`。Player 继续使用项目自己的设备 ID。

从 `JulyGF/工具/测试账号 UUID` 打开窗口，可生成新 UUID、切换至指定或历史 ID、
恢复本机默认 ID，并为历史账号保存备注。退出 Play Mode 后可修改，下一次启动游戏并登录时生效。
当前覆盖值和历史存放在项目的 `Library/July/Diagnostics/test-accounts.json`。
这是本机状态，清理 Library 前可以备份该文件。首次读取时，如有本项目的旧版 July EditorPrefs
记录，工具在写入文件成功后移除这些旧键。
