// JellyLinks client: generate — the "Liens de téléchargement" dialog.
(function (JL) {
  'use strict';
  var el = JL.el;

  function link(text, fn) { return el('button', { type: 'button', className: 'jlLink', text: text, on: { click: fn } }); }
  function row(label, right, extra) { return el('div', { className: 'jlRow' + (extra ? ' ' + extra : '') }, [typeof label === 'string' ? el('span', { text: label }) : label, right]); }
  function check(state, fn) {
    var glyph = state === 'all' ? '☑' : (state === 'some' ? '◩' : '☐');
    return el('button', { type: 'button', className: 'jlCheck', text: glyph, attrs: { 'aria-pressed': String(state === 'all') }, on: { click: fn } });
  }
  function action(text, primary, disabled, fn) {
    return el('button', { type: 'button', className: 'raised emby-button ' + (primary ? 'button-submit' : 'button-cancel'), disabled: disabled, on: { click: fn } }, [el('span', { text: text })]);
  }
  function totalText(t) { return JL.plural(t.files, 'fichier', 'fichiers') + ' · ' + JL.formatBytes(t.bytes); }

  JL.openGenerate = function (rootIds) {
    var s = { allVersions: false, subs: true, expanded: false, excluded: new Set(), groups: [], preview: null, batch: null, pending: null, gen: 0, open: {} };
    var d = JL.openDialog('Liens de téléchargement');

    function message(text) { d.content.replaceChildren(el('div', { className: 'jlMsg', text: text })); }

    // Every load() starts a new generation: answers (preview or batch) from an older one are ignored.
    function load() {
      var gen = ++s.gen;
      s.batch = null;
      s.pending = null;
      d.footer.replaceChildren();
      message('Chargement…');
      JL.api('POST', 'batches/preview', { RootItemIds: rootIds, AllVersions: s.allVersions, IncludeSubtitles: s.subs }).then(function (p) {
        if (gen !== s.gen) { return; }
        s.preview = p;
        s.groups = JL.buildTree(p.Files);
        if (s.groups.length === 1) { s.open[s.groups[0].key] = true; }
        render();
      }, function (status) { if (gen === s.gen) { message(JL.errorMessage(status)); } });
    }

    function busy(on) { Array.prototype.forEach.call(d.footer.querySelectorAll('button'), function (b) { b.disabled = on; }); }

    function versions() {
      var b = function (text, all) {
        return el('button', { type: 'button', className: s.allVersions === all ? 'jlOn' : '', text: text,
          on: { click: function () { if (s.allVersions !== all) { s.allVersions = all; load(); } } } });
      };
      return el('span', { className: 'jlSeg' }, [b('Par défaut', false), b('Toutes', true)]);
    }

    function subtitles() {
      return el('span', null, [check(s.subs ? 'all' : 'none', function () { s.subs = !s.subs; load(); }), s.subs ? 'inclus' : 'exclus']);
    }

    function groupRows(g) {
      var state = JL.groupState(g, s.excluded);
      var picked = g.units.filter(function (u) { return !s.excluded.has(u.id); });
      var bytes = picked.reduce(function (n, u) { return n + u.bytes; }, 0);
      var head = el('span', null, [
        check(state, function () { JL.toggleGroup(g, s.excluded); render(); }),
        link((s.open[g.key] ? '▾ ' : '▸ ') + g.label, function () { s.open[g.key] = !s.open[g.key]; render(); })
      ]);
      var rows = [row(head, el('span', { className: 'jlMuted', text: picked.length + '/' + g.units.length + ' · ' + JL.formatBytes(bytes) }))];
      if (s.open[g.key]) {
        g.units.forEach(function (u) {
          var label = el('span', null, [
            check(s.excluded.has(u.id) ? 'none' : 'all', function () { JL.toggleUnit(u.id, s.excluded); render(); }),
            JL.unitLabel(u),
            u.played ? el('span', { className: 'jlMuted', text: ' · vu' }) : null
          ]);
          rows.push(row(label, el('span', { className: 'jlMuted', text: JL.formatBytes(u.bytes) + (u.subs ? ' · ' + u.subs + ' st' : '') }), 'jlInd'));
        });
      }
      return rows;
    }

    function render() {
      if (!s.groups.length) { message('Aucun fichier téléchargeable dans cette sélection.'); return; }
      var t = JL.totals(s.groups, s.excluded);
      var p = s.preview;
      var rows = [];
      if (!s.expanded) {
        rows.push(el('div', { className: 'jlMuted', text: JL.headline(s.groups) }));
        rows.push(row('Versions', versions()));
        rows.push(row('Sous-titres externes', subtitles()));
        rows.push(row('Total', el('span', null, [el('b', { text: totalText(t) }), ' · ', link('ajuster ▸', function () { s.expanded = true; render(); })])));
      } else {
        rows.push(el('div', { className: 'jlMuted' }, [JL.headline(s.groups) + ' · ', link('◂ replier', function () { s.expanded = false; render(); })]));
        rows.push(row('Versions', versions()));
        rows.push(row('Sous-titres externes', subtitles()));
        rows.push(el('div', { className: 'jlTools' }, [
          link('Tout', function () { JL.selectAll(s.excluded); render(); }),
          link('Rien', function () { JL.selectNone(s.groups, s.excluded); render(); }),
          link('Non vus seulement', function () { JL.unwatchedOnly(s.groups, s.excluded); render(); })
        ]));
        s.groups.forEach(function (g) { rows = rows.concat(groupRows(g)); });
        rows.push(row('Sélection', el('b', { text: totalText(t) })));
      }
      var remaining = JL.quotaRemaining(p.Quota);
      rows.push(row("Valables jusqu'au", el('span', { text: JL.formatDate(p.ExpiresAt) + (remaining ? ' · ' + remaining : '') })));
      d.content.replaceChildren.apply(d.content, rows);
      d.footer.replaceChildren(
        action('Télécharger .txt', false, t.units === 0 || !!s.pending, function () {
          withBatch(function (b) { JL.downloadText(JL.txtName(b.Label), JL.linksText(b)); done(b, 'Fichier .txt téléchargé.'); });
        }),
        action('Copier les liens', true, t.units === 0 || !!s.pending, function () {
          withBatch(function (b) {
            JL.copyText(JL.linksText(b)).then(function (ok) {
              if (ok) { done(b, JL.plural(b.Links.length, 'lien copié', 'liens copiés') + ' dans le presse-papier.'); } else { manual(b); }
            });
          });
        }));
    }

    // One dialog creates at most one batch: a click while the POST is in flight chains on it,
    // and the other button then reuses the batch instead of creating a second one.
    function withBatch(fn) {
      if (s.batch) { fn(s.batch); return; }
      var gen = s.gen;
      if (!s.pending) {
        busy(true);
        s.pending = JL.api('POST', 'batches', { RootItemIds: rootIds, AllVersions: s.allVersions, IncludeSubtitles: s.subs, ExcludedItemIds: Array.from(s.excluded) })
          .then(function (b) {
            if (gen === s.gen) { s.batch = b; s.pending = null; busy(false); }
            return b;
          }, function (status) {
            if (gen === s.gen) { s.pending = null; busy(false); message(JL.errorMessage(status)); }
            throw status;
          });
      }
      s.pending.then(function (b) { if (gen === s.gen) { fn(b); } }, function () { /* already shown */ });
    }

    function done(b, text) {
      d.content.replaceChildren(
        el('div', { className: 'jlMsg', text: text }),
        el('div', { className: 'jlMuted', text: JL.labelTitle(b.Label) + " · valable jusqu'au " + JL.formatDate(b.ExpiresAt) + ' · retrouve ce lot dans « Mes liens ».' }));
    }

    function manual(b) {
      d.content.replaceChildren(
        el('div', { className: 'jlMsg', text: 'Copie automatique impossible ici : sélectionne le texte ci-dessous et copie-le.' }),
        el('textarea', { className: 'jlManual', value: JL.linksText(b), attrs: { readonly: '' } }));
    }

    load();
  };
})(window.JellyLinks);
