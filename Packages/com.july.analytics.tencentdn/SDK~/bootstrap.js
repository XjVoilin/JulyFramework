import { SDK } from './dn-sdk-minigame.js';

// SDK 创建和自动采集归 JS 初始化所有，C# 只发送后续业务事件。
export function initializeTencentDn(config) {
    if (GameGlobal.dnSDK) {
        throw new Error('[TencentDn] SDK instance already exists. Initialization must have one owner.');
    }
    try {
        const sdk = new SDK(Object.assign({}, config, {
            // SDK 构造时通过内置自动采集触发 START_APP，无需再调用 onAppStart 或 track('START_APP')。
            auto_track: true,
            on_report_fail() {
                // SDK 自己处理重试；不输出可能包含配置及用户标识的失败参数。
                console.error('[TencentDn] SDK report failed.');
            },
        }));
        const result = sdk.getInitResult();
        if (!result.inited) throw new Error(result.initErrMsg);
        GameGlobal.dnSDK = sdk;
        return true;
    } catch (error) {
        // 第三方初始化失败不阻断游戏启动，C# 会检测不可用并明确停用本次发送。
        console.error('[TencentDn] SDK initialization failed: ' + error.message);
        return false;
    }
}
