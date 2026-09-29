const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const JL = require('../../Jellyfin.Plugin.JellyLinks/Client/core.js');

const f = (o) => Object.assign({ ItemId: 'x', MediaSourceId: 'x', FileName: 'a.mkv', Size: 1000, Kind: 'video',
  SeasonNumber: null, EpisodeNumber: null, Title: 'T', VersionName: null, Played: false, ItemName: null }, o);

const andor = [
  f({ ItemId: 'a1', Title: 'Andor', SeasonNumber: 1, EpisodeNumber: 1, ItemName: 'Kassa', Size: 4_000_000_000 }),
  f({ ItemId: 'a1', Title: 'Andor', SeasonNumber: 1, EpisodeNumber: 1, Kind: 'subtitle', Size: 50_000 }),
  f({ ItemId: 'b1', Title: 'Andor', SeasonNumber: 2, EpisodeNumber: 1, ItemName: 'One Year Later', Size: 4_100_000_000, Played: true }),
  f({ ItemId: 'b2', Title: 'Andor', SeasonNumber: 2, EpisodeNumber: 2, ItemName: 'Sagrona Teema', Size: 3_900_000_000 }),
];

test('sizes use French decimals and units', () => {
  assert.equal(JL.formatBytes(0), '0 o');
  assert.equal(JL.formatBytes(999), '999 o');
  assert.equal(JL.formatBytes(1500), '1,5 Ko');
  assert.equal(JL.formatBytes(96_400_000_000), '96,4 Go');
});

test('dates are short French dates', () => {
  assert.equal(JL.formatDate(1791288000), '6 oct. 2026');
});

test('files group into seasons of units, subtitles folded into their video', () => {
  const g = JL.buildTree(andor);
  assert.deepEqual(g.map((x) => x.label), ['Saison 1', 'Saison 2']);
  const u = g[0].units[0];
  assert.equal(u.bytes, 4_000_050_000);
  assert.equal(u.files, 2);
  assert.equal(u.subs, 1);
  assert.equal(JL.unitLabel(u), 'E01 — Kassa');
  assert.equal(JL.headline(g), 'Andor · 2 saisons · 3 épisodes');
});

test('seasons of two shows keep the show name', () => {
  const g = JL.buildTree([
    f({ ItemId: 'a', Title: 'Andor', SeasonNumber: 1, EpisodeNumber: 1 }),
    f({ ItemId: 'b', Title: 'Arcane', SeasonNumber: 1, EpisodeNumber: 1 }),
  ]);
  assert.deepEqual(g.map((x) => x.label), ['Andor — Saison 1', 'Arcane — Saison 1']);
  assert.equal(JL.headline(g), '2 titres · 2 saisons · 2 épisodes');
});

test('a movie with two versions is one group with one line per version', () => {
  const g = JL.buildTree([
    f({ ItemId: 'v1', Title: 'Dune', VersionName: '4K' }),
    f({ ItemId: 'v2', Title: 'Dune', VersionName: '1080p' }),
  ]);
  assert.equal(g.length, 1);
  assert.equal(g[0].label, 'Dune');
  assert.deepEqual(g[0].units.map(JL.unitLabel), ['Dune — 4K', 'Dune — 1080p']);
  assert.equal(JL.headline(g), 'Dune');
});

test('season checkbox has three states', () => {
  const g = JL.buildTree(andor);
  const ex = new Set();
  const s2 = g[1];
  assert.equal(JL.groupState(s2, ex), 'all');
  JL.toggleUnit('b1', ex);
  assert.equal(JL.groupState(s2, ex), 'some');
  JL.toggleGroup(s2, ex);
  assert.equal(JL.groupState(s2, ex), 'all');
  JL.toggleGroup(s2, ex);
  assert.equal(JL.groupState(s2, ex), 'none');
  assert.equal(JL.groupState(g[0], ex), 'all');
});

test('shortcuts: all, none, unwatched only', () => {
  const g = JL.buildTree(andor);
  const ex = new Set();
  JL.selectNone(g, ex);
  assert.equal(JL.totals(g, ex).units, 0);
  JL.selectAll(ex);
  assert.equal(JL.totals(g, ex).units, 3);
  JL.unwatchedOnly(g, ex);
  assert.deepEqual([...ex], ['b1']);
});

test('totals count units, files and bytes of what is ticked', () => {
  const g = JL.buildTree(andor);
  const ex = new Set(['b2']);
  assert.deepEqual(JL.totals(g, ex), { units: 2, files: 3, bytes: 8_100_050_000 });
});

test('links text is one URL per line and skips dead links', () => {
  assert.equal(JL.linksText({ Links: [{ Url: 'https://h.example/1' }, { Url: '' }, { Url: 'https://h.example/2' }] }),
    'https://h.example/1\nhttps://h.example/2\n');
  assert.equal(JL.linksText({ Links: [{ Url: '' }] }), '');
});

test('txt file name is the label head without forbidden characters', () => {
  assert.equal(JL.txtName('Andor — Saison 2 · 12 fichiers · 48,0 Go'), 'Andor — Saison 2.txt');
  assert.equal(JL.txtName('A/B: C? · 1 fichier · 1,0 Go'), 'A-B- C-.txt');
  assert.equal(JL.labelTitle('<b>Dune</b> & co · 1 fichier · 1,0 Go'), '<b>Dune</b> & co');
});

test('batch cards: active, done, expired, blocked, revoked', () => {
  const b = (o) => Object.assign({ Label: 'X · 9 fichiers · 1,0 Go', State: 'active', FileCount: 9, CompleteCount: 2, TotalBytes: 22_400_000_000, ExpiresAt: 1791288000 }, o);
  let v = JL.batchView(b());
  assert.equal(v.status, '2/9 terminés'); assert.equal(v.tone, 'warn'); assert.deepEqual(v.actions, ['copy', 'revoke']);
  assert.equal(v.expires, 'expire le 6 oct. 2026'); assert.equal(v.size, '9 fichiers · 22,4 Go'); assert.equal(v.pct, 22);
  v = JL.batchView(b({ CompleteCount: 9 }));
  assert.equal(v.status, 'terminé'); assert.equal(v.tone, 'ok');
  v = JL.batchView(b({ State: 'expired' }));
  assert.equal(v.status, 'abandonné 2/9'); assert.equal(v.tone, 'off'); assert.deepEqual(v.actions, ['regenerate']); assert.equal(v.expires, 'expiré');
  v = JL.batchView(b({ State: 'blocked' }));
  assert.equal(v.status, 'bloqué'); assert.equal(v.note, "contacte l'administrateur"); assert.deepEqual(v.actions, []);
  v = JL.batchView(b({ State: 'revoked' }));
  assert.equal(v.status, 'révoqué'); assert.deepEqual(v.actions, []);
});

test('quota line and remaining quota', () => {
  assert.equal(JL.quotaLine({ Enabled: false }), null);
  assert.equal(JL.quotaRemaining({ Enabled: false }), '');
  const q = { Enabled: true, UsedBytes: 88e9, VolumeBytes: 500e9, PeriodDays: 7, ActiveBatches: 1, MaxActiveBatches: 0 };
  assert.deepEqual(JL.quotaLine(q), { text: 'Quota : 88,0 Go utilisés sur 500,0 Go · période glissante de 7 jours', pct: 18 });
  assert.equal(JL.quotaRemaining(q), 'quota restant 412,0 Go');
  assert.equal(JL.quotaLine(Object.assign({}, q, { VolumeBytes: 0, MaxActiveBatches: 3 })).text,
    'Quota : 88,0 Go utilisés · période glissante de 7 jours · lots actifs 1/3');
  assert.equal(JL.quotaRemaining(Object.assign({}, q, { UsedBytes: 600e9 })), 'quota restant 0 o');
});

test('error messages', () => {
  assert.match(JL.errorMessage(429), /^Quota atteint/);
  assert.match(JL.errorMessage(400), /^Rien à lier/);
  assert.equal(JL.errorMessage(500), 'Erreur du serveur (500).');
  assert.equal(JL.errorMessage(0), 'Serveur injoignable.');
});

test('client scripts never use innerHTML', () => {
  const dir = path.join(__dirname, '../../Jellyfin.Plugin.JellyLinks/Client');
  for (const name of fs.readdirSync(dir).filter((n) => n.endsWith('.js'))) {
    assert.doesNotMatch(fs.readFileSync(path.join(dir, name), 'utf8'), /innerHTML|outerHTML|insertAdjacentHTML/, name);
  }
});
