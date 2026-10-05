const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const JL = require('../../Jellyfin.Plugin.JellyLinks/Client/core.js');

JL.STRINGS = JSON.parse(fs.readFileSync(path.join(__dirname, '../../Jellyfin.Plugin.JellyLinks/Client/strings.json'), 'utf8'));
const fr = (fn) => { JL.setLang('fr'); try { fn(); } finally { JL.setLang(null); } };
const en = (fn) => { JL.setLang('en'); try { fn(); } finally { JL.setLang(null); } };

const f = (o) => Object.assign({ ItemId: 'x', MediaSourceId: 'x', FileName: 'a.mkv', Size: 1000, Kind: 'video',
  SeasonNumber: null, EpisodeNumber: null, Title: 'T', VersionName: null, Played: false, ItemName: null }, o);

const andor = [
  f({ ItemId: 'a1', Title: 'Andor', SeasonNumber: 1, EpisodeNumber: 1, ItemName: 'Kassa', Size: 4_000_000_000 }),
  f({ ItemId: 'a1', Title: 'Andor', SeasonNumber: 1, EpisodeNumber: 1, Kind: 'subtitle', Size: 50_000 }),
  f({ ItemId: 'b1', Title: 'Andor', SeasonNumber: 2, EpisodeNumber: 1, ItemName: 'One Year Later', Size: 4_100_000_000, Played: true }),
  f({ ItemId: 'b2', Title: 'Andor', SeasonNumber: 2, EpisodeNumber: 2, ItemName: 'Sagrona Teema', Size: 3_900_000_000 }),
];

test('sizes follow the language', () => {
  en(() => {
    assert.equal(JL.formatBytes(999), '999 B');
    assert.equal(JL.formatBytes(1500), '1.5 KB');
  });
  fr(() => {
    assert.equal(JL.formatBytes(0), '0 o');
    assert.equal(JL.formatBytes(1500), '1,5 Ko');
    assert.equal(JL.formatBytes(96_400_000_000), '96,4 Go');
  });
});

test('dates follow the language', () => {
  en(() => { assert.equal(JL.formatDate(1791288000), 'Oct 6, 2026'); assert.equal(JL.formatDay(1791288000), 'Oct 6'); });
  fr(() => { assert.equal(JL.formatDate(1791288000), '6 oct. 2026'); assert.equal(JL.formatDay(1791288000), '6 oct.'); });
});

test('other languages fall back to English', () => {
  JL.setLang('de-de');
  try {
    assert.equal(JL.lang(), 'en');
    assert.equal(JL.t('gen.title'), 'Download links');
    assert.match(JL.formatBytes(1500), / KB$/);
  } finally { JL.setLang(null); }
});

test('plurals: French counts 0 and 1 as one, a missing key shows itself', () => {
  fr(() => { assert.equal(JL.t('ev.created', { n: 0 }), '0 fichier'); assert.equal(JL.t('ev.created', { n: 2 }), '2 fichiers'); });
  en(() => { assert.equal(JL.t('ev.created', { n: 0 }), '0 files'); assert.equal(JL.t('ev.created', { n: 1 }), '1 file'); });
  assert.equal(JL.t('nope.nothing'), 'nope.nothing');
});

test('files group into seasons of units, subtitles folded into their video', () => {
  fr(() => {
    const g = JL.buildTree(andor);
    assert.deepEqual(g.map((x) => x.label), ['Saison 1', 'Saison 2']);
    const u = g[0].units[0];
    assert.equal(u.bytes, 4_000_050_000);
    assert.equal(u.files, 2);
    assert.equal(u.subs, 1);
    assert.equal(JL.unitLabel(u), 'E01 — Kassa');
    assert.equal(JL.headline(g), 'Andor · 2 saisons · 3 épisodes');
  });
  en(() => assert.equal(JL.headline(JL.buildTree(andor)), 'Andor · 2 seasons · 3 episodes'));
});

test('seasons of two shows keep the show name; specials have their name', () => {
  en(() => {
    const g = JL.buildTree([
      f({ ItemId: 'a', Title: 'Andor', SeasonNumber: 1, EpisodeNumber: 1 }),
      f({ ItemId: 'b', Title: 'Arcane', SeasonNumber: 0, EpisodeNumber: 1 }),
    ]);
    assert.deepEqual(g.map((x) => x.label), ['Andor · Season 1', 'Arcane · Specials']);
    assert.equal(JL.headline(g), '2 titles · 2 seasons · 2 episodes');
  });
});

test('a movie with two versions is one group with one line per version', () => {
  const g = JL.buildTree([f({ ItemId: 'v1', Title: 'Dune', VersionName: '4K' }), f({ ItemId: 'v2', Title: 'Dune', VersionName: '1080p' })]);
  assert.equal(g.length, 1);
  assert.equal(g[0].label, 'Dune');
  assert.deepEqual(g[0].units.map(JL.unitLabel), ['Dune — 4K', 'Dune — 1080p']);
});

test('a movie with a single version is labelled by its title alone', () => {
  const g = JL.buildTree([f({ ItemId: 'v1', Title: 'Sample Film', VersionName: 'Sample Film (2020)' })]);
  assert.deepEqual(g[0].units.map(JL.unitLabel), ['Sample Film']);
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
  assert.deepEqual(JL.totals(JL.buildTree(andor), new Set(['b2'])), { units: 2, files: 3, bytes: 8_100_050_000 });
});

test('links text is one URL per line and skips dead links', () => {
  assert.equal(JL.linksText({ Links: [{ Url: 'https://h.example/1' }, { Url: '' }, { Url: 'https://h.example/2' }] }),
    'https://h.example/1\nhttps://h.example/2\n');
  assert.equal(JL.linksText({ Links: [{ Url: '' }] }), '');
});

test('scope titles mirror the server', () => {
  const show = (seasons) => ({ Titles: [{ Name: 'Andor', Seasons: seasons }] });
  en(() => {
    assert.equal(JL.scopeTitle(show([{ Number: 2, Episodes: [1, 2, 3] }])), 'Andor · Season 2 · E01–E03');
    assert.equal(JL.scopeTitle(show([{ Number: 1, Episodes: [1] }, { Number: 2, Episodes: [1] }])), 'Andor · 2 seasons');
    assert.equal(JL.scopeTitle(show([{ Number: 1, Episodes: [1, 3, 5] }])), 'Andor · Season 1 · 3 episodes');
    assert.equal(JL.scopeTitle({ Titles: [{ Name: 'A', Seasons: [] }, { Name: 'B', Seasons: [] }, { Name: 'C', Seasons: [] }, { Name: 'D', Seasons: [] }] }), 'A, B +2');
  });
  fr(() => assert.equal(JL.scopeTitle(show([{ Number: 0, Episodes: [4] }])), 'Andor · Spéciaux · E04'));
});

test('legacy batch keeps its stored title', () => {
  assert.equal(JL.batchTitle({ Label: 'Andor — Saison 2', Scope: null }), 'Andor — Saison 2');
  fr(() => assert.equal(JL.batchTitle({ Label: 'x', Scope: { Titles: [{ Name: 'Dune', Seasons: [] }] } }), 'Dune'));
});

test('txt file name comes from the title, sanitised', () => {
  assert.equal(JL.txtName('Dune: Part Two'), 'Dune- Part Two.txt');
  assert.equal(JL.txtName('A/B: C?'), 'A-B- C-.txt');
});

test('batch cards: active, done, expired, blocked, revoked', () => {
  const b = (o) => Object.assign({ Label: 'X', Scope: null, State: 'active', FileCount: 9, CompleteCount: 2, TotalBytes: 22_400_000_000, CreatedAt: 1791288000, ExpiresAt: 1791892800 }, o);
  fr(() => {
    let v = JL.batchView(b());
    assert.equal(v.status, '2 fichiers sur 9 téléchargés'); assert.equal(v.tone, ''); assert.deepEqual(v.actions, ['copy', 'revoke']);
    assert.equal(v.section, 'active'); assert.equal(v.meta, 'Créé le 6 oct. · expire le 13 oct.'); assert.equal(v.size, '9 fichiers · 22,4 Go'); assert.equal(v.pct, 22);
    v = JL.batchView(b({ CompleteCount: 9 }));
    assert.equal(v.status, 'Téléchargé'); assert.equal(v.tone, 'ok'); assert.equal(v.icon, 'check_circle');
    v = JL.batchView(b({ State: 'expired' }));
    assert.equal(v.status, 'Expiré · 2 fichiers sur 9 téléchargés'); assert.equal(v.tone, 'off'); assert.deepEqual(v.actions, ['regenerate']); assert.equal(v.section, 'history');
    v = JL.batchView(b({ State: 'blocked' }));
    assert.equal(v.status, 'Bloqué : contactez votre administrateur'); assert.equal(v.icon, 'block'); assert.deepEqual(v.actions, []);
    v = JL.batchView(b({ State: 'revoked' }));
    assert.equal(v.status, 'Révoqué'); assert.equal(v.icon, 'link_off'); assert.deepEqual(v.actions, []);
  });
  en(() => assert.equal(JL.batchView(b({ CompleteCount: 1 })).status, '1 of 9 downloaded'));
});

test('a file of a revoked or expired batch never reads as pending', () => {
  en(() => {
    assert.equal(JL.fileStatus({ Status: 'pending' }, 'active'), 'Not started');
    assert.equal(JL.fileStatus({ Status: 'pending' }, 'revoked'), 'Not downloaded');
    assert.equal(JL.fileStatus({ Status: 'complete' }, 'expired'), 'Downloaded');
  });
});

test('quota line, warning past 80 %, remaining quota', () => {
  assert.equal(JL.quotaLine({ Enabled: false }), null);
  assert.equal(JL.quotaRemaining({ Enabled: false }), '');
  const q = { Enabled: true, UsedBytes: 88e9, VolumeBytes: 500e9, PeriodDays: 7, ActiveBatches: 1, MaxActiveBatches: 0 };
  fr(() => {
    assert.deepEqual(JL.quotaLine(q), { text: 'Quota · 88,0 Go utilisés sur 500,0 Go · sur 7 jours glissants', pct: 18, warn: false });
    assert.equal(JL.quotaRemaining(q), 'Quota restant : 412,0 Go');
    assert.equal(JL.quotaLine(Object.assign({}, q, { VolumeBytes: 0, MaxActiveBatches: 3 })).text, 'Quota · 88,0 Go utilisés · sur 7 jours glissants · 1 actif sur 3');
  });
  assert.equal(JL.quotaLine(Object.assign({}, q, { UsedBytes: 450e9 })).warn, true);
});

test('errors say what happened and what to do', () => {
  en(() => {
    const quota = { status: 429, body: { Enabled: true, UsedBytes: 5e9, VolumeBytes: 5e9, PeriodDays: 7, ActiveBatches: 0, MaxActiveBatches: 0, FreesAt: 1791288000 } };
    assert.equal(JL.errorText(quota), 'Quota reached: 5.0 GB of 5.0 GB over 7 days. Space frees up on Oct 6, 2026.');
    assert.match(JL.errorText({ status: 429, body: { Enabled: true, UsedBytes: 0, VolumeBytes: 0, PeriodDays: 7, ActiveBatches: 3, MaxActiveBatches: 3 } }), /^You can keep 3 active downloads/);
    assert.equal(JL.errorText({ status: 400, body: { Code: 'empty' } }), 'Select at least one item.');
    assert.equal(JL.errorText({ status: 403, body: { Code: 'not_allowed', Args: {} } }), 'Your account is not allowed to download. Ask your administrator.');
    assert.equal(JL.errorText({ status: 401, body: null }), 'Your session has expired. Sign in again, then retry.');
    assert.equal(JL.errorText({ status: 0, body: null }), 'The server did not answer. Try again in a moment.');
    assert.equal(JL.errorText(500), 'The server could not complete this request. Try again in a moment.');
  });
});

test('coded messages render in the reader language', () => {
  fr(() => {
    assert.equal(JL.msgText({ Code: 'new_ip', Args: { ip: '1.2.3.4', n: '2', limit: '0' } }, 'ev.'), 'adresse 1.2.3.4 (2/∞)');
    assert.equal(JL.msgText({ Code: 'text', Args: { text: 'ancien détail' } }, 'ev.'), 'ancien détail');
    assert.equal(JL.msgText({ Code: 'session', Args: { file: 'a.mkv', ip: '1.2.3.4', ua: 'Mozilla/5.0 (Windows NT 10.0) Chrome/130.0 Safari/537.36', coveredBytes: '1500', sizeBytes: '3000', status: 'complete' } }, 'ev.'),
      'a.mkv · 1.2.3.4 · Chrome · Windows · 1,5 Ko / 3,0 Ko · Terminé');
  });
  assert.equal(JL.msgText(null, 'ev.'), '');
});

test('user agents are shortened to something a person recognises', () => {
  assert.equal(JL.shortUa('Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/605.1.15 Version/17.0 Safari/605.1.15'), 'Safari · macOS');
  assert.equal(JL.shortUa('Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0'), 'Firefox · Linux');
  assert.equal(JL.shortUa('Mozilla/5.0 (Linux; Android 14) Chrome/130.0 Mobile Safari/537.36 EdgA/130'), 'Chrome · Android');
  assert.equal(JL.shortUa('JDownloader/2 (Java)'), 'JDownloader');
  assert.equal(JL.shortUa('curl/8.4.0'), 'curl');
  assert.equal(JL.shortUa(''), '—');
});

test('transparent colours are detected', () => {
  for (const c of ['', 'transparent', 'rgba(0, 0, 0, 0)', 'rgba(255, 255, 255, 0)']) { assert.equal(JL.isTransparent(c), true, c); }
  for (const c of ['rgb(0, 164, 220)', 'rgba(255, 255, 255, 0.8)']) { assert.equal(JL.isTransparent(c), false, c); }
});

test('chart palette avoids the status hues and keeps colours stable', () => {
  const hue = (hex) => {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
    const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
    const h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return (h * 60 + 360) % 360;
  };
  assert.equal(JL.PALETTE.length, 6);
  for (const c of JL.PALETTE) {
    const h = hue(c);
    assert.ok(!(h >= 345 || h <= 15), c + ' reads as red');
    assert.ok(!(h >= 25 && h <= 65), c + ' reads as amber');
    assert.ok(!(h >= 80 && h <= 160), c + ' reads as green');
  }
  const totals = { b: 10, a: 30, c: 5, d: 1, e: 1, f: 1, g: 0.5 };
  const colors = JL.userColors(totals);
  assert.equal(colors.g, null);
  assert.equal(colors.a, JL.PALETTE[0]);
  assert.deepEqual(JL.userColors(Object.assign({}, totals, { a: 1 })).a, JL.PALETTE[0]);
});

test('client scripts never use innerHTML', () => {
  const dir = path.join(__dirname, '../../Jellyfin.Plugin.JellyLinks/Client');
  for (const name of fs.readdirSync(dir).filter((n) => n.endsWith('.js'))) {
    assert.doesNotMatch(fs.readFileSync(path.join(dir, name), 'utf8'), /innerHTML|outerHTML|insertAdjacentHTML/, name);
  }
});

test('a menu entry opens once the menu has stepped back in history', async () => {
  const win = new EventTarget();
  let opened = 0;
  JL.afterSheetClose(win, () => opened++, 50);
  assert.equal(opened, 0);
  win.dispatchEvent(new Event('popstate'));
  assert.equal(opened, 1);
  await new Promise((r) => setTimeout(r, 80));
  assert.equal(opened, 1);
});

test('a menu entry still opens when the menu never steps back', async () => {
  const win = new EventTarget();
  let opened = 0;
  JL.afterSheetClose(win, () => opened++, 20);
  await new Promise((r) => setTimeout(r, 50));
  assert.equal(opened, 1);
  win.dispatchEvent(new Event('popstate'));
  assert.equal(opened, 1);
});
