import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
const root = path.resolve(process.argv[2]);
const bridgeSource = fs.readFileSync(path.join(root, 'Runtime/Plugins/TencentDn.jslib'), 'utf8');
const errors = [];
const library = {};
const calls = [];
const bridgeContext = vm.createContext({
    GameGlobal: { dnSDK: {
        getInitResult: () => ({ inited: true }),
        track: (name, parameters) => { calls.push({ name, parameters }); return { code: name === 'reject' ? 101 : 0 }; },
        setOpenId: id => { calls.push(id); return { code: 0 }; },
    } },
    console: { error: text => errors.push(text) },
    UTF8ToString: value => value,
    LibraryManager: { library },
    mergeInto: (target, values) => Object.assign(target, values),
});
vm.runInContext(bridgeSource, bridgeContext);
assert.equal(library.July_TencentDn_IsReady(), true);
assert.equal(library.July_TencentDn_Track('LEVEL_LOSE', JSON.stringify({ level_id: 1, ad_cnt: 0, items: ['{item_type:4,item_num:3}'] })), 0);
assert.equal(calls[0].name, 'LEVEL_LOSE');
assert.equal(calls[0].parameters.ad_cnt, 0);
assert.equal(Array.isArray(calls[0].parameters.items), true);
assert.equal(calls[0].parameters.items[0], '{item_type:4,item_num:3}');
assert.equal(library.July_TencentDn_Track('reject', '{}'), 101);
assert.equal(library.July_TencentDn_SetOpenId('openid-fixture'), 0);
assert.equal(calls[2], 'openid-fixture');
assert.throws(() => library.July_TencentDn_Track('bad-json', '{'), error => error.name === 'SyntaxError');
delete bridgeContext.GameGlobal.dnSDK;
assert.equal(library.July_TencentDn_IsReady(), false);
assert.equal(library.July_TencentDn_Track('LOAD_FINISH', '{}'), 100);
assert.equal(library.July_TencentDn_SetOpenId('openid-fixture'), 100);


const nativeSource = fs.readFileSync(path.join(root, 'Runtime/TencentDnAnalytics.cs'), 'utf8');
const nativeSymbols = Array.from(nativeSource.matchAll(/EntryPoint = "([^"]+)"/g), match => match[1]).sort();
assert.deepEqual(nativeSymbols, Object.keys(library).sort());
assert.equal(nativeSource.includes('GooseMarket'), false);
const meta = fs.readFileSync(path.join(root, 'Runtime/Plugins/TencentDn.jslib.meta'), 'utf8');
assert.match(meta, /defineConstraints:\s*- JULYGF_WX_MINIGAME/);
console.log('PASS: package jslib/native symbols, track/OpenID/error codes.');
