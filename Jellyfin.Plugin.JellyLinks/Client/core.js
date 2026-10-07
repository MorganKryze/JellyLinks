// JellyLinks client: core — language, formats, selection model, batch cards, theme, native components, API and dialogs.
(function () {
  'use strict';
  var JL = typeof window !== 'undefined' ? (window.JellyLinks = window.JellyLinks || {}) : {};

  // ---- language --------------------------------------------------------------

  var forced = null;

  /** The language tag Jellyfin shows ("fr-fr", "en-us"…): it sets <html lang> from the user's display setting. */
  function tag() { return forced || (typeof document !== 'undefined' && document.documentElement.lang) || 'en'; }

  /** 'fr' when Jellyfin shows French, 'en' for anything else: the two languages JellyLinks ships. */
  JL.lang = function () { return /^fr/i.test(tag()) ? 'fr' : 'en'; };

  JL.setLang = function (t) { forced = t; };

  JL.raw = function (key) {
    var S = JL.STRINGS || {}, mine = S[JL.lang()] || {}, en = S.en || {};
    return mine[key] != null ? mine[key] : en[key];
  };

  /** A text of the dictionary: {name} replaced; a plural entry chosen by args.n (French: 0 and 1 are singular). */
  JL.t = function (key, args) {
    var v = JL.raw(key);
    if (v == null) { return key; }
    if (typeof v === 'object' && !Array.isArray(v)) {
      var n = Number(args && args.n) || 0;
      v = (JL.lang() === 'fr' ? n === 0 || n === 1 : n === 1) ? v.one : v.other;
    }
    return String(v).replace(/\{(\w+)\}/g, function (m, k) { return args && args[k] != null ? String(args[k]) : m; });
  };

  // ---- formats ---------------------------------------------------------------

  /** The locale for numbers and dates: Jellyfin's regional variant of a shipped language ("en-gb"), else that language alone. */
  function locale() {
    var l = JL.lang(), t = String(tag()).replace(/_/g, '-');
    return t.slice(0, 2).toLowerCase() === l ? t : l;
  }

  /** Runs an Intl formatting with the locale; a malformed tag falls back to English instead of throwing. */
  function intl(fn) {
    try { return fn(locale()); } catch (e) { return fn('en'); }
  }

  JL.formatBytes = function (n) {
    var units = JL.raw('units') || ['B', 'KB', 'MB', 'GB', 'TB'], i = 0;
    while (n >= 1000 && i < units.length - 1) { n /= 1000; i++; }
    return (i === 0 ? String(n) : intl(function (loc) { return n.toLocaleString(loc, { minimumFractionDigits: 1, maximumFractionDigits: 1 }); })) + ' ' + units[i];
  };

  JL.formatDate = function (unix) {
    return intl(function (loc) { return new Date(unix * 1000).toLocaleDateString(loc, { day: 'numeric', month: 'short', year: 'numeric' }); });
  };

  JL.formatDay = function (unix) {
    return intl(function (loc) { return new Date(unix * 1000).toLocaleDateString(loc, { day: 'numeric', month: 'short' }); });
  };

  JL.formatWhen = function (unix) {
    return intl(function (loc) { return new Date(unix * 1000).toLocaleString(loc, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); });
  };

  JL.formatDuration = function (s) {
    if (s < 60) { return s + ' s'; }
    if (s < 3600) { return Math.round(s / 60) + ' min'; }
    return intl(function (loc) { return (s / 3600).toLocaleString(loc, { maximumFractionDigits: 1 }); }) + ' h';
  };

  function pad2(n) { return n < 10 ? '0' + n : String(n); }

  function seasonName(n) { return n === 0 ? JL.t('specials') : JL.t('season', { n: n }); }

  // ---- selection model -------------------------------------------------------

  /** Files of a preview → groups (a season, or a movie) of units (one video version and its subtitles). */
  JL.buildTree = function (files) {
    var units = {}, order = [];
    files.forEach(function (f) {
      var u = units[f.ItemId];
      if (!u) {
        u = units[f.ItemId] = { id: f.ItemId, season: f.SeasonNumber, episode: f.EpisodeNumber, title: f.Title,
          name: f.ItemName, version: f.VersionName, played: f.Played, bytes: 0, files: 0, subs: 0 };
        order.push(u);
      }
      u.bytes += f.Size;
      u.files++;
      if (f.Kind === 'subtitle') { u.subs++; }
    });

    var titles = {};
    order.forEach(function (u) { titles[u.title] = true; });
    var manyTitles = Object.keys(titles).length > 1;

    var groups = [], byKey = {};
    order.forEach(function (u) {
      var key = u.season == null ? 't:' + u.title : 's:' + u.title + ':' + u.season;
      var g = byKey[key];
      if (!g) {
        g = byKey[key] = { key: key, label: u.season == null ? u.title : (manyTitles ? u.title + ' · ' : '') + seasonName(u.season), units: [] };
        groups.push(g);
      }
      g.units.push(u);
    });
    // A version name only tells lines apart: a movie with a single version shows its title alone.
    groups.forEach(function (g) { if (g.units.length === 1) { g.units[0].version = null; } });
    return groups;
  };

  JL.unitLabel = function (u) {
    if (u.episode != null) { return 'E' + pad2(u.episode) + (u.name ? ' — ' + u.name : ''); }
    return u.version ? u.title + ' — ' + u.version : u.title;
  };

  JL.headline = function (groups) {
    var titles = {}, episodes = 0, seasons = 0;
    groups.forEach(function (g) {
      if (g.units[0].season != null) { seasons++; }
      g.units.forEach(function (u) { titles[u.title] = true; if (u.episode != null) { episodes++; } });
    });
    var names = Object.keys(titles);
    var parts = [names.length === 1 ? names[0] : JL.t('head.titles', { n: names.length })];
    if (seasons > 1) { parts.push(JL.t('head.seasons', { n: seasons })); }
    if (episodes) { parts.push(JL.t('head.episodes', { n: episodes })); }
    return parts.join(' · ');
  };

  JL.groupState = function (g, excluded) {
    var out = g.units.filter(function (u) { return excluded.has(u.id); }).length;
    return out === 0 ? 'all' : (out === g.units.length ? 'none' : 'some');
  };

  JL.toggleGroup = function (g, excluded) {
    var include = JL.groupState(g, excluded) !== 'all';
    g.units.forEach(function (u) { if (include) { excluded.delete(u.id); } else { excluded.add(u.id); } });
  };

  JL.toggleUnit = function (id, excluded) { if (excluded.has(id)) { excluded.delete(id); } else { excluded.add(id); } };

  JL.selectAll = function (excluded) { excluded.clear(); };

  JL.selectNone = function (groups, excluded) {
    groups.forEach(function (g) { g.units.forEach(function (u) { excluded.add(u.id); }); });
  };

  JL.unwatchedOnly = function (groups, excluded) {
    excluded.clear();
    groups.forEach(function (g) { g.units.forEach(function (u) { if (u.played) { excluded.add(u.id); } }); });
  };

  JL.totals = function (groups, excluded) {
    var t = { units: 0, files: 0, bytes: 0 };
    groups.forEach(function (g) {
      g.units.forEach(function (u) {
        if (!excluded.has(u.id)) { t.units++; t.files += u.files; t.bytes += u.bytes; }
      });
    });
    return t;
  };

  JL.linksText = function (batch) {
    var urls = batch.Links.map(function (l) { return l.Url; }).filter(Boolean);
    return urls.length ? urls.join('\n') + '\n' : '';
  };

  // ---- batches ---------------------------------------------------------------

  /** Same rules as the server's BatchScope.Title, so a title reads the same in the browser and in the activity log. */
  JL.scopeTitle = function (scope) {
    var titles = (scope && scope.Titles) || [];
    if (!titles.length) { return ''; }
    if (titles.length > 1) {
      var head = titles.slice(0, 2).map(function (x) { return x.Name; }).join(', ');
      return titles.length > 2 ? head + ' +' + (titles.length - 2) : head;
    }
    var only = titles[0], seasons = only.Seasons || [];
    if (!seasons.length) { return only.Name; }
    if (seasons.length > 1) { return only.Name + ' · ' + JL.t('head.seasons', { n: seasons.length }); }
    var e = seasons[0].Episodes || [], eps = '';
    if (e.length === 1) { eps = 'E' + pad2(e[0]); }
    else if (e.length > 1) { eps = e[e.length - 1] - e[0] === e.length - 1 ? 'E' + pad2(e[0]) + '–E' + pad2(e[e.length - 1]) : JL.t('head.episodes', { n: e.length }); }
    return only.Name + ' · ' + seasonName(seasons[0].Number) + (eps ? ' · ' + eps : '');
  };

  /** A batch made by 0.2.x has no scope: its stored title (already trimmed by the server) is shown as is. */
  JL.batchTitle = function (b) { return JL.scopeTitle(b.Scope) || b.Label; };

  JL.txtName = function (title) { return title.replace(/[\\/:*?"<>|]+/g, '-').trim() + '.txt'; };

  JL.batchView = function (b) {
    var n = b.FileCount, c = b.CompleteCount, done = n > 0 && c === n;
    var created = JL.formatDay(b.CreatedAt), expires = JL.formatDay(b.ExpiresAt);
    var v = { title: JL.batchTitle(b), pct: n ? Math.round((100 * c) / n) : 0, tone: '', icon: '', actions: [], section: 'history',
      size: JL.t('gen.total', { n: n, size: JL.formatBytes(b.TotalBytes) }),
      progress: done ? JL.t('mine.done') : JL.t('mine.progress', { n: c, total: n }) };
    if (b.State === 'active') {
      v.section = 'active';
      v.actions = ['copy', 'revoke'];
      v.meta = JL.t('mine.meta', { created: created, expires: expires });
      v.status = v.progress;
      if (done) { v.tone = 'ok'; v.icon = 'check_circle'; }
    } else if (b.State === 'expired') {
      v.actions = ['regenerate'];
      v.tone = 'off'; v.icon = 'schedule';
      v.meta = JL.t('mine.metaExpired', { created: created, expires: expires });
      v.status = JL.t('state.expired') + ' · ' + v.progress;
    } else if (b.State === 'blocked') {
      v.tone = 'bad'; v.icon = 'block';
      v.meta = JL.t('mine.metaCreated', { created: created });
      v.status = JL.t('mine.blocked');
    } else {
      v.tone = 'off'; v.icon = 'link_off';
      v.meta = JL.t('mine.metaCreated', { created: created });
      v.status = JL.t('state.revoked');
    }
    return v;
  };

  JL.fileStatus = function (link, batchState) {
    if (link.Status === 'complete') { return JL.t('file.complete'); }
    return batchState === 'active' ? JL.t('file.' + link.Status) : JL.t('file.notDownloaded');
  };

  JL.quotaLine = function (q) {
    if (!q || !q.Enabled) { return null; }
    var used = JL.formatBytes(q.UsedBytes);
    var parts = [q.VolumeBytes > 0 ? JL.t('quota.used', { used: used, volume: JL.formatBytes(q.VolumeBytes) }) : JL.t('quota.usedOnly', { used: used }),
      JL.t('quota.window', { n: q.PeriodDays })];
    if (q.MaxActiveBatches > 0) { parts.push(JL.t('quota.batches', { n: q.ActiveBatches, max: q.MaxActiveBatches })); }
    var pct = q.VolumeBytes > 0 ? Math.min(100, Math.round((100 * q.UsedBytes) / q.VolumeBytes)) : 0;
    return { text: JL.t('quota.label') + ' · ' + parts.join(' · '), pct: pct, warn: pct >= 80 };
  };

  JL.quotaRemaining = function (q) {
    return q && q.Enabled && q.VolumeBytes > 0 ? JL.t('gen.quotaLeft', { size: JL.formatBytes(Math.max(0, q.VolumeBytes - q.UsedBytes)) }) : '';
  };

  // ---- messages --------------------------------------------------------------

  function quotaError(q) {
    if (q.MaxActiveBatches > 0 && q.ActiveBatches >= q.MaxActiveBatches) { return JL.t('err.quota.batches', { n: q.MaxActiveBatches }); }
    var text = JL.t('err.quota.volume', { used: JL.formatBytes(q.UsedBytes), volume: JL.formatBytes(q.VolumeBytes), n: q.PeriodDays });
    return q.FreesAt ? text + ' ' + JL.t('err.quota.frees', { date: JL.formatDate(q.FreesAt) }) : text;
  }

  /** What went wrong and what to do: err is what JL.api rejects with ({ status, body }) or a bare status. */
  JL.errorText = function (err) {
    var status = err && typeof err === 'object' ? err.status : err;
    var body = err && typeof err === 'object' ? err.body : null;
    if (status === 429 && body) { return quotaError(body); }
    if (body && body.Code) { return JL.t('err.' + body.Code, body.Args); }
    if (status === 401) { return JL.t('err.401'); }
    if (status === 403) { return JL.t('err.403'); }
    if (status === 404 || status === 410) { return JL.t('err.gone'); }
    if (status === 409) { return JL.t('err.conflict'); }
    return status ? JL.t('err.server') : JL.t('err.unreachable');
  };

  /** A coded server message ({ Code, Args }) in the reader's language: *Bytes as sizes, a limit of 0 as ∞, 0.2.x text as is. */
  JL.msgText = function (msg, prefix) {
    if (!msg) { return ''; }
    var src = msg.Args || {}, a = {};
    if (msg.Code === 'text') { return src.text || ''; }
    Object.keys(src).forEach(function (k) {
      var v = src[k];
      a[k] = /Bytes$/.test(k) ? JL.formatBytes(Number(v)) : (k === 'limit' && String(v) === '0' ? '∞' : v);
    });
    if (msg.Code === 'session') { a.client = JL.shortUa(src.ua); a.status = JL.t('session.' + src.status); }
    return JL.t(prefix + msg.Code, a);
  };

  /** "Chrome · Windows", "JDownloader", "curl": the full string stays available as a tooltip. */
  JL.shortUa = function (ua) {
    if (!ua) { return '—'; }
    var tool = /(JDownloader|aria2|curl|Wget|yt-dlp|Motrix|qBittorrent|Transmission|VLC|Kodi|python-requests|Go-http-client|okhttp)/i.exec(ua);
    if (tool) { return tool[1]; }
    var browser = /Edg\//.test(ua) ? 'Edge' : /OPR\//.test(ua) ? 'Opera' : /Firefox\//.test(ua) ? 'Firefox'
      : /Chrome\//.test(ua) ? 'Chrome' : /Safari\//.test(ua) ? 'Safari' : null;
    var os = /Windows/.test(ua) ? 'Windows' : /iPhone|iPad|iPod/.test(ua) ? 'iOS' : /Android/.test(ua) ? 'Android'
      : /Mac OS X|Macintosh/.test(ua) ? 'macOS' : /Linux/.test(ua) ? 'Linux' : null;
    if (browser) { return os ? browser + ' · ' + os : browser; }
    return ua.split(/[\s/]/)[0] || ua;
  };

  // ---- colours ---------------------------------------------------------------

  JL.isTransparent = function (css) {
    return !css || css === 'transparent' || /^rgba\([^)]*,\s*0(\.0+)?\s*\)$/.test(css);
  };

  /** Categorical colours for the admin chart: none of them reads as a status (red, amber, green). */
  JL.PALETTE = ['#5b8def', '#a274e8', '#22b8cf', '#e879b5', '#3f5bb5', '#c7a6f5'];

  /** The 6 largest users keep a colour, given in id order so it does not move from day to day; the others share none (null). */
  JL.userColors = function (totals) {
    var ids = Object.keys(totals);
    var top = ids.slice().sort(function (a, b) { return totals[b] - totals[a] || (a < b ? -1 : 1); }).slice(0, JL.PALETTE.length).sort();
    var map = {};
    ids.forEach(function (id) { var i = top.indexOf(id); map[id] = i < 0 ? null : JL.PALETTE[i]; });
    return map;
  };

  // ---- DOM (runs in the Jellyfin web client only) ------------------------------

  JL.el = function (tag, props, children) {
    var e = document.createElement(tag);
    Object.keys(props || {}).forEach(function (k) {
      var v = props[k];
      if (k === 'text') { e.textContent = v; }
      else if (k === 'on') { Object.keys(v).forEach(function (ev) { e.addEventListener(ev, v[ev]); }); }
      else if (k === 'attrs') { Object.keys(v).forEach(function (a) { e.setAttribute(a, v[a]); }); }
      else { e[k] = v; }
    });
    (children || []).forEach(function (c) {
      if (c != null && c !== '') { e.appendChild(typeof c === 'string' ? document.createTextNode(c) : c); }
    });
    return e;
  };

  JL.icon = function (name, extra) {
    return JL.el('span', { className: 'material-icons ' + (extra ? extra + ' ' : '') + name, attrs: { 'aria-hidden': 'true' } });
  };

  /** Calls the plugin API with the signed-in user's token. Rejects with { status, body } (status 0 when unreachable). */
  JL.api = function (method, path, body) {
    var opts = { type: method, url: ApiClient.getUrl('JellyLinks/' + path) };
    if (body !== undefined) { opts.data = JSON.stringify(body); opts.contentType = 'application/json'; }
    return ApiClient.ajax(opts).then(function (r) {
      if (!r || typeof r.json !== 'function') { return r; }
      return r.status === 204 ? null : r.json();
    }, function (r) {
      var status = (r && r.status) || 0;
      if (r && typeof r.json === 'function') {
        return r.json().then(function (b) { throw { status: status, body: b }; }, function () { throw { status: status, body: null }; });
      }
      throw { status: status, body: null };
    });
  };

  // Every colour is a token on .jlRoot; the only literals are fallbacks of Jellyfin's own variables.
  var STYLE = [
    '.jlRoot{--jl-accent:var(--jf-palette-primary-main,#00a4dc);--jl-on-accent:var(--jf-palette-primary-contrastText,#fff);',
    '--jl-divider:var(--jf-palette-divider,rgba(128,128,128,.25));--jl-muted:var(--jf-palette-text-secondary,rgba(128,128,128,1));',
    '--jl-radius:var(--jf-card-borderRadius,.2em);--jl-surface:transparent;',
    '--jl-ok-fg:var(--jf-palette-Alert-successColor,#66bb6a);--jl-ok-bg:var(--jf-palette-Alert-successStandardBg,rgba(102,187,106,.15));',
    '--jl-warn-fg:var(--jf-palette-Alert-warningColor,#ffa726);--jl-warn-bg:var(--jf-palette-Alert-warningStandardBg,rgba(255,167,38,.15));',
    '--jl-bad-fg:var(--jf-palette-Alert-errorColor,#ef5350);--jl-bad-bg:var(--jf-palette-Alert-errorStandardBg,rgba(198,40,40,.15));',
    '--jl-off-fg:var(--jf-palette-text-disabled,rgba(128,128,128,1));--jl-off-bg:var(--jf-palette-action-selected,rgba(128,128,128,.15))}',
    '.jlRoot[hidden],.jlRoot [hidden]{display:none!important}',
    '.dialogContainer.jlTop{align-items:flex-start;padding-top:5vh;box-sizing:border-box}',
    '.jlDialog{width:min(640px,94vw);max-height:90vh;display:flex;flex-direction:column}',
    '.jlDialog.jlSmall{width:min(440px,94vw)}',
    '.jlDialog.jlFull{position:fixed;top:0;left:0;width:100vw;max-width:none;height:100%;max-height:none;margin:0;border-radius:0}',
    '.jlDialog .formDialogContent{flex:1 1 auto;overflow-y:auto}',
    '.jlBody{padding-top:.5em;padding-bottom:1em}',
    '.jlRoot button:disabled{opacity:.3;cursor:default}',
    '.jlRow{display:flex;justify-content:space-between;align-items:center;gap:1em;min-height:2.8em;border-bottom:1px solid var(--jl-divider)}',
    '.jlRow>:first-child{min-width:0;flex:1 1 auto}',
    '.jlRow .checkboxContainer{margin:0}',
    '.jlRow .checkboxLabel,.jlEllipsis{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;display:block}',
    '.jlIndent{padding-left:2em}',
    '.jlNum,.jlNum.fieldDescription{white-space:nowrap!important;font-variant-numeric:tabular-nums}',
    '.jlChips{display:flex;flex-wrap:wrap;gap:.5em;padding:.6em 0}',
    '.jlChips .emby-button,.jlActions .emby-button,.jlFooter .emby-button{margin:0;min-height:40px}',
    '.jlDisclose{display:flex;width:100%;align-items:center;justify-content:space-between;min-height:48px;padding:0;background:none;border:0;',
    'border-bottom:1px solid var(--jl-divider);color:inherit;font:inherit;cursor:pointer;text-align:left}',
    '.jlExpand{flex:0 0 auto}',
    '.jlFooter{display:flex;gap:.5em;justify-content:flex-end;align-items:center;flex-wrap:wrap;box-sizing:border-box}',
    '.jlFooter:empty{display:none}',
    '.jlHint{flex:1 1 100%;text-align:right}',
    '.jlBar{height:6px;background:var(--jl-divider);border-radius:3px;overflow:hidden;margin:.5em 0}',
    '.jlBar>i{display:block;height:100%;background:var(--jl-accent)}',
    '.jlBar.jlWarn>i{background:var(--jl-warn-fg)}',
    '.jlBar.jlBarOff>i{background:var(--jl-off-fg)}',
    '.jlPill{display:inline-flex;align-items:center;gap:.3em;padding:.15em .6em;border-radius:1em;font-size:.85em;white-space:nowrap}',
    '.jlPill .material-icons{font-size:1.15em}',
    '.jl-ok{color:var(--jl-ok-fg);background:var(--jl-ok-bg)}.jl-warn{color:var(--jl-warn-fg);background:var(--jl-warn-bg)}',
    '.jl-bad{color:var(--jl-bad-fg);background:var(--jl-bad-bg)}.jl-off{color:var(--jl-off-fg);background:var(--jl-off-bg)}',
    '.jlCard{border-radius:var(--jl-radius);padding:.8em 1em;margin-bottom:.7em}',
    '.jlCardHead{display:flex;align-items:center;gap:.5em;width:100%;min-height:40px;padding:0;background:none;border:0;color:inherit;font:inherit;text-align:left;cursor:pointer}',
    '.jlTitle{flex:1 1 auto;min-width:0;font-weight:600}',
    '.jlActions{display:flex;flex-wrap:wrap;gap:.3em;margin-top:.5em}',
    '.jlState{padding:2em 1em;text-align:center}',
    '.jlState>.material-icons{font-size:3em;opacity:.7}',
    '.jlSpin{display:inline-block;width:1.8em;height:1.8em;border:.22em solid var(--jl-divider);border-top-color:var(--jl-accent);border-radius:50%;animation:jlSpin .8s linear infinite}',
    '@keyframes jlSpin{to{transform:rotate(360deg)}}',
    '@media (prefers-reduced-motion:reduce){.jlSpin{animation-duration:2.4s}}',
    '.jlFade{opacity:.5;pointer-events:none}',
    '.jlAlert{padding:.7em .9em;border-radius:var(--jl-radius);margin:.6em 0;display:flex;gap:.8em;align-items:center;flex-wrap:wrap}',
    '.jlSticky{position:sticky;top:0;z-index:1;background:var(--jl-surface);padding:.3em 0}',
    '.jlSection{margin:1.2em 0 .4em}',
    '.jlManual{width:100%;min-height:8em;box-sizing:border-box}',
    '.jlTable{width:100%;border-collapse:collapse;margin:.5em 0;font-size:.95em}',
    '.jlTable th,.jlTable td{padding:.5em .4em;border-bottom:1px solid var(--jl-divider);text-align:left;vertical-align:top}',
    '.jlTable th{color:var(--jl-muted);font-weight:500}',
    '.jlTable tr.jlClick{cursor:pointer}',
    '.jlTable tr.jlClick:hover,.jlTable tr.jlClick:focus{background:var(--jf-palette-action-hover,rgba(128,128,128,.12))}',
    '.jlTable td.jlChev{width:1.5em;color:var(--jl-muted)}',
    '@media (max-width:600px){.jlTable thead{display:none}.jlTable tr{display:block;padding:.6em 0;border-bottom:1px solid var(--jl-divider)}',
    '.jlTable td{display:flex;justify-content:space-between;gap:1em;border:0;padding:.15em 0}',
    '.jlTable td::before{content:attr(data-label);color:var(--jl-muted)}.jlTable td.jlChev{display:none}}',
    '.jlSr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}',
    '.jlRoot .jlFieldError{color:var(--jl-bad-fg)}',
    ':where(.toastContainer){position:fixed;bottom:1.5em;left:1.5em;z-index:10000}',
    ':where(.jlToast){background:var(--jf-palette-SnackbarContent-bg,#323232);color:var(--jf-palette-SnackbarContent-color,#fff);padding:.8em 1.2em;border-radius:.3em;margin-top:.5em}',
    '@media (max-width:600px){.dialogContainer.jlTop{padding-top:0}}'
  ].join('\n');

  JL.injectStyle = function () {
    if (document.getElementById('jlStyle')) { return; }
    document.head.appendChild(JL.el('style', { id: 'jlStyle', text: STYLE }));
  };

  /** The theme's accent, read from a hidden native submit button: community themes restyle buttons, not Jellyfin's variables. */
  JL.themeAccent = function () {
    var b = JL.el('button', { type: 'button', className: 'raised button-submit emby-button',
      attrs: { 'aria-hidden': 'true', tabindex: '-1', style: 'position:absolute;left:-9999px;visibility:hidden;pointer-events:none' } });
    document.body.appendChild(b);
    var cs = getComputedStyle(b), r = { bg: cs.backgroundColor, fg: cs.color };
    b.remove();
    return JL.isTransparent(r.bg) ? null : r;
  };

  /** Marks root as a JellyLinks surface and pins the live accent (and, for sticky bars, the surface colour) on it. */
  JL.applyTheme = function (root, surface) {
    JL.injectStyle();
    root.classList.add('jlRoot');
    var a = JL.themeAccent();
    if (a) { root.style.setProperty('--jl-accent', a.bg); root.style.setProperty('--jl-on-accent', a.fg); }
    if (surface) {
      var bg = getComputedStyle(surface).backgroundColor;
      if (!JL.isTransparent(bg)) { root.style.setProperty('--jl-surface', bg); }
    }
  };

  /** A native button. kind: 'submit' | 'cancel' | 'delete' (raised) or 'flat' | 'link'. */
  JL.button = function (text, kind, onClick, iconName) {
    var raised = kind === 'submit' || kind === 'cancel' || kind === 'delete';
    return JL.el('button', { type: 'button', className: (raised ? 'raised button-' + kind : 'button-' + kind) + ' emby-button',
      attrs: { is: 'emby-button' }, on: { click: onClick } }, [iconName ? JL.icon(iconName) : null, JL.el('span', { text: text })]);
  };

  /**
   * A native checkbox (emby-checkbox, restyled by every theme). state: true/false or 'all' | 'none' | 'some';
   * key restores focus across re-renders (data-k).
   */
  JL.checkbox = function (text, state, onChange, key) {
    var input = JL.el('input', { type: 'checkbox', checked: state === true || state === 'all' || state === 'some',
      attrs: { is: 'emby-checkbox' }, on: { change: function () { onChange(input.checked); } } });
    if (state === 'some') { input.setAttribute('data-mixed', '1'); input.setAttribute('aria-checked', 'mixed'); }
    if (key) { input.setAttribute('data-k', key); }
    return JL.el('label', { className: 'checkboxContainer' }, [input, JL.el('span', { className: 'checkboxLabel', text: text })]);
  };

  /** emby-checkbox has no mixed state: a mixed box shows checked with Jellyfin's "remove" glyph. Run after the boxes are in the page. */
  JL.fixMixed = function (root) {
    Array.prototype.forEach.call(root.querySelectorAll('input[data-mixed]'), function (i) {
      var icon = i.parentNode.querySelector('.checkboxIcon-checked');
      if (icon) { icon.classList.remove('check'); icon.classList.add('remove'); }
    });
  };

  JL.spinner = function () {
    return JL.el('div', { className: 'jlState' }, [JL.el('span', { className: 'jlSpin', attrs: { role: 'progressbar', 'aria-label': JL.t('loading') } })]);
  };

  /** Jellyfin's own toast markup, so themes style it; announced to screen readers. */
  JL.toast = function (text) {
    var box = document.querySelector('.toastContainer');
    if (!box) { box = document.body.appendChild(JL.el('div', { className: 'toastContainer' })); }
    var t = JL.el('div', { className: 'toast jlToast', text: text, attrs: { role: 'status', 'aria-live': 'polite' } });
    box.appendChild(t);
    requestAnimationFrame(function () { t.classList.add('toastVisible'); });
    setTimeout(function () { t.classList.remove('toastVisible'); setTimeout(function () { t.remove(); }, 300); }, 3300);
  };

  /**
   * Runs fn once a Jellyfin action sheet has closed. The sheet closes with history.back(), whose popstate
   * arrives after the item's click: a dialog opened before it would close on it. Falls back after `wait` ms.
   */
  JL.afterSheetClose = function (win, fn, wait) {
    var done = false;
    var run = function () {
      if (done) { return; }
      done = true;
      win.removeEventListener('popstate', run);
      fn();
    };
    win.addEventListener('popstate', run);
    setTimeout(run, wait);
  };

  var stack = [];

  /**
   * A native Jellyfin form dialog, themed like the page: full screen with a back arrow on phones (as Jellyfin's own),
   * top-anchored elsewhere so it grows downward. Escape (topmost only), backdrop, the header button, back and route
   * changes close it. opts.small: a centred confirmation; opts.onClose: called once closed.
   */
  JL.openDialog = function (title, opts) {
    opts = opts || {};
    JL.injectStyle();
    var full = !opts.small && window.matchMedia && window.matchMedia('(max-width: 600px)').matches;
    var before = document.activeElement;
    var backdrop = JL.el('div', { className: 'dialogBackdrop dialogBackdropOpened' });
    var content = JL.el('div', { className: 'dialogContentInner dialog-content-centered padded-left padded-right jlBody' });
    var footer = JL.el('div', { className: 'formDialogFooter formDialogFooter-flex jlFooter' });
    var container, dialog, downOnBackdrop = false;
    function focusables() {
      return Array.prototype.filter.call(dialog.querySelectorAll('button, [href], input, textarea, select, [tabindex]:not([tabindex="-1"])'),
        function (n) { return !n.disabled && n.offsetParent !== null; });
    }
    var onKey = function (e) {
      if (stack[stack.length - 1] !== close) { return; } // only the topmost dialog reacts
      if (e.key === 'Escape') { e.stopPropagation(); close(); return; }
      if (e.key === 'Tab') {
        var f = focusables();
        if (!f.length) { return; }
        if (!dialog.contains(document.activeElement)) { e.preventDefault(); f[0].focus(); return; } // content was replaced: focus fell to the page
        var first = f[0], last = f[f.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
      }
    };
    var onNav = function () { close(); };
    var close = function () {
      var i = stack.indexOf(close);
      if (i < 0) { return; }
      stack.splice(i, 1);
      document.removeEventListener('keydown', onKey, true);
      window.removeEventListener('popstate', onNav);
      window.removeEventListener('hashchange', onNav);
      backdrop.remove();
      container.remove();
      if (before && typeof before.focus === 'function') { before.focus(); }
      if (opts.onClose) { opts.onClose(); }
    };
    var label = JL.t(full ? 'back' : 'close');
    dialog = JL.el('div', { className: 'focuscontainer dialog formDialog opened jlDialog' + (opts.small ? ' jlSmall centeredDialog' : full ? ' jlFull' : ' centeredDialog'),
      attrs: { role: 'dialog', 'aria-modal': 'true', 'aria-label': title } }, [
      JL.el('div', { className: 'formDialogHeader' }, [
        JL.el('button', { type: 'button', className: 'btnCancel autoSize paper-icon-button-light', title: label, attrs: { 'aria-label': label }, on: { click: close } },
          [JL.icon(full ? 'arrow_back' : 'close')]),
        JL.el('h3', { className: 'formDialogHeaderTitle', text: title })
      ]),
      JL.el('div', { className: 'formDialogContent smoothScrollY' }, [content]),
      footer
    ]);
    container = JL.el('div', { className: 'dialogContainer' + (opts.small ? '' : ' jlTop'), on: {
      mousedown: function (e) { downOnBackdrop = e.target === container; },
      click: function (e) { if (e.target === container && downOnBackdrop) { close(); } downOnBackdrop = false; }
    } }, [dialog]);
    stack.push(close);
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('popstate', onNav);
    window.addEventListener('hashchange', onNav);
    document.body.appendChild(backdrop);
    document.body.appendChild(container);
    JL.applyTheme(dialog, dialog);
    setTimeout(function () { var f = focusables(); if (f.length) { f[0].focus(); } }, 0);
    return { content: content, footer: footer, close: close, dialog: dialog };
  };

  /** A small native confirmation. opts: title, text, ok, danger, typed (a word to type before the button wakes up). */
  JL.confirm = function (opts) {
    return new Promise(function (resolve) {
      var answered = false, d;
      function answer(v) { answered = true; resolve(v); d.close(); }
      d = JL.openDialog(opts.title, { small: true, onClose: function () { if (!answered) { resolve(false); } } });
      var ok = JL.button(opts.ok, opts.danger ? 'delete' : 'submit', function () { answer(true); });
      var nodes = [JL.el('p', { text: opts.text })];
      if (opts.typed) {
        var input = JL.el('input', { type: 'text', id: 'jlTyped', attrs: { is: 'emby-input', autocomplete: 'off' },
          on: { input: function () { ok.disabled = input.value.trim() !== opts.typed; } } });
        ok.disabled = true;
        nodes.push(JL.el('div', { className: 'inputContainer' }, [
          JL.el('label', { className: 'inputLabel', attrs: { 'for': 'jlTyped' }, text: JL.t('all.confirmLabel', { word: opts.typed }) }), input]));
      }
      d.content.replaceChildren.apply(d.content, nodes);
      d.footer.replaceChildren(JL.button(JL.t('cancel'), 'cancel', function () { answer(false); }), ok);
    });
  };

  function legacyCopy(text) {
    var prev = document.activeElement;
    var ta = JL.el('textarea', { value: text, attrs: { readonly: '', style: 'position:fixed;opacity:0;top:0;left:0' } });
    document.body.appendChild(ta);
    ta.select();
    ta.setSelectionRange(0, text.length); // iOS selects nothing with select() alone
    var ok = false;
    try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
    ta.remove();
    if (prev && prev.isConnected && typeof prev.focus === 'function') { prev.focus(); }
    return ok;
  }

  /** Resolves true when the text reached the clipboard. Plain http has no Clipboard API: fall back to execCommand. */
  JL.copyText = function (text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return legacyCopy(text); });
    }
    return Promise.resolve(legacyCopy(text));
  };

  JL.downloadText = function (name, text) {
    var url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
    var a = JL.el('a', { href: url, download: name });
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  };

  if (typeof module !== 'undefined' && module.exports) { module.exports = JL; }
})();
