import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Execute C#-generated scripts. SDK and network boundaries are controlled to test
// ordering and failure behavior; this does not measure device download performance.
const output = process.env.PRELOAD_TEST_OUTPUT || path.join(path.dirname(fileURLToPath(import.meta.url)), 'outputs');
const list = ['https://cdn.example.com/a.bundle', 'https://cdn.example.com/b.bundle'];
function boot(platform = 'TikTok') {
  const requests = [], messages = [], logs = [];
  let starts = 0, prepared, progress, now = 1000;
  const context = vm.createContext({
    GameGlobal: {}, window: {},
    tt: { request: r => requests.push(r) }, wx: { request: r => requests.push(r) },
    Date: { now: () => now },
    console: Object.fromEntries(['log', 'warn', 'error'].map(k => [k, (...args) => logs.push(args.join(' '))])),
    gameManager: {
      startGame: () => starts++, setPreloadList: value => messages.push(value),
      onModulePrepared: fn => prepared = fn, onLaunchProgress: fn => progress = fn,
    },
  });
  vm.runInContext(fs.readFileSync(path.join(output, platform, 'game.js'), 'utf8'), context);
  assert.equal(starts, 1, 'Engine starts before network completion');
  const request = requests.find(r => r.url.includes('preload.json'));
  assert(request, platform + ' must fetch the dynamic list itself');
  assert.equal(request.timeout, 5000);
  assert.equal(requests.length, 2, 'One config and one preload request');
  if (platform === 'TikTok') {
    assert.equal(vm.runInContext('managerConfig.preloadDataList.length', context), 0, 'Remove static/sparse list before SDK starts');
    const config = JSON.parse(fs.readFileSync(path.join(output, platform, 'game.json'), 'utf8'));
    assert(!('preloadDataListUrl' in config), 'Remove native URL configuration');
    assert.equal(config.deviceOrientation, 'portrait');
    assert.deepEqual(config.subpackages, [{ name: 'wasm', root: 'wasmcode' }]);
  }
  return {
    context, request, messages, logs,
    now: value => now = value,
    ready: () => { prepared(); assert.equal(context.GameGlobal.existingPrepared, true); },
    bridge: (kind = 'valid') => {
      if (kind === 'missing') return;
      context.UNBridgeCore = {
        h5HasAPI: name => kind !== 'unregistered' && name === 'setPreloadList',
        handleMsgFromUnity: json => {
          if (kind === 'throws') throw new Error('SDK dispatch failure');
          messages.push(JSON.parse(json));
        },
      };
      if (kind === 'missing-dispatch') delete context.UNBridgeCore.handleMsgFromUnity;
      if (kind === 'missing-check') delete context.UNBridgeCore.h5HasAPI;
    },
    check: status => {
      assert.equal(starts, 1);
      const summaries = logs.filter(l => l.startsWith('[Preload][TikTok] summary '));
      assert.equal(summaries.length, 1, 'Exactly one terminal summary');
      const data = JSON.parse(summaries[0].slice('[Preload][TikTok] summary '.length));
      assert.equal(data.status, status);
      return data;
    },
  };
}

let checks = 0;
for (const listFirst of [true, false]) {
  const t = boot();
  t.now(1200);
  if (listFirst) t.request.success({ statusCode: 200, data: { list } });
  else { t.bridge(); t.ready(); }
  assert.equal(t.messages.length, 0, 'Both readiness conditions are required');
  t.now(1800);
  if (listFirst) { t.bridge(); t.ready(); }
  else t.request.success({ statusCode: 200, data: { list } });
  t.ready();
  assert.equal(t.messages.length, 1, 'Repeated lifecycle callback must not resubmit');
  assert.deepEqual(t.messages[0].param, { preloadList: list });
  assert.equal(t.messages[0].target, 'setPreloadList');
  assert.equal(t.messages[0].source, 1);
  assert.equal(t.messages[0].type, 0);
  assert.equal(t.messages[0].unity_sdk_ver, 1);
  const s = t.check('submitted');
  assert.equal(s.requestMs, listFirst ? 200 : 800);
  assert.equal(s.modulePreparedMs, listFirst ? 800 : 200);
  assert.equal(s.count, 2);
  checks++;
}
for (const response of [
  { statusCode: 503, data: { list } },
  { statusCode: 200, data: {} },
  { statusCode: 200, data: { list: [] } },
  { statusCode: 200, data: { list: [null] } },
  { statusCode: 200, data: { list: ['  '] } },
  { statusCode: 200, data: { list: 'invalid' } },
]) {
  const t = boot(); t.request.success(response); t.bridge(); t.ready();
  assert.equal(t.messages.length, 0); t.check('invalidList'); checks++;
}
for (const failFirst of [true, false]) {
  const t = boot();
  if (!failFirst) { t.bridge(); t.ready(); }
  t.request.fail({ errMsg: 'request:fail timeout' });
  if (failFirst) { t.bridge(); t.ready(); }
  assert.equal(t.messages.length, 0); t.check('requestFailed'); checks++;
}
for (const kind of ['missing', 'unregistered', 'missing-dispatch', 'missing-check', 'throws']) {
  const t = boot(); t.request.success({ statusCode: 200, data: { list } });
  t.bridge(kind); t.ready(); t.ready();
  assert.equal(t.messages.length, 0);
  t.check(kind === 'throws' ? 'bridgeFailed' : 'bridgeUnavailable'); checks++;
}
for (const valid of [true, false]) {
  const t = boot('WeChat');
  t.request.success({ statusCode: 200, data: { list: valid ? list : [] } });
  t.ready(); assert.equal(t.messages.length, valid ? 1 : 0); checks++;
}
console.log(`PASS: ${checks} generated-script scenarios (parallel startup, both arrival orders, once-only submit, invalid/network/bridge failures, WeChat regression).`);
