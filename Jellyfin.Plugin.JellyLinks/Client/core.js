// JellyLinks client: core — formatting, selection model, batch cards, API and dialog helpers.
(function () {
  'use strict';
  var JL = typeof window !== 'undefined' ? (window.JellyLinks = window.JellyLinks || {}) : {};
  var FR = 'fr-FR';
  var UNITS = ['o', 'Ko', 'Mo', 'Go', 'To'];

  // ---- pure --------------------------------------------------------------

  JL.formatBytes = function (n) {
    var i = 0;
    while (n >= 1000 && i < UNITS.length - 1) { n /= 1000; i++; }
    return (i === 0 ? String(n) : n.toLocaleString(FR, { minimumFractionDigits: 1, maximumFractionDigits: 1 })) + ' ' + UNITS[i];
  };

  JL.formatDate = function (unixSeconds) {
    return new Date(unixSeconds * 1000).toLocaleDateString(FR, { day: 'numeric', month: 'short', year: 'numeric' });
  };

  JL.plural = function (n, one, many) { return n + ' ' + (n > 1 ? many : one); };

  function pad2(n) { return n < 10 ? '0' + n : String(n); }

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
        var season = u.season === 0 ? 'Spéciaux' : 'Saison ' + u.season;
        g = byKey[key] = { key: key, label: u.season == null ? u.title : (manyTitles ? u.title + ' — ' : '') + season, units: [] };
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
    var titles = {}, units = 0, episodes = 0, seasons = 0;
    groups.forEach(function (g) {
      if (g.units[0].season != null) { seasons++; }
      g.units.forEach(function (u) { titles[u.title] = true; units++; if (u.episode != null) { episodes++; } });
    });
    var names = Object.keys(titles);
    var parts = [names.length === 1 ? names[0] : names.length + ' titres'];
    if (seasons > 1) { parts.push(seasons + ' saisons'); }
    if (episodes) { parts.push(JL.plural(episodes, 'épisode', 'épisodes')); }
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

  JL.labelTitle = function (label) { return label.split(' · ')[0]; };

  JL.txtName = function (label) { return JL.labelTitle(label).replace(/[\\/:*?"<>|]+/g, '-').trim() + '.txt'; };

  JL.FILE_STATUS = { pending: 'en attente', in_progress: 'en cours', complete: 'terminé', interrupted: 'interrompu', abandoned: 'abandonné' };

  JL.batchView = function (b) {
    var n = b.FileCount, c = b.CompleteCount, done = n > 0 && c === n;
    var v = { pct: n ? Math.round((100 * c) / n) : 0, actions: [], note: '', status: '', tone: '' };
    if (b.State === 'blocked') {
      v.status = 'bloqué'; v.tone = 'bad'; v.note = "contacte l'administrateur";
    } else if (b.State === 'revoked') {
      v.status = 'révoqué'; v.tone = 'off';
    } else if (b.State === 'expired') {
      v.status = done ? 'terminé' : 'abandonné ' + c + '/' + n; v.tone = done ? 'ok' : 'off'; v.actions = ['regenerate'];
    } else {
      v.status = done ? 'terminé' : c + '/' + n + ' terminés'; v.tone = done ? 'ok' : 'warn'; v.actions = ['copy', 'revoke'];
    }
    v.expires = b.State === 'active' ? 'expire le ' + JL.formatDate(b.ExpiresAt) : (b.State === 'expired' ? 'expiré' : '—');
    v.size = JL.plural(n, 'fichier', 'fichiers') + ' · ' + JL.formatBytes(b.TotalBytes);
    return v;
  };

  JL.quotaLine = function (q) {
    if (!q || !q.Enabled) { return null; }
    var used = JL.formatBytes(q.UsedBytes) + ' utilisés' + (q.VolumeBytes > 0 ? ' sur ' + JL.formatBytes(q.VolumeBytes) : '');
    var text = 'Quota : ' + used + ' · période glissante de ' + JL.plural(q.PeriodDays, 'jour', 'jours');
    if (q.MaxActiveBatches > 0) { text += ' · lots actifs ' + q.ActiveBatches + '/' + q.MaxActiveBatches; }
    return { text: text, pct: q.VolumeBytes > 0 ? Math.min(100, Math.round((100 * q.UsedBytes) / q.VolumeBytes)) : 0 };
  };

  JL.quotaRemaining = function (q) {
    return q && q.Enabled && q.VolumeBytes > 0 ? 'quota restant ' + JL.formatBytes(Math.max(0, q.VolumeBytes - q.UsedBytes)) : '';
  };

  JL.errorMessage = function (status) {
    if (status === 429) { return 'Quota atteint : impossible de créer un lot pour le moment.'; }
    if (status === 400) { return 'Rien à lier : sélection vide ou téléchargement non autorisé.'; }
    if (status === 401) { return 'Session expirée : reconnecte-toi puis réessaie.'; }
    if (status === 410) { return 'Ce lot ou ce fichier n’existe plus.'; }
    if (status === 403) { return 'Action refusée.'; }
    if (status === 404) { return 'Lot introuvable.'; }
    return status ? 'Erreur du serveur (' + status + ').' : 'Serveur injoignable.';
  };

  // ---- DOM (runs in the Jellyfin web client only) --------------------------

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

  /** Calls the plugin API with the signed-in user's token. Rejects with the HTTP status (0 when unreachable). */
  JL.api = function (method, path, body) {
    var opts = { type: method, url: ApiClient.getUrl('JellyLinks/' + path) };
    if (body !== undefined) { opts.data = JSON.stringify(body); opts.contentType = 'application/json'; }
    return ApiClient.ajax(opts).then(function (r) {
      if (!r || typeof r.json !== 'function') { return r; }
      return r.status === 204 ? null : r.json();
    }, function (r) { throw (r && r.status) || 0; });
  };

  var STYLE = [
    '.jlDialog{width:min(640px,94vw);max-height:90vh}',
    '.jlBody{padding-top:.5em;padding-bottom:.5em}',
    '.jlMuted{opacity:.65;font-size:.9em}',
    '.jlRow{display:flex;justify-content:space-between;align-items:center;gap:1em;padding:.5em 0;border-bottom:1px solid rgba(128,128,128,.2)}',
    '.jlInd{padding-left:1.8em}',
    '.jlSeg{display:inline-flex;border:1px solid rgba(128,128,128,.4);border-radius:6px;overflow:hidden}',
    '.jlSeg button{background:none;border:0;color:inherit;padding:.3em .8em;cursor:pointer;font:inherit}',
    '.jlSeg button.jlOn{background:#00a4dc;color:#fff}',
    '.jlLink{background:none;border:0;color:#00a4dc;cursor:pointer;padding:0;font:inherit}',
    '.jlCheck{background:none;border:0;color:#00a4dc;cursor:pointer;font-size:1.15em;padding:0 .4em 0 0;font-family:inherit}',
    '.jlTools{display:flex;gap:1em;padding:.5em 0}',
    '.jlFooter{display:flex;gap:.5em;justify-content:flex-end;box-sizing:border-box}',
    '.jlBar{height:8px;background:rgba(128,128,128,.25);border-radius:4px;overflow:hidden;margin:.3em 0 .8em}',
    '.jlBar i{display:block;height:100%;background:#00a4dc}',
    '.jlBar.jlDim i{background:#777}',
    '.jlCard{background:rgba(128,128,128,.1);border-radius:8px;padding:.8em;margin-bottom:.6em}',
    '.jlCardTop{display:flex;justify-content:space-between;gap:1em;cursor:pointer}',
    '.jl-ok{color:#3fb950}.jl-warn{color:#f0b429}.jl-off{color:#8b8b8b}.jl-bad{color:#f85149}',
    '.jlMsg{padding:.6em 0}',
    '.jlClip{position:fixed;opacity:0;top:0;left:0}',
    '.jlManual{width:100%;min-height:8em;box-sizing:border-box}',
    '.jlFiles{width:100%;border-collapse:collapse;margin-top:.5em;font-size:.9em}',
    '.jlFiles td{padding:.3em .2em;border-bottom:1px solid rgba(128,128,128,.15)}'
  ].join('\n');

  JL.injectStyle = function () {
    if (document.getElementById('jlStyle')) { return; }
    document.head.appendChild(JL.el('style', { id: 'jlStyle', text: STYLE }));
  };

  var stack = [];

  /** A native-looking Jellyfin dialog (legacy dialog CSS exists on 10.11 and 12). Escape (topmost only), backdrop, ✕, back and route changes close it. */
  JL.openDialog = function (title) {
    JL.injectStyle();
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
    };
    dialog = JL.el('div', { className: 'focuscontainer dialog formDialog opened centeredDialog jlDialog', attrs: { role: 'dialog', 'aria-modal': 'true', 'aria-label': title } }, [
      JL.el('div', { className: 'formDialogHeader' }, [
        JL.el('button', { type: 'button', className: 'btnCancel autoSize paper-icon-button-light', title: 'Fermer', attrs: { 'aria-label': 'Fermer' }, on: { click: close } }, [JL.icon('close')]),
        JL.el('h3', { className: 'formDialogHeaderTitle', text: title })
      ]),
      JL.el('div', { className: 'formDialogContent smoothScrollY' }, [content]),
      footer
    ]);
    container = JL.el('div', { className: 'dialogContainer', on: {
      mousedown: function (e) { downOnBackdrop = e.target === container; },
      click: function (e) { if (e.target === container && downOnBackdrop) { close(); } downOnBackdrop = false; }
    } }, [dialog]);
    stack.push(close);
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('popstate', onNav);
    window.addEventListener('hashchange', onNav);
    document.body.appendChild(backdrop);
    document.body.appendChild(container);
    setTimeout(function () { var f = focusables(); if (f.length) { f[0].focus(); } }, 0);
    return { content: content, footer: footer, close: close };
  };

  function legacyCopy(text) {
    var ta = JL.el('textarea', { value: text, className: 'jlClip', attrs: { readonly: '' } });
    document.body.appendChild(ta);
    ta.select();
    ta.setSelectionRange(0, text.length); // iOS selects nothing with select() alone
    var ok = false;
    try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
    ta.remove();
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
