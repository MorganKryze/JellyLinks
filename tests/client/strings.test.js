const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '../../Jellyfin.Plugin.JellyLinks');
const S = JSON.parse(fs.readFileSync(path.join(root, 'Client/strings.json'), 'utf8'));
const placeholders = (v) => [...JSON.stringify(v).matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',');

test('English and French have the same keys, shapes and placeholders', () => {
  assert.deepEqual(Object.keys(S.fr).sort(), Object.keys(S.en).sort());
  for (const k of Object.keys(S.en)) {
    const en = S.en[k], fr = S.fr[k];
    assert.equal(Array.isArray(fr), Array.isArray(en), k);
    assert.equal(typeof fr, typeof en, k);
    if (en && typeof en === 'object' && !Array.isArray(en)) {
      assert.deepEqual(Object.keys(fr).sort(), ['one', 'other'], k);
      assert.deepEqual(Object.keys(en).sort(), ['one', 'other'], k);
    }
    assert.equal(placeholders(fr), placeholders(en), k);
  }
});

test('every key the client and the admin page name exists', () => {
  const files = fs.readdirSync(path.join(root, 'Client')).filter((n) => n.endsWith('.js')).map((n) => path.join(root, 'Client', n))
    .concat([path.join(root, 'Configuration/configPage.html')]);
  const used = new Set();
  for (const f of files) {
    const src = fs.readFileSync(f, 'utf8');
    for (const m of src.matchAll(/\bt\('([\w.]+)'\s*[,)]/g)) { used.add(m[1]); } // 'kind.' + k is built at run time: not checked here
    for (const m of src.matchAll(/data-t[pa]?="([\w.]+)"/g)) { used.add(m[1]); }
  }
  for (const k of used) { assert.ok(k in S.en, k); }
});
