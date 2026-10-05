// JellyLinks client: generate — the "Download links" dialog.
(function (JL) {
  'use strict';
  var el = JL.el, t = JL.t;

  function sizeCell(text) { return el('span', { className: 'jlNum fieldDescription', text: text }); }

  JL.openGenerate = function (rootIds) {
    var s = { allVersions: false, subs: true, expanded: false, excluded: new Set(), groups: [], preview: null,
      batch: null, pending: null, gen: 0, open: {}, error: null, loaded: false, lastAction: null };
    var d = JL.openDialog(t('gen.title'));

    function setFooter(nodes) { d.footer.replaceChildren.apply(d.footer, nodes); }

    function request(withExclusions) {
      var body = { RootItemIds: rootIds, AllVersions: s.allVersions, IncludeSubtitles: s.subs };
      if (withExclusions) { body.ExcludedItemIds = Array.from(s.excluded); }
      return body;
    }

    // Every load() starts a new generation: answers (preview or batch) from an older one are ignored.
    function load() {
      var gen = ++s.gen;
      s.batch = null; s.pending = null; s.error = null;
      if (s.loaded) { d.content.classList.add('jlFade'); } else { d.content.replaceChildren(JL.spinner()); setFooter([]); }
      JL.api('POST', 'batches/preview', request(false)).then(function (p) {
        if (gen !== s.gen) { return; }
        d.content.classList.remove('jlFade');
        s.preview = p;
        s.loaded = true;
        s.groups = JL.buildTree(p.Files);
        if (s.groups.length === 1) { s.open[s.groups[0].key] = true; }
        render();
      }, function (err) {
        if (gen !== s.gen) { return; }
        d.content.classList.remove('jlFade');
        d.content.replaceChildren(el('div', { className: 'jlAlert jl-bad', text: JL.errorText(err), attrs: { role: 'alert' } }));
        setFooter([JL.button(t('close'), 'cancel', d.close), JL.button(t('retry'), 'submit', load)]);
      });
    }

    function unitCount() { return s.groups.reduce(function (n, g) { return n + g.units.length; }, 0); }

    function groupRows(g) {
      var picked = g.units.filter(function (u) { return !s.excluded.has(u.id); });
      var bytes = picked.reduce(function (n, u) { return n + u.bytes; }, 0);
      var open = !!s.open[g.key];
      var rows = [el('div', { className: 'jlRow' }, [
        JL.checkbox(g.label, JL.groupState(g, s.excluded), function () { JL.toggleGroup(g, s.excluded); render(); }, 'g:' + g.key),
        sizeCell(picked.length + '/' + g.units.length + ' · ' + JL.formatBytes(bytes)),
        el('button', { type: 'button', className: 'paper-icon-button-light jlExpand',
          attrs: { 'aria-expanded': String(open), 'aria-label': g.label, 'data-k': 'o:' + g.key },
          on: { click: function () { s.open[g.key] = !open; render(); } } }, [JL.icon(open ? 'expand_less' : 'expand_more')])
      ])];
      if (open) {
        g.units.forEach(function (u) {
          rows.push(el('div', { className: 'jlRow jlIndent' }, [
            JL.checkbox(JL.unitLabel(u) + (u.played ? ' · ' + t('gen.played') : ''), !s.excluded.has(u.id),
              function () { JL.toggleUnit(u.id, s.excluded); render(); }, 'u:' + u.id),
            sizeCell(JL.formatBytes(u.bytes) + (u.subs ? ' · ' + t('gen.subsCount', { n: u.subs }) : ''))
          ]));
        });
      }
      return rows;
    }

    function render() {
      if (!s.groups.length) {
        d.content.replaceChildren(el('div', { className: 'jlState' }, [JL.icon('link_off'), el('p', { text: t('gen.nothing') })]));
        setFooter([JL.button(t('close'), 'submit', d.close)]);
        return;
      }
      var active = document.activeElement;
      var focused = active && d.dialog.contains(active) ? active.getAttribute('data-k') : null;
      var tot = JL.totals(s.groups, s.excluded), p = s.preview, nodes = [];
      if (s.error) {
        nodes.push(el('div', { className: 'jlAlert jl-bad', attrs: { role: 'alert' } }, [
          el('span', { text: s.error }),
          JL.button(t('retry'), 'flat', function () { s.error = null; if (s.lastAction) { s.lastAction(); } })
        ]));
      }
      nodes.push(el('p', { className: 'fieldDescription', text: JL.headline(s.groups) }));
      if (p.HasOtherVersions) {
        nodes.push(el('div', { className: 'jlRow' }, [JL.checkbox(t('gen.versions'), s.allVersions, function (v) { s.allVersions = v; load(); }, 'versions')]));
      }
      if (p.HasSubtitles) {
        nodes.push(el('div', { className: 'jlRow' }, [JL.checkbox(t('gen.subs'), s.subs, function (v) { s.subs = v; load(); }, 'subs')]));
      }
      if (unitCount() > 1) {
        var episodes = s.groups.some(function (g) { return g.units[0].episode != null; });
        nodes.push(el('button', { type: 'button', className: 'jlDisclose', attrs: { 'aria-expanded': String(s.expanded), 'data-k': 'disclose' },
          on: { click: function () { s.expanded = !s.expanded; render(); } } },
          [el('span', { text: s.expanded ? t('gen.hide') : t(episodes ? 'gen.chooseEpisodes' : 'gen.chooseTitles') }),
            JL.icon(s.expanded ? 'expand_less' : 'expand_more')]));
        if (s.expanded) {
          var chips = [
            JL.button(t('gen.all'), 'flat', function () { JL.selectAll(s.excluded); render(); }),
            JL.button(t('gen.none'), 'flat', function () { JL.selectNone(s.groups, s.excluded); render(); })
          ];
          if (s.groups.some(function (g) { return g.units.some(function (u) { return u.played; }); })) {
            chips.push(JL.button(t('gen.unplayed'), 'flat', function () { JL.unwatchedOnly(s.groups, s.excluded); render(); }));
          }
          nodes.push(el('div', { className: 'jlChips' }, chips));
          s.groups.forEach(function (g) { nodes = nodes.concat(groupRows(g)); });
        }
      }
      nodes.push(el('div', { className: 'jlRow' }, [el('b', { text: t('gen.selection') }), sizeCell(t('gen.total', { n: tot.files, size: JL.formatBytes(tot.bytes) }))]));
      nodes.push(el('div', { className: 'jlRow' }, [el('span', { text: t('gen.validUntil', { date: JL.formatDate(p.ExpiresAt) }) }), sizeCell(JL.quotaRemaining(p.Quota))]));
      d.content.replaceChildren.apply(d.content, nodes);
      footer(tot);
      requestAnimationFrame(function () { JL.fixMixed(d.content); });
      if (focused) {
        var back = d.dialog.querySelector('[data-k="' + CSS.escape(focused) + '"]');
        if (back) { back.focus(); }
      }
    }

    function footer(tot) {
      var off = tot.units === 0 || !!s.pending;
      var txt = JL.button(t('gen.txt'), 'flat', function () { act(saveTxt); }, 'download');
      var copy = JL.button(t('gen.copy'), 'submit', function () { act(copyLinks); }, 'content_copy');
      txt.disabled = off;
      copy.disabled = off;
      setFooter(tot.units === 0 ? [el('span', { className: 'fieldDescription jlHint', text: t('err.empty') }), txt, copy] : [txt, copy]);
    }

    function act(fn) { s.lastAction = function () { act(fn); }; withBatch(fn); }

    // One dialog creates at most one batch: a click while the POST is in flight chains on it,
    // and the other button then reuses the batch instead of creating a second one.
    function withBatch(fn) {
      if (s.batch) { fn(s.batch); return; }
      var gen = s.gen;
      if (!s.pending) {
        s.error = null;
        s.pending = JL.api('POST', 'batches', request(true)).then(function (b) {
          if (gen === s.gen) { s.batch = b; s.pending = null; }
          return b;
        }, function (err) {
          if (gen === s.gen) { s.pending = null; s.error = JL.errorText(err); render(); }
          throw err;
        });
        render(); // the buttons wait for the answer
      }
      s.pending.then(function (b) { if (gen === s.gen) { fn(b); } }, function () { /* shown by render */ });
    }

    function done(b, title) {
      JL.toast(title);
      d.content.replaceChildren(el('div', { className: 'jlState' }, [
        JL.icon('check_circle'),
        el('h3', { text: title }),
        el('p', { className: 'fieldDescription', text: t('gen.doneHint', { date: JL.formatDate(b.ExpiresAt) }) })
      ]));
      var close = JL.button(t('close'), 'submit', d.close);
      setFooter([
        JL.button(t('gen.txt'), 'flat', function () { saveTxt(b); }, 'download'),
        JL.button(t('gen.viewMine'), 'flat', function () { d.close(); if (JL.openMyLinks) { JL.openMyLinks(); } }),
        close
      ]);
      close.focus();
    }

    function copyLinks(b) {
      JL.copyText(JL.linksText(b)).then(function (ok) {
        if (ok) { done(b, t('gen.copied', { n: b.Links.length })); } else { manual(b, t('gen.manual')); }
      });
    }

    function saveTxt(b) {
      // Jellyfin app shells (WebViews) ignore blob downloads: show the links as text instead.
      if (window.NativeShell) { manual(b, t('gen.manualFile')); return; }
      JL.downloadText(JL.txtName(JL.batchTitle(b)), JL.linksText(b));
      done(b, t('gen.saved'));
    }

    function manual(b, text) {
      var ta = el('textarea', { className: 'jlManual', value: JL.linksText(b), attrs: { readonly: '', 'aria-label': t('gen.title') } });
      d.content.replaceChildren(el('p', { text: text }), ta);
      setFooter([JL.button(t('close'), 'submit', d.close)]);
      ta.focus();
      ta.select();
    }

    load();
  };
})(window.JellyLinks);
