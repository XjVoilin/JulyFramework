import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';

const root = path.resolve(process.argv[2]);
const bootstrap = fs.readFileSync(path.join(root, 'SDK~', 'bootstrap.js'), 'utf8');
const errors = [];
const context = vm.createContext({ GameGlobal: {}, console: { error: text => errors.push(text) } });
let instances = 0;
let constructorConfig;
class FakeSdk {
    constructor(config) { instances++; constructorConfig = config; }
    getInitResult() { return { inited: true }; }
    onAppStart() { throw new Error('Bootstrap must not duplicate automatic startup'); }
}
const sdkModule = new vm.SyntheticModule(['SDK'], function () { this.setExport('SDK', FakeSdk); }, { context });
const module = new vm.SourceTextModule(bootstrap, { context });
await module.link(() => sdkModule);
await module.evaluate();
assert.equal(module.namespace.initializeTencentDn({ user_action_set_id: 123, secret_key: 'test', appid: 'wx-test' }), true);
assert.equal(instances, 1);
assert.equal(constructorConfig.auto_track, true);
assert.equal(context.GameGlobal.dnSDK instanceof FakeSdk, true);
assert.throws(() => module.namespace.initializeTencentDn({}), /already exists/);
constructorConfig.on_report_fail({ secret_key: 'should-not-be-logged', openid: 'private' });
assert.equal(errors.some(value => value.includes('private') || value.includes('should-not-be-logged')), false);

const failedContext = vm.createContext({ GameGlobal: {}, console: { error: text => errors.push(text) } });
const failedSdk = new vm.SyntheticModule(['SDK'], function () {
    this.setExport('SDK', class { getInitResult() { return { inited: false, initErrMsg: 'fixture initialization failure' }; } });
}, { context: failedContext });
const failedModule = new vm.SourceTextModule(bootstrap, { context: failedContext });
await failedModule.link(() => failedSdk);
await failedModule.evaluate();
assert.equal(failedModule.namespace.initializeTencentDn({}), false);
assert.equal(failedContext.GameGlobal.dnSDK, undefined);
assert.equal(errors.some(value => value.includes('fixture initialization failure')), true);

// 真实 SDK 的公共接口验证，所有微信 API 均为本地替身，不访问网络。
const handlers = {};
const storage = new Map();
const info = { platform: 'devtools', system: 'Windows 10', SDKVersion: '3.6.1', version: '8.0.0', brand: 'fixture', model: 'fixture', language: 'zh_CN', screenWidth: 390, screenHeight: 844 };
const realErrors = [];
const quiet = { log() {}, info() {}, warn() {}, error(...args) { realErrors.push(args); }, debug() {} };
const wx = new Proxy({
    env: { USER_DATA_PATH: '/fixture' },
    createCanvas: () => ({}),
    getAccountInfoSync: () => ({ miniProgram: { appId: 'wx-test', envVersion: 'release', version: '1.0.0' } }),
    getSystemInfoSync: () => info,
    getDeviceInfo: () => info,
    getAppBaseInfo: () => info,
    getWindowInfo: () => info,
    getEnterOptionsSync: () => ({ scene: 1001, query: {} }),
    getLaunchOptionsSync: () => ({ scene: 1001, query: {} }),
    getStorageSync: key => storage.get(key),
    setStorageSync: (key, value) => storage.set(key, value),
    removeStorageSync: key => storage.delete(key),
    getLogManager: () => quiet,
    getRealtimeLogManager: () => quiet,
    request: options => options.success?.({ statusCode: 200, data: { code: 0, data: {} } }),
}, { get(target, key) {
    if (key in target) return target[key];
    if (String(key).startsWith('on')) return fn => { handlers[key] = fn; };
    if (String(key).startsWith('off')) return () => {};
    return () => ({});
} });
const realContext = vm.createContext({ wx, GameGlobal: {}, console: quiet, setInterval: () => 1, clearInterval() {}, setTimeout: () => 1, clearTimeout() {}, TextEncoder, TextDecoder, Buffer });
const realSdk = new vm.SourceTextModule(fs.readFileSync(path.join(root, 'SDK~', 'dn-sdk-minigame.js'), 'utf8'), { context: realContext });
await realSdk.link(() => { throw new Error('Unexpected vendor import'); });
await realSdk.evaluate();
const automaticActions = [];
const originalTrack = realSdk.namespace.SDK.prototype.track;
realSdk.namespace.SDK.prototype.track = function (name, parameters) {
    automaticActions.push(name);
    return originalTrack.call(this, name, parameters);
};
const realBootstrap = new vm.SourceTextModule(bootstrap, { context: realContext });
await realBootstrap.link(() => realSdk);
await realBootstrap.evaluate();
assert.equal(realBootstrap.namespace.initializeTencentDn({ user_action_set_id: 123, secret_key: 't'.repeat(32), appid: 'wx-test' }), true, JSON.stringify(realErrors));
assert.equal(automaticActions.filter(name => name === 'START_APP').length, 1, 'SDK constructor automatically tracks startup once');
assert.equal(realContext.GameGlobal.dnSDK.getInitResult().inited, true);
assert.equal(realContext.GameGlobal.dnSDK.track('TUTORIAL_FINISH', {}).code, 0);
assert.equal(realContext.GameGlobal.dnSDK.track('LOAD_FINISH', {}).code, 0);
assert.equal(realContext.GameGlobal.dnSDK.setOpenId('openid-fixture').code, 0);
const library = {};
realContext.LibraryManager = { library };
realContext.mergeInto = (target, values) => Object.assign(target, values);
realContext.UTF8ToString = value => value;
vm.runInContext(fs.readFileSync(path.join(root, 'Runtime/Plugins/TencentDn.jslib'), 'utf8'), realContext);
assert.equal(library.July_TencentDn_IsReady(), true);
assert.equal(library.July_TencentDn_Track('TUTORIAL_FINISH', '{}'), 0);
assert.equal(library.July_TencentDn_Track('LOAD_FINISH', '{}'), 0);
assert.equal(library.July_TencentDn_SetOpenId('openid-fixture'), 0);
assert.equal(library.July_TencentDn_Track('REGISTER', '{}'), 0);
console.log('PASS: SDK automatic START_APP once; bootstrap -> package jslib -> actual SDK track/OpenID (mock wx; no network).');
